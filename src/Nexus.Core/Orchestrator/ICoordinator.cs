namespace Nexus.Core.Orchestrator;

public interface ICoordinator
{
    Task<IReadOnlyList<SubTask>> DecomposeAsync(string instruction, CancellationToken ct);
}
