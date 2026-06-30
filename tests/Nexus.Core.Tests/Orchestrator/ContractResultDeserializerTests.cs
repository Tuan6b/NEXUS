using Nexus.Core.Orchestrator;

namespace Nexus.Core.Tests.Orchestrator;

public class ContractResultDeserializerTests
{
    [Fact]
    public void Parse_ValidJsonInMarkers_ReturnsAllFields()
    {
        const string output = """
            <<<JSON>>>
            {
              "interface_path": "src/main/java/com/example/IAuthService.java",
              "interface_code": "public interface IAuthService {}",
              "test_path": "src/test/java/com/example/AuthServiceTest.java",
              "test_code": "class AuthServiceTest {}"
            }
            <<<END>>>
            """;

        var result = ContractResultDeserializer.Parse("auth", output);

        Assert.Equal("auth", result.Module);
        Assert.Equal("src/main/java/com/example/IAuthService.java", result.InterfacePath);
        Assert.Equal("public interface IAuthService {}", result.InterfaceCode);
        Assert.Equal("src/test/java/com/example/AuthServiceTest.java", result.TestPath);
        Assert.Equal("class AuthServiceTest {}", result.TestCode);
    }

    [Fact]
    public void Parse_JsonWithoutMarkers_FallsBackToBraceExtraction()
    {
        // StdoutSanitizer strategy 2 (brace-count) should still find the JSON.
        const string output =
            "Sure, here is the contract:\n" +
            "{\"interface_path\":\"I.java\",\"interface_code\":\"c\",\"test_path\":\"T.java\",\"test_code\":\"t\"}\n" +
            "Let me know if you need changes.";

        var result = ContractResultDeserializer.Parse("m", output);

        Assert.Equal("I.java", result.InterfacePath);
    }

    [Fact]
    public void Parse_AnsiNoiseThenMarkers_StillParses()
    {
        const string output =
            "\x1b[32mClaude Code v2.1\x1b[0m\n" +
            "<<<JSON>>>\n" +
            "{\"interface_path\":\"p\",\"interface_code\":\"c\",\"test_path\":\"tp\",\"test_code\":\"tc\"}\n" +
            "<<<END>>>\n";

        var result = ContractResultDeserializer.Parse("m", output);

        Assert.Equal("p", result.InterfacePath);
    }

    [Fact]
    public void Parse_NoJson_ThrowsWithModuleNameInMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContractResultDeserializer.Parse("auth", "Error: rate limited"));

        Assert.Contains("auth", ex.Message);
        Assert.Contains("no JSON object found", ex.Message);
    }

    [Fact]
    public void Parse_EmptyInterfaceCode_ThrowsMissingFieldsError()
    {
        const string output = """
            <<<JSON>>>
            {"interface_path":"p","interface_code":"","test_path":"tp","test_code":"tc"}
            <<<END>>>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContractResultDeserializer.Parse("auth", output));

        Assert.Contains("missing required fields", ex.Message);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsWithParseError()
    {
        const string output = "<<<JSON>>>\n{broken json\n<<<END>>>";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ContractResultDeserializer.Parse("auth", output));

        Assert.Contains("parse", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
