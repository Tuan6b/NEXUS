using Nexus.Core.Git;

namespace Nexus.Core.Tests.Git;

// Integration tests for ShadowRepo — run real git commands in a temp directory.
// IMPORTANT: all repos are created under Path.GetTempPath(), never inside this solution.
public sealed class ShadowRepoTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public ShadowRepoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nexus-shadow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EnsureInitializedAsync_CreatesBareShadowRepo()
    {
        var sut = new ShadowRepo(_tempDir);
        await sut.EnsureInitializedAsync();
        Assert.True(File.Exists(Path.Combine(_tempDir, "shadow.git", "HEAD")));
    }

    [Fact]
    public async Task EnsureInitializedAsync_IsIdempotent()
    {
        var sut = new ShadowRepo(_tempDir);
        await sut.EnsureInitializedAsync();
        var ex = await Record.ExceptionAsync(() => sut.EnsureInitializedAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task EnsureWorktreeAsync_CreatesWorktreeDirectory()
    {
        var sut = new ShadowRepo(_tempDir);
        await sut.EnsureInitializedAsync();
        await sut.EnsureWorktreeAsync("auth");
        Assert.True(Directory.Exists(sut.GetWorktreePath("auth")));
    }

    [Fact]
    public async Task EnsureWorktreeAsync_IsIdempotent()
    {
        var sut = new ShadowRepo(_tempDir);
        await sut.EnsureInitializedAsync();
        await sut.EnsureWorktreeAsync("auth");
        var ex = await Record.ExceptionAsync(() => sut.EnsureWorktreeAsync("auth"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task EnsureWorktreeAsync_TwoModules_BothDirectoriesExist()
    {
        var sut = new ShadowRepo(_tempDir);
        await sut.EnsureInitializedAsync();
        await sut.EnsureWorktreeAsync("auth");
        await sut.EnsureWorktreeAsync("booking");
        Assert.True(Directory.Exists(sut.GetWorktreePath("auth")));
        Assert.True(Directory.Exists(sut.GetWorktreePath("booking")));
    }

    [Fact]
    public void GetWorktreePath_ReturnsExpectedPath()
    {
        var sut = new ShadowRepo(_tempDir);
        var expected = Path.Combine(_tempDir, "worktrees", "auth");
        Assert.Equal(expected, sut.GetWorktreePath("auth"));
    }

    [Fact]
    public async Task TryGetProjectHeadHashAsync_NotAGitRepo_ReturnsNull()
    {
        var notARepo = Path.Combine(Path.GetTempPath(), $"nexus-no-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(notARepo);
        try
        {
            var hash = await ShadowRepo.TryGetProjectHeadHashAsync(notARepo);
            Assert.Null(hash);
        }
        finally
        {
            try { Directory.Delete(notARepo, recursive: true); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Windows may hold file handles on worktree index files briefly; suppress cleanup errors.
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        await Task.CompletedTask;
    }
}
