namespace Nexus.Core.Adapters;

public record AgentTask(string TaskId, string Instruction, string[] OwnsFiles, string WorktreePath);
