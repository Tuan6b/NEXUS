using Nexus.Core.Domain;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.Core.Pipeline;

// Discriminated-union style events flowing through the channel pipeline

public abstract record StateEvent;

public record TaskCreatedEvent(TaskItem Task) : StateEvent;

public record ProgressEvent(string TaskId, int Percent, string Note) : StateEvent;

public record TaskCompletedEvent(string TaskId, bool Success, string? Error) : StateEvent;

public record AgentRegisteredEvent(AgentInfo Agent) : StateEvent;

public record AgentStatusChangedEvent(string AgentId, AgentLiveStatus NewStatus) : StateEvent;

public record TaskStatusChangedEvent(string TaskId, TaskStatus NewStatus) : StateEvent;

public record ContractPublishedEvent(string Module, string Method, string Signature) : StateEvent;

public record HeartbeatEvent(string TaskId, DateTimeOffset Timestamp) : StateEvent;
