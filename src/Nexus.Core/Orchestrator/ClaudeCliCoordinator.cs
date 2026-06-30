using System.Diagnostics;

namespace Nexus.Core.Orchestrator;

// Coordinator that invokes the Claude Code CLI in headless print mode:
//   claude -p "<prompt>"
// Core design principle (§3): "Xài quota đã mua, không gọi API".
// All LLM calls go through the CLI (Plan Pro quota), never via the HTTP API.
//
// processRunner: injectable seam for unit tests — pass a fake to skip spawning a real process.
public sealed class ClaudeCliCoordinator : ICoordinator
{
    // §12: <<<JSON>>>/<<<END>>> delimiters let StdoutSanitizer extract the payload
    // reliably regardless of ANSI noise or update-check banners in the CLI output.
    private const string SystemPrompt =
        "You are an orchestration router. " +
        "Decompose the user request into disjoint modules with non-overlapping file ownership. " +
        "Each module must be independently implementable by a separate coding agent. " +
        "Output ONLY valid JSON wrapped exactly between <<<JSON>>> and <<<END>>> markers — " +
        "no preamble, no markdown fences, no trailing explanation. " +
        "Schema: {\"subtasks\":[{\"module\":\"<kebab-name>\",\"instruction\":\"<coding task>\",\"owns_files\":[\"<glob>\"],\"depends_on\":[\"<module>\"]}]}. " +
        "Rules: module names are unique lowercase kebab-case; owns_files are non-overlapping globs " +
        "relative to the repo root; depends_on lists only module names defined in this same response; " +
        "no circular dependencies. A single subtask with empty depends_on is correct when the work fits one agent.";

    private readonly string? _model;
    private readonly Func<string, CancellationToken, Task<string>> _processRunner;

    public ClaudeCliCoordinator(string? model = null,
        Func<string, CancellationToken, Task<string>>? processRunner = null)
    {
        _model = model;
        _processRunner = processRunner ?? ((prompt, ct) => SpawnAsync(prompt, _model, ct));
    }

    public static async Task<bool> IsCliInstalledAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } return false; }

            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<SubTask>> DecomposeAsync(string instruction, CancellationToken ct)
    {
        var prompt = $"{SystemPrompt}\n\nUser request: {instruction}";
        var stdout = await _processRunner(prompt, ct);
        return SubTaskDeserializer.Parse(stdout);
    }

    private static async Task<string> SpawnAsync(string prompt, string? model, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-p");
        if (model is { Length: > 0 })
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(model);
        }
        psi.ArgumentList.Add(prompt);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start 'claude' CLI — is Claude Code installed and in PATH?");

        // Kill the child process if the caller cancels.
        using var killReg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        // Both reads must start before WaitForExitAsync to avoid deadlock when
        // either pipe's buffer fills while the other is not being consumed.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Claude CLI exited with code {process.ExitCode}: {stderr.Trim()}");

        return stdout;
    }
}
