using Microsoft.Data.Sqlite;
using Nexus.Core.Adapters;
using Nexus.Core.Domain;
using Nexus.Core.Orchestrator;
using Nexus.Core.Pipeline;
using Nexus.State;
using Nexus.State.Repositories;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.App;

/// <summary>
/// Wires the pipeline, repositories, orchestrator, and UI notification callbacks.
/// The single consumer loop here is the ONLY writer to SQLite.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly EventPipeline _pipeline;
    private readonly SqliteConnection _db;
    private readonly TaskRepository _taskRepo;
    private readonly AgentRepository _agentRepo;
    private readonly NexusOrchestrator _orchestrator;
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumerTask;

    public event Action<TaskItem>? TaskCreated;
    public event Action<string, TaskStatus>? TaskStatusChanged;
    public event Action<string, int, string>? TaskProgressUpdated;
    public event Action<AgentInfo>? AgentRegistered;
    public event Action<IEnumerable<TaskItem>>? TasksRestored;

    public AppHost()
    {
        var dbPath = DatabaseInitializer.GetDbPath();
        _db = DatabaseInitializer.CreateConnection(dbPath);
        DatabaseInitializer.EnsureSchema(_db);

        _taskRepo = new TaskRepository(_db);
        _agentRepo = new AgentRepository(_db);
        _pipeline = new EventPipeline();

        // Register the single-writer handler
        _pipeline.RegisterHandler(HandleEventAsync);

        var coordinator = new StubCoordinator();
        var adapter = new StubAdapter(_pipeline);
        _orchestrator = new NexusOrchestrator(coordinator, adapter, _pipeline);
    }

    public async Task StartAsync()
    {
        // Crash recovery: reload open tasks from SQLite
        var openTasks = (await _taskRepo.LoadOpenTasksAsync()).ToList();
        if (openTasks.Count > 0)
            TasksRestored?.Invoke(openTasks);

        _consumerTask = _pipeline.RunConsumerLoopAsync(_cts.Token);
    }

    public Task SubmitTaskAsync(string instruction) =>
        _orchestrator.SubmitAsync(instruction, _cts.Token);

    private async Task HandleEventAsync(StateEvent evt)
    {
        // This is the ONLY place that writes to SQLite
        switch (evt)
        {
            case TaskCreatedEvent e:
                await _taskRepo.UpsertAsync(e.Task);
                TaskCreated?.Invoke(e.Task);
                break;

            case TaskStatusChangedEvent e:
                var existing = await _taskRepo.GetByIdAsync(e.TaskId);
                if (existing is not null)
                {
                    await _taskRepo.UpsertAsync(existing with { Status = e.NewStatus });
                    TaskStatusChanged?.Invoke(e.TaskId, e.NewStatus);
                }
                break;

            case ProgressEvent e:
                var task = await _taskRepo.GetByIdAsync(e.TaskId);
                if (task is not null)
                {
                    await _taskRepo.UpsertAsync(task with { ProgressPercent = e.Percent });
                    TaskProgressUpdated?.Invoke(e.TaskId, e.Percent, e.Note);
                }
                break;

            case AgentRegisteredEvent e:
                await _agentRepo.UpsertAsync(e.Agent);
                AgentRegistered?.Invoke(e.Agent);
                break;

            case TaskCompletedEvent:
                // Status is updated via TaskStatusChangedEvent emitted alongside
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _pipeline.Complete();
        if (_consumerTask is not null)
        {
            try { await _consumerTask; }
            catch (OperationCanceledException) { }
        }
        await _pipeline.DisposeAsync();
        _db.Dispose();
        _cts.Dispose();
    }
}
