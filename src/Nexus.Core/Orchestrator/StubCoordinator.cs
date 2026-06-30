namespace Nexus.Core.Orchestrator;

public sealed class StubCoordinator : ICoordinator
{
    /// <summary>
    /// Returns a canned 2-task decomposition: auth (no deps) and booking (depends on auth).
    /// </summary>
    public Task<IReadOnlyList<SubTask>> DecomposeAsync(string instruction, CancellationToken ct)
    {
        var tasks = new List<SubTask>
        {
            new("auth", instruction, new[] { "src/auth/**" }, Array.Empty<string>()),
            new("booking", instruction, new[] { "src/booking/**" }, new[] { "auth" })
        };
        return Task.FromResult<IReadOnlyList<SubTask>>(tasks);
    }
}

public record SubTask(string ModuleName, string Instruction, string[] OwnsFiles, string[] DependsOn);
