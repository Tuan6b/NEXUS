namespace Nexus.Core.Orchestrator;

public interface ICoordinator
{
    Task<IReadOnlyList<SubTask>> DecomposeAsync(string instruction, CancellationToken ct);

    // FR-03: generate a Java interface + JUnit 5 test for the given module.
    // Written into the module's worktree before the worker agent is spawned.
    Task<ContractGenerationResult> GenerateContractAsync(SubTask subTask, CancellationToken ct);
}
