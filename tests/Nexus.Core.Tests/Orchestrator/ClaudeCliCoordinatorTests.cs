using Nexus.Core.Orchestrator;

namespace Nexus.Core.Tests.Orchestrator;

// These tests exercise ClaudeCliCoordinator via the processRunner seam —
// no real 'claude' process is spawned, so they run offline in CI.
public class ClaudeCliCoordinatorTests
{
    [Fact]
    public async Task DecomposeAsync_ValidJsonInMarkers_ParsesSubTasks()
    {
        const string output = """
            <<<JSON>>>
            {"subtasks":[{"module":"auth","instruction":"Impl auth","owns_files":["src/auth/**"],"depends_on":[]}]}
            <<<END>>>
            """;
        var sut = Coordinator(Task.FromResult(output));

        var result = await sut.DecomposeAsync("build a hotel booking system", default);

        Assert.Single(result);
        Assert.Equal("auth", result[0].ModuleName);
        Assert.Equal("Impl auth", result[0].Instruction);
    }

    [Fact]
    public async Task DecomposeAsync_MultipleSubTasks_DependenciesPreserved()
    {
        const string output = """
            <<<JSON>>>
            {"subtasks":[
              {"module":"auth","instruction":"Impl auth","owns_files":["src/auth/**"],"depends_on":[]},
              {"module":"booking","instruction":"Impl booking","owns_files":["src/booking/**"],"depends_on":["auth"]}
            ]}
            <<<END>>>
            """;
        var sut = Coordinator(Task.FromResult(output));

        var result = await sut.DecomposeAsync("hotel app", default);

        Assert.Equal(2, result.Count);
        Assert.Equal("booking", result[1].ModuleName);
        Assert.Equal(new[] { "auth" }, result[1].DependsOn);
    }

    [Fact]
    public async Task DecomposeAsync_NoJsonInOutput_ThrowsInvalidOperation()
    {
        var sut = Coordinator(Task.FromResult("Error: model not found\nplease retry"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DecomposeAsync("task", default));

        Assert.Contains("no JSON object found", ex.Message);
    }

    [Fact]
    public async Task DecomposeAsync_ProcessRunnerThrows_PropagatesException()
    {
        var sut = Coordinator(Task.FromException<string>(
            new InvalidOperationException("Claude CLI exited with code 1: rate limited")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DecomposeAsync("task", default));

        Assert.Contains("rate limited", ex.Message);
    }

    [Fact]
    public async Task DecomposeAsync_EmptySubtasksArray_ThrowsInvalidOperation()
    {
        var sut = Coordinator(Task.FromResult("""{"subtasks":[]}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DecomposeAsync("task", default));

        Assert.Contains("no subtasks", ex.Message);
    }

    [Fact]
    public async Task DecomposeAsync_AnsiNoiseThenMarkers_StillParses()
    {
        // StdoutSanitizer strips ANSI before looking for delimiters.
        const string output =
            "\x1b[32mClaude Code v2.1.196\x1b[0m\n" +
            "<<<JSON>>>\n" +
            "{\"subtasks\":[{\"module\":\"x\",\"instruction\":\"i\",\"owns_files\":[],\"depends_on\":[]}]}\n" +
            "<<<END>>>\n";
        var sut = Coordinator(Task.FromResult(output));

        var result = await sut.DecomposeAsync("task", default);

        Assert.Single(result);
        Assert.Equal("x", result[0].ModuleName);
    }

    private static ClaudeCliCoordinator Coordinator(Task<string> result) =>
        new(processRunner: (_, _) => result);
}
