using Nexus.Core.Domain;
using Nexus.Core.Pipeline;

namespace Nexus.Core.Adapters;

public sealed class StubAdapter : IAgentAdapter
{
    private readonly IEventSink _sink;

    public string Type => "stub";
    public AgentSource Source => AgentSource.Cli;

    public StubAdapter(IEventSink sink)
    {
        _sink = sink;
    }

    public Task<bool> DetectInstalledAsync() => Task.FromResult(true);

    public async Task<AgentRunResult> RunAsync(AgentTask task, CancellationToken ct)
    {
        var progresses = new[] { 10, 30, 60, 90, 100 };
        foreach (var pct in progresses)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(400, ct);
            await _sink.PublishProgressAsync(new ProgressEvent(task.TaskId, pct, $"stub: step {pct}%"), ct);
        }

        var fakeDiff = $"--- a/{task.OwnsFiles.FirstOrDefault() ?? "file"}\n+++ b/{task.OwnsFiles.FirstOrDefault() ?? "file"}\n@@ stub diff @@";
        return new AgentRunResult(task.TaskId, true, null, fakeDiff, null);
    }
}
