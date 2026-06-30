using Nexus.Core.Orchestrator;

namespace Nexus.Core.Tests.Orchestrator;

// Tests for the path-traversal guard in NexusOrchestrator.WriteFileAsync.
// Uses the internal WriteContractFilesAsync indirectly via StubCoordinator +
// a lightweight orchestrator invocation against a temp worktree directory.
public sealed class ContractWriteGuardTests : IDisposable
{
    private readonly string _worktree;

    public ContractWriteGuardTests()
    {
        _worktree = Path.Combine(Path.GetTempPath(), $"nexus-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_worktree);
    }

    [Theory]
    [InlineData("../../../etc/passwd", "code")]
    [InlineData("..\\..\\Windows\\system32\\evil.dll", "code")]
    [InlineData("/etc/cron.d/evil", "code")]
    [InlineData("C:\\Windows\\evil.exe", "code")]
    public async Task WriteContractFiles_TraversalPath_Throws(string badPath, string code)
    {
        var contract = new ContractGenerationResult(
            "auth", badPath, code, "src/test/Safe.java", "class Safe {}");

        // Invoke via reflection so we can test the private static helper directly.
        var method = typeof(NexusOrchestrator).GetMethod(
            "WriteContractFilesAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(null, [_worktree, contract, CancellationToken.None])!;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);

        // Both guard branches start with "Contract path ..."
        Assert.StartsWith("Contract path", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteContractFiles_ValidRelativePaths_WritesInsideWorktree()
    {
        var contract = new ContractGenerationResult(
            "auth",
            "src/main/java/IAuthService.java", "public interface IAuthService {}",
            "src/test/java/AuthServiceTest.java", "class AuthServiceTest {}");

        var method = typeof(NexusOrchestrator).GetMethod(
            "WriteContractFilesAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        await (Task)method!.Invoke(null, [_worktree, contract, CancellationToken.None])!;

        Assert.True(File.Exists(Path.Combine(_worktree, "src", "main", "java", "IAuthService.java")));
        Assert.True(File.Exists(Path.Combine(_worktree, "src", "test", "java", "AuthServiceTest.java")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_worktree, recursive: true); } catch { }
    }
}
