namespace Nexus.Core.Domain;

public enum TaskStatus
{
    Pending,
    Running,
    Done,
    Failed,
    Orphaned,
    Escalated
}
