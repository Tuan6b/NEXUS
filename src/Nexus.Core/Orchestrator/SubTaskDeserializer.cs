using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Core.Parsing;

namespace Nexus.Core.Orchestrator;

// Parses the JSON emitted by any ICoordinator into a SubTask list.
// Kept separate from ClaudeCoordinator so it can be unit-tested without HTTP.
public static class SubTaskDeserializer
{
    public static IReadOnlyList<SubTask> Parse(string raw)
    {
        var json = StdoutSanitizer.ExtractJson(raw)
            ?? throw new InvalidOperationException(
                "Cannot parse decomposition: no JSON object found in coordinator output.");

        DecompositionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DecompositionDto>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Cannot parse decomposition JSON: {ex.Message}", ex);
        }

        if (dto?.Subtasks is null or { Count: 0 })
            throw new InvalidOperationException(
                "Coordinator returned no subtasks — check the model output and retry.");

        return dto.Subtasks
            .Select(s => new SubTask(
                s.Module,
                s.Instruction,
                s.OwnsFiles?.ToArray() ?? Array.Empty<string>(),
                s.DependsOn?.ToArray() ?? Array.Empty<string>()))
            .ToList();
    }

    private sealed class DecompositionDto
    {
        [JsonPropertyName("subtasks")]
        public List<SubTaskDto>? Subtasks { get; set; }
    }

    private sealed class SubTaskDto
    {
        [JsonPropertyName("module")]
        public string Module { get; set; } = "";

        [JsonPropertyName("instruction")]
        public string Instruction { get; set; } = "";

        [JsonPropertyName("owns_files")]
        public List<string>? OwnsFiles { get; set; }

        [JsonPropertyName("depends_on")]
        public List<string>? DependsOn { get; set; }
    }
}
