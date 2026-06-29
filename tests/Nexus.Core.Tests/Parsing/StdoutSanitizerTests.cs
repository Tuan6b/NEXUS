using Nexus.Core.Parsing;

namespace Nexus.Core.Tests.Parsing;

public class StdoutSanitizerTests
{
    [Fact]
    public void ExtractJson_CleanJson_ReturnsJson()
    {
        const string input = """{"tasks":[{"id":"auth"}]}""";
        var result = StdoutSanitizer.ExtractJson(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void ExtractJson_WrappedInMarkers_ReturnsContent()
    {
        const string input = "some preamble\n<<<JSON>>>\n{\"tasks\":[]}\n<<<END>>>\ntrailing text";
        var result = StdoutSanitizer.ExtractJson(input);
        Assert.Equal("{\"tasks\":[]}", result);
    }

    [Fact]
    public void ExtractJson_AnsiNoiseBeforeJson_ReturnsJson()
    {
        // ANSI color codes + "checking for updates..." noise before JSON
        const string ansiPrefix = "\x1B[32mchecking for updates...\x1B[0m\n";
        const string json = """{"status":"ok"}""";
        var result = StdoutSanitizer.ExtractJson(ansiPrefix + json);
        Assert.Equal(json, result);
    }

    [Fact]
    public void ExtractJson_NestedBraces_ReturnsFullObject()
    {
        const string input = """{"outer":{"inner":{"deep":1}},"key":"val"}""";
        var result = StdoutSanitizer.ExtractJson(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void ExtractJson_NoJson_ReturnsNull()
    {
        const string input = "Error: command not found\nchecking for updates...";
        var result = StdoutSanitizer.ExtractJson(input);
        Assert.Null(result);
    }

    [Fact]
    public void StripAnsi_RemovesEscapeSequences_LeavesText()
    {
        const string input = "\x1B[32mGreen text\x1B[0m normal";
        var result = StdoutSanitizer.StripAnsi(input);
        Assert.Equal("Green text normal", result);
    }
}
