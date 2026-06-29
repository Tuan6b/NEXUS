namespace Nexus.Mcp;

// TODO M1: Replace with real MCP server using ModelContextProtocol NuGet package.
// For M0, MCP ingestion is a stub — agents report via in-proc IEventSink.
public sealed class McpStub
{
    public bool IsRunning => false;
}
