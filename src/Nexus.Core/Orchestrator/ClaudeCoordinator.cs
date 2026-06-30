using System.Text;
using System.Text.Json;

namespace Nexus.Core.Orchestrator;

public sealed class ClaudeCoordinator : ICoordinator, IDisposable
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    // Prompt instructs Claude to return the decomposition JSON directly,
    // with no preamble, so SubTaskDeserializer can extract it cleanly.
    private const string PromptTemplate = """
        You are a software architect. Decompose the task below into independent modules
        that can be coded in parallel by separate agents. Each module must have clear
        file ownership and minimal cross-module dependencies.

        Task: {0}

        Respond with valid JSON only — no preamble, no markdown code fences:
        {{
          "subtasks": [
            {{
              "module": "<module-name-kebab-case>",
              "instruction": "<specific coding instruction for this module>",
              "owns_files": ["<glob-pattern>"],
              "depends_on": ["<module-name>"]
            }}
          ]
        }}

        Rules:
        - module names: unique, lowercase, kebab-case identifiers
        - owns_files: non-overlapping glob patterns relative to the repo root
        - depends_on: only module names defined in this same response; no circular deps
        - a single subtask with empty depends_on is correct when the task fits one agent
        """;

    private readonly HttpClient _http = new();
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeCoordinator(string apiKey, string model = "claude-sonnet-4-6")
    {
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<IReadOnlyList<SubTask>> DecomposeAsync(string instruction, CancellationToken ct)
    {
        var prompt = string.Format(PromptTemplate, instruction);
        var raw = await CallApiAsync(prompt, ct);
        return SubTaskDeserializer.Parse(raw);
    }

    private async Task<string> CallApiAsync(string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            max_tokens = 4096,
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Coordinator API error {(int)response.StatusCode}: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException(
                "Coordinator API returned empty content.");

        return text;
    }

    public void Dispose() => _http.Dispose();
}
