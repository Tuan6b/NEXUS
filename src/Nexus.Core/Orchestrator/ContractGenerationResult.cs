namespace Nexus.Core.Orchestrator;

// FR-03: Result of coordinator contract generation — Java interface + JUnit test written to a worktree.
public record ContractGenerationResult(
    string Module,
    string InterfacePath,
    string InterfaceCode,
    string TestPath,
    string TestCode);
