using Microsoft.Data.Sqlite;
using Nexus.Core.Adapters;
using Nexus.Core.Domain;
using Nexus.Core.Orchestrator;
using Nexus.Core.Pipeline;
using Nexus.State;
using Nexus.State.Repositories;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.Core.Tests.Orchestration;

/// <summary>
/// Integration tests for the orchestrator + event pipeline + SQLite state machine.
/// Uses an in-memory SQLite database; no Avalonia UI involved.
/// </summary>
public sealed class StateMachineTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly TaskRepository _taskRepo;
    private readonly AgentRepository _agentRepo;
    private readonly EventPipeline _pipeline;
    private readonly NexusOrchestrator _orchestrator;

    public StateMachineTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        DatabaseInitializer.EnsureSchema(_db);

        _taskRepo = new TaskRepository(_db);
        _agentRepo = new AgentRepository(_db);
        _pipeline = new EventPipeline();

        // Wire the single-writer handler (mirrors AppHost.HandleEventAsync)
        _pipeline.RegisterHandler(HandleEventAsync);

        var coordinator = new StubCoordinator();
        var adapter = new StubAdapter(_pipeline);
        _orchestrator = new NexusOrchestrator(coordinator, adapter, _pipeline);
    }

    // ── UC-01 / FR-01 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_CreatesTwoPendingTasks()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Capture tasks as they arrive from TaskCreatedEvent — status is Pending at creation time.
        var createdTasks = new List<TaskItem>();
        var allCreated = new TaskCompletionSource();
        _pipeline.RegisterHandler(evt =>
        {
            if (evt is TaskCreatedEvent e)
            {
                lock (createdTasks)
                {
                    createdTasks.Add(e.Task);
                    if (createdTasks.Count == 2) allCreated.TrySetResult();
                }
            }
            return Task.CompletedTask;
        });

        var consumerTask = _pipeline.RunConsumerLoopAsync(cts.Token);
        await _orchestrator.SubmitAsync("build login feature", cts.Token);
        await allCreated.Task.WaitAsync(cts.Token);

        Assert.Equal(2, createdTasks.Count);
        Assert.All(createdTasks, t => Assert.Equal(TaskStatus.Pending, t.Status));

        await cts.CancelAsync();
        await SafeAwait(consumerTask);
    }

    // ── UC-01 / FR-15: dependency ordering ─────────────────────────────────────

    [Fact]
    public async Task DependencyOrder_BookingStaysPendingWhileAuthRuns()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var consumerTask = _pipeline.RunConsumerLoopAsync(cts.Token);

        var authRunning = new TaskCompletionSource();
        var statusLog = new List<(string TaskId, TaskStatus Status)>();

        _pipeline.RegisterHandler(evt =>
        {
            if (evt is TaskStatusChangedEvent e)
            {
                lock (statusLog) statusLog.Add((e.TaskId, e.NewStatus));
                if (e.TaskId == "auth" && e.NewStatus == TaskStatus.Running)
                    authRunning.TrySetResult();
            }
            return Task.CompletedTask;
        });

        await _orchestrator.SubmitAsync("build login feature", cts.Token);

        // Wait until auth transitions to Running
        await authRunning.Task.WaitAsync(cts.Token);

        // At this point booking must still be Pending — not yet eligible
        var bookingTask = await _taskRepo.GetByIdAsync("booking");
        Assert.NotNull(bookingTask);
        Assert.Equal(TaskStatus.Pending, bookingTask!.Status);

        await cts.CancelAsync();
        await SafeAwait(consumerTask);
    }

    // ── UC-02 / StubAdapter: progress ticks reach 100% ─────────────────────────

    [Fact]
    public async Task StubAdapter_EmitsFiveProgressTicksAndCompletes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var progressPercents = new List<int>();
        // Gate on the 5th progress event, not on Done — the 100% progress tick lands in
        // the _progress channel while Done lands in _critical; consumer drains critical
        // first, so Done can fire before 100% is dispatched.
        var allProgressReceived = new TaskCompletionSource();

        _pipeline.RegisterHandler(evt =>
        {
            if (evt is ProgressEvent p && p.TaskId == "auth")
            {
                lock (progressPercents)
                {
                    progressPercents.Add(p.Percent);
                    if (progressPercents.Count == 5) allProgressReceived.TrySetResult();
                }
            }
            return Task.CompletedTask;
        });

        var consumerTask = _pipeline.RunConsumerLoopAsync(cts.Token);
        await _orchestrator.SubmitAsync("build login feature", cts.Token);
        await allProgressReceived.Task.WaitAsync(cts.Token);

        Assert.Equal(5, progressPercents.Count);
        Assert.Equal(new[] { 10, 30, 60, 90, 100 }, progressPercents.ToArray());

        await cts.CancelAsync();
        await SafeAwait(consumerTask);
    }

    // ── UC-02 / dependency: booking runs only after auth Done ──────────────────

    [Fact]
    public async Task BothTasksComplete_InDependencyOrder()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var consumerTask = _pipeline.RunConsumerLoopAsync(cts.Token);

        var doneOrder = new List<string>();
        var bothDone = new TaskCompletionSource();

        _pipeline.RegisterHandler(evt =>
        {
            if (evt is TaskStatusChangedEvent { NewStatus: TaskStatus.Done } e)
            {
                lock (doneOrder)
                {
                    doneOrder.Add(e.TaskId);
                    if (doneOrder.Count == 2) bothDone.TrySetResult();
                }
            }
            return Task.CompletedTask;
        });

        await _orchestrator.SubmitAsync("build login feature", cts.Token);
        await bothDone.Task.WaitAsync(cts.Token);

        Assert.Equal("auth", doneOrder[0]);
        Assert.Equal("booking", doneOrder[1]);

        var allTasks = (await _taskRepo.LoadAllAsync()).ToList();
        Assert.All(allTasks, t => Assert.Equal(TaskStatus.Done, t.Status));

        await cts.CancelAsync();
        await SafeAwait(consumerTask);
    }

    // ── Crash recovery (UC-02 / AppHost.StartAsync) ────────────────────────────

    [Fact]
    public async Task CrashRecovery_LoadOpenTasksReturnsRunningAndPending()
    {
        // Seed two tasks with open statuses directly into SQLite
        var runningTask = new TaskItem("auth", "stub", TaskStatus.Running, 60,
            new[] { "src/auth/**" }, Array.Empty<string>(), 0);
        var pendingTask = new TaskItem("booking", "stub", TaskStatus.Pending, 0,
            new[] { "src/booking/**" }, new[] { "auth" }, 0);

        await _taskRepo.UpsertAsync(runningTask);
        await _taskRepo.UpsertAsync(pendingTask);

        var open = (await _taskRepo.LoadOpenTasksAsync()).ToList();

        Assert.Equal(2, open.Count);
        Assert.Contains(open, t => t.Id == "auth" && t.Status == TaskStatus.Running);
        Assert.Contains(open, t => t.Id == "booking" && t.Status == TaskStatus.Pending);
    }

    // ── UC-01 Alt 4a: circular dependency detection ────────────────────────────

    [Fact]
    public void FindCycle_NoDependencies_ReturnsNull()
    {
        var tasks = new List<SubTask>
        {
            new("auth",    "instr", new[] { "src/auth/**" },    Array.Empty<string>()),
            new("booking", "instr", new[] { "src/booking/**" }, Array.Empty<string>()),
        };
        Assert.Null(NexusOrchestrator.FindCycle(tasks));
    }

    [Fact]
    public void FindCycle_LinearDependency_ReturnsNull()
    {
        var tasks = new List<SubTask>
        {
            new("auth",    "instr", new[] { "src/auth/**" },    Array.Empty<string>()),
            new("booking", "instr", new[] { "src/booking/**" }, new[] { "auth" }),
        };
        Assert.Null(NexusOrchestrator.FindCycle(tasks));
    }

    [Fact]
    public void FindCycle_DirectCycle_ReturnsCyclePath()
    {
        var tasks = new List<SubTask>
        {
            new("auth",    "instr", new[] { "src/auth/**" },    new[] { "booking" }),
            new("booking", "instr", new[] { "src/booking/**" }, new[] { "auth" }),
        };
        var cycle = NexusOrchestrator.FindCycle(tasks);
        Assert.NotNull(cycle);
        Assert.Contains("auth", cycle!);
        Assert.Contains("booking", cycle);
    }

    [Fact]
    public void FindCycle_ThreewayChain_ReturnsNull()
    {
        var tasks = new List<SubTask>
        {
            new("a", "i", new[] { "src/a" }, Array.Empty<string>()),
            new("b", "i", new[] { "src/b" }, new[] { "a" }),
            new("c", "i", new[] { "src/c" }, new[] { "b" }),
        };
        Assert.Null(NexusOrchestrator.FindCycle(tasks));
    }

    [Fact]
    public void FindCycle_ThreewayCycle_ReturnsCyclePath()
    {
        var tasks = new List<SubTask>
        {
            new("a", "i", new[] { "src/a" }, new[] { "c" }),
            new("b", "i", new[] { "src/b" }, new[] { "a" }),
            new("c", "i", new[] { "src/c" }, new[] { "b" }),
        };
        var cycle = NexusOrchestrator.FindCycle(tasks);
        Assert.NotNull(cycle);
    }

    // ── single-writer: the handler is the ONLY SQLite writer ───────────────────

    private async Task HandleEventAsync(StateEvent evt)
    {
        switch (evt)
        {
            case TaskCreatedEvent e:
                await _taskRepo.UpsertAsync(e.Task);
                break;
            case TaskStatusChangedEvent e:
                var existing = await _taskRepo.GetByIdAsync(e.TaskId);
                if (existing is not null)
                    await _taskRepo.UpsertAsync(existing with { Status = e.NewStatus });
                break;
            case ProgressEvent e:
                var task = await _taskRepo.GetByIdAsync(e.TaskId);
                if (task is not null)
                    await _taskRepo.UpsertAsync(task with { ProgressPercent = e.Percent });
                break;
            case AgentRegisteredEvent e:
                await _agentRepo.UpsertAsync(e.Agent);
                break;
        }
    }

    private static async Task SafeAwait(Task t)
    {
        try { await t; }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _pipeline.Complete();
        _db.Dispose();
    }
}
