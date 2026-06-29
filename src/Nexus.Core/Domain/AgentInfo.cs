namespace Nexus.Core.Domain;

public record AgentInfo(
    string Id,
    string AdapterType,
    AgentSource Source,
    AgentLiveStatus Live,
    DateTimeOffset LastSeen);
