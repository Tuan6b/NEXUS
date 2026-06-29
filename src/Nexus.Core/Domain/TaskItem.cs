namespace Nexus.Core.Domain;

public record TaskItem(
    string Id,
    string AgentId,
    TaskStatus Status,
    int ProgressPercent,
    string[] OwnsFiles,
    string[] DependsOn,
    int RetryCount);
