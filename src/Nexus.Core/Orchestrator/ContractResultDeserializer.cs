using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Core.Parsing;

namespace Nexus.Core.Orchestrator;

// Parses the JSON emitted by ICoordinator.GenerateContractAsync into a ContractGenerationResult.
// Same sanitizer pipeline as SubTaskDeserializer: strip ANSI → delimiter → brace-count fallback.
public static class ContractResultDeserializer
{
    public static ContractGenerationResult Parse(string module, string raw)
    {
        var json = StdoutSanitizer.ExtractJson(raw)
            ?? throw new InvalidOperationException(
                $"Cannot parse contract for module '{module}': no JSON object found in coordinator output.");

        ContractDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ContractDto>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Cannot parse contract JSON for module '{module}': {ex.Message}", ex);
        }

        if (dto is null
            || string.IsNullOrWhiteSpace(dto.InterfacePath)
            || string.IsNullOrWhiteSpace(dto.InterfaceCode)
            || string.IsNullOrWhiteSpace(dto.TestPath)
            || string.IsNullOrWhiteSpace(dto.TestCode))
            throw new InvalidOperationException(
                $"Contract for module '{module}' is missing required fields " +
                "(interface_path, interface_code, test_path, test_code).");

        return new ContractGenerationResult(
            module,
            dto.InterfacePath,
            dto.InterfaceCode,
            dto.TestPath,
            dto.TestCode);
    }

    private sealed class ContractDto
    {
        [JsonPropertyName("interface_path")]
        public string InterfacePath { get; set; } = "";

        [JsonPropertyName("interface_code")]
        public string InterfaceCode { get; set; } = "";

        [JsonPropertyName("test_path")]
        public string TestPath { get; set; } = "";

        [JsonPropertyName("test_code")]
        public string TestCode { get; set; } = "";
    }
}
