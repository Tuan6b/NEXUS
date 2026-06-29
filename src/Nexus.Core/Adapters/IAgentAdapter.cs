using Nexus.Core.Domain;

namespace Nexus.Core.Adapters;

public interface IAgentAdapter
{
    string Type { get; }
    AgentSource Source { get; }
    Task<bool> DetectInstalledAsync();
    Task<AgentRunResult> RunAsync(AgentTask task, CancellationToken ct);
}
