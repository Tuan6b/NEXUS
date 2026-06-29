namespace Nexus.Core.Adapters;

public record AgentRunResult(string TaskId, bool Success, string? DiffPath, string RawStdout, string? Error);
