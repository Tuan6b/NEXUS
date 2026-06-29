using Nexus.Core.Adapters;
using Nexus.Core.Domain;
using Nexus.Core.Pipeline;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.Core.Orchestrator;

public sealed class NexusOrchestrator
{
    private readonly StubCoordinator _coordinator;
    private readonly IAgentAdapter _adapter;
    private readonly IEventSink _sink;
    // taskId → instruction text; populated per SubmitAsync call, consumed in RunOneAsync
    private readonly Dictionary<string, string> _instructions = new(StringComparer.Ordinal);

    public NexusOrchestrator(StubCoordinator coordinator, IAgentAdapter adapter, IEventSink sink)
    {
        _coordinator = coordinator;
        _adapter = adapter;
        _sink = sink;
    }

    public async Task SubmitAsync(string instruction, CancellationToken ct)
    {
        var subTasks = await _coordinator.DecomposeAsync(instruction, ct);

        var cycle = FindCycle(subTasks);
        if (cycle is not null)
            throw new InvalidOperationException($"Circular dependency detected: {cycle}");

        var agentId = _adapter.Type;
        var items = subTasks
            .Select(s => new TaskItem(s.ModuleName, agentId, TaskStatus.Pending, 0, s.OwnsFiles, s.DependsOn, 0))
            .ToList();

        foreach (var s in subTasks)
            _instructions[s.ModuleName] = s.Instruction;

        foreach (var item in items)
            await _sink.PublishCriticalAsync(new TaskCreatedEvent(item), ct);

        // Instance ID is distinct from adapter type ("stub" → "stub-agent-1")
        var agentInfo = new AgentInfo($"{agentId}-agent-1", _adapter.Type, _adapter.Source, AgentLiveStatus.Alive, DateTimeOffset.UtcNow);
        await _sink.PublishCriticalAsync(new AgentRegisteredEvent(agentInfo), ct);

        _ = Task.Run(() => RunAllAsync(items, ct), ct)
              .ContinueWith(
                  t => ReportRunFailure(t.Exception!, items),
                  CancellationToken.None,
                  TaskContinuationOptions.OnlyOnFaulted,
                  TaskScheduler.Default);
    }

    private async Task RunAllAsync(List<TaskItem> items, CancellationToken ct)
    {
        var completed = new HashSet<string>();
        var remaining = new List<TaskItem>(items);

        while (remaining.Count > 0 && !ct.IsCancellationRequested)
        {
            var eligible = remaining
                .Where(t => t.DependsOn.All(dep => completed.Contains(dep)))
                .ToList();

            if (eligible.Count == 0)
            {
                await Task.Delay(200, ct);
                continue;
            }

            var runTasks = eligible.Select(t => RunOneAsync(t, ct)).ToList();
            var results = await Task.WhenAll(runTasks);

            foreach (var result in results)
            {
                if (result.Success)
                    completed.Add(result.TaskId);
                remaining.RemoveAll(t => t.Id == result.TaskId);
            }
        }
    }

    private async Task<AgentRunResult> RunOneAsync(TaskItem item, CancellationToken ct)
    {
        await _sink.PublishCriticalAsync(new TaskStatusChangedEvent(item.Id, TaskStatus.Running), ct);

        var instruction = _instructions.TryGetValue(item.Id, out var text) ? text : item.Id;
        var agentTask = new AgentTask(item.Id, instruction, item.OwnsFiles, ".nexus/worktrees/" + item.Id);
        var result = await _adapter.RunAsync(agentTask, ct);

        var finalStatus = result.Success ? TaskStatus.Done : TaskStatus.Failed;
        await _sink.PublishCriticalAsync(new TaskCompletedEvent(item.Id, result.Success, result.Error), ct);
        await _sink.PublishCriticalAsync(new TaskStatusChangedEvent(item.Id, finalStatus), ct);

        return result;
    }

    private void ReportRunFailure(AggregateException ex, List<TaskItem> items)
    {
        System.Diagnostics.Debug.WriteLine($"[NexusOrchestrator] RunAllAsync faulted: {ex.GetBaseException().Message}");
        foreach (var item in items)
            _ = _sink.PublishCriticalAsync(new TaskStatusChangedEvent(item.Id, TaskStatus.Failed)).AsTask();
    }

    // UC-01 Alt 4a: detect circular dependencies before spawning any tasks.
    // Returns a human-readable cycle path (e.g. "auth → booking → auth"), or null if clean.
    internal static string? FindCycle(IReadOnlyList<SubTask> tasks)
    {
        var graph = tasks.ToDictionary(t => t.ModuleName, t => t.DependsOn);
        var white = new HashSet<string>(tasks.Select(t => t.ModuleName));
        var gray = new HashSet<string>();

        foreach (var task in tasks)
        {
            if (!white.Contains(task.ModuleName)) continue;
            var path = new List<string>();
            if (DfsHasCycle(task.ModuleName, graph, white, gray, path))
                return string.Join(" → ", path);
        }
        return null;
    }

    internal static bool DfsHasCycle(string node, Dictionary<string, string[]> graph,
        HashSet<string> white, HashSet<string> gray, List<string> path)
    {
        white.Remove(node);
        gray.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                if (gray.Contains(dep)) { path.Add(dep); return true; }
                if (white.Contains(dep) && DfsHasCycle(dep, graph, white, gray, path)) return true;
            }
        }

        gray.Remove(node);
        path.RemoveAt(path.Count - 1);
        return false;
    }
}
