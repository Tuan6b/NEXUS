using System.Diagnostics;

namespace Nexus.Core.Git;

// FR-27: Bare shadow repo (.nexus/shadow.git) + one git worktree per agent module.
// Agents write only within their worktree; NEXUS union-copies changed files back at merge time.
// The bare repo never receives remote pushes — it is local-only by design (§6.1).
public sealed class ShadowRepo
{
    private const string RootBranch = "nexus-root";

    private readonly string _nexusDir;
    private string ShadowGitDir => Path.Combine(_nexusDir, "shadow.git");
    private string WorktreesRoot => Path.Combine(_nexusDir, "worktrees");

    public ShadowRepo(string nexusDir) => _nexusDir = nexusDir;

    // Creates shadow.git (bare) with one empty initial commit so worktree-add works.
    // Idempotent: returns immediately if shadow.git already exists.
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (File.Exists(Path.Combine(ShadowGitDir, "HEAD")))
            return;

        Directory.CreateDirectory(ShadowGitDir);
        await GitAsync(ShadowGitDir, ct, "init", "--bare");

        // An empty tree commit gives the bare repo at least one ref so worktree-add works.
        // 4b825dc... is the well-known SHA1 of the empty tree — stable across all git versions.
        const string emptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        var sha = (await GitWithIdentityAsync(ShadowGitDir, ct,
            "commit-tree", emptyTree, "-m", "nexus: init shadow")).Trim();

        await GitAsync(ShadowGitDir, ct, "update-ref", $"refs/heads/{RootBranch}", sha);
        await GitAsync(ShadowGitDir, ct, "symbolic-ref", "HEAD", $"refs/heads/{RootBranch}");
    }

    // Creates .nexus/worktrees/{module} on a fresh branch forked from the root commit.
    // Idempotent: returns immediately if the worktree directory already exists.
    public async Task EnsureWorktreeAsync(string module, CancellationToken ct = default)
    {
        var worktreePath = GetWorktreePath(module);
        if (Directory.Exists(worktreePath))
            return;

        Directory.CreateDirectory(WorktreesRoot);
        await GitAsync(ShadowGitDir, ct, "worktree", "add", worktreePath, "-b", module, RootBranch);
    }

    // Absolute path to the worktree directory for the given module.
    public string GetWorktreePath(string module) =>
        Path.Combine(WorktreesRoot, module);

    // FR-29: Returns the project repo's HEAD hash before agents are spawned.
    // Stored by the caller; compared at merge time to detect dev edits during the run.
    // Returns null if the project root is not a git repo (safe to skip the guard).
    public static async Task<string?> TryGetProjectHeadHashAsync(
        string projectRoot, CancellationToken ct = default)
    {
        try
        {
            return (await GitAsync(projectRoot, ct, "rev-parse", "HEAD")).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static Task<string> GitAsync(string workDir, CancellationToken ct, params string[] args) =>
        RunGitAsync(workDir, ct, withIdentity: false, args);

    private static Task<string> GitWithIdentityAsync(string workDir, CancellationToken ct, params string[] args) =>
        RunGitAsync(workDir, ct, withIdentity: true, args);

    private static async Task<string> RunGitAsync(
        string workDir, CancellationToken ct, bool withIdentity, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (withIdentity)
        {
            // commit-tree requires author/committer identity.
            // Fixed values make the bare repo work in CI without global git config.
            psi.Environment["GIT_AUTHOR_NAME"] = "nexus";
            psi.Environment["GIT_AUTHOR_EMAIL"] = "nexus@local";
            psi.Environment["GIT_COMMITTER_NAME"] = "nexus";
            psi.Environment["GIT_COMMITTER_EMAIL"] = "nexus@local";
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("'git' not found in PATH");

        using var killReg = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } });
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(" ", args)} failed (exit {p.ExitCode}): {stderr.Trim()}");

        return stdout;
    }
}
