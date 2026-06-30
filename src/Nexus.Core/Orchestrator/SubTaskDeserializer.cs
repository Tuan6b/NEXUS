using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Nexus.Core.Parsing;

namespace Nexus.Core.Orchestrator;

// Parses the JSON emitted by any ICoordinator into a SubTask list.
// Kept separate from ClaudeCoordinator so it can be unit-tested without HTTP.
public static class SubTaskDeserializer
{
    // Strict allowlist: lowercase kebab-case, 1-64 chars.
    // Validated here so the check covers all downstream consumers:
    // worktree paths, git branch names, event payloads, and file writes.
    private static readonly Regex ValidModuleName =
        new(@"^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);

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
            .Select(s =>
            {
                if (!ValidModuleName.IsMatch(s.Module))
                    throw new InvalidOperationException(
                        $"Coordinator returned invalid module name '{s.Module}'. " +
                        "Module names must be lowercase kebab-case (a-z, 0-9, -, _), 1-64 chars.");
                return new SubTask(
                    s.Module,
                    s.Instruction,
                    s.OwnsFiles?.ToArray() ?? Array.Empty<string>(),
                    s.DependsOn?.ToArray() ?? Array.Empty<string>());
            })
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
