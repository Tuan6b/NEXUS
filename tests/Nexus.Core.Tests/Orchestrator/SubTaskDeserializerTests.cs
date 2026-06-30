using Nexus.Core.Orchestrator;

namespace Nexus.Core.Tests.Orchestrator;

public class SubTaskDeserializerTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsTwoSubTasks()
    {
        const string json = """
            {
              "subtasks": [
                { "module": "auth", "instruction": "Impl auth", "owns_files": ["src/auth/**"], "depends_on": [] },
                { "module": "booking", "instruction": "Impl booking", "owns_files": ["src/booking/**"], "depends_on": ["auth"] }
              ]
            }
            """;

        var result = SubTaskDeserializer.Parse(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("auth", result[0].ModuleName);
        Assert.Equal("Impl auth", result[0].Instruction);
        Assert.Empty(result[0].DependsOn);
        Assert.Equal("booking", result[1].ModuleName);
        Assert.Equal(new[] { "auth" }, result[1].DependsOn);
    }

    [Fact]
    public void Parse_JsonWrappedInMarkers_ExtractsCorrectly()
    {
        const string raw = """
            Let me decompose this task...
            <<<JSON>>>
            {"subtasks":[{"module":"auth","instruction":"i","owns_files":["src/auth/**"],"depends_on":[]}]}
            <<<END>>>
            """;

        var result = SubTaskDeserializer.Parse(raw);

        Assert.Single(result);
        Assert.Equal("auth", result[0].ModuleName);
    }

    [Fact]
    public void Parse_MissingDependsOnField_DefaultsToEmpty()
    {
        const string json = """{"subtasks":[{"module":"x","instruction":"i","owns_files":["src/**"]}]}""";

        var result = SubTaskDeserializer.Parse(json);

        Assert.Empty(result[0].DependsOn);
    }

    [Fact]
    public void Parse_MissingOwnsFilesField_DefaultsToEmpty()
    {
        const string json = """{"subtasks":[{"module":"x","instruction":"i","depends_on":[]}]}""";

        var result = SubTaskDeserializer.Parse(json);

        Assert.Empty(result[0].OwnsFiles);
    }

    [Fact]
    public void Parse_NoJsonInOutput_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubTaskDeserializer.Parse("Error: model not found\nplease check your API key"));

        Assert.Contains("no JSON object found", ex.Message);
    }

    [Fact]
    public void Parse_EmptySubtasksArray_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubTaskDeserializer.Parse("""{"subtasks":[]}"""));

        Assert.Contains("no subtasks", ex.Message);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubTaskDeserializer.Parse("""{"subtasks": [broken json}"""));

        Assert.Contains("Cannot parse decomposition JSON", ex.Message);
    }

    // ── Module name validation (path-traversal guard) ─────────────────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../evil")]
    [InlineData("Auth")]            // uppercase rejected
    [InlineData("auth module")]     // spaces rejected
    [InlineData("")]                // empty rejected
    public void Parse_InvalidModuleName_ThrowsInvalidOperation(string badModule)
    {
        var json = $$"""{"subtasks":[{"module":"{{badModule}}","instruction":"i","owns_files":[],"depends_on":[]}]}""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SubTaskDeserializer.Parse(json));

        Assert.Contains("invalid module name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("booking-service")]
    [InlineData("module1")]
    [InlineData("a")]
    public void Parse_ValidModuleName_DoesNotThrow(string goodModule)
    {
        var json = $$"""{"subtasks":[{"module":"{{goodModule}}","instruction":"i","owns_files":[],"depends_on":[]}]}""";

        var result = SubTaskDeserializer.Parse(json);

        Assert.Equal(goodModule, result[0].ModuleName);
    }
}
