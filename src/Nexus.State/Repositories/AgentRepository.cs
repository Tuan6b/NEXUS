using Dapper;
using Microsoft.Data.Sqlite;
using Nexus.Core.Domain;

namespace Nexus.State.Repositories;

public sealed class AgentRepository
{
    private readonly SqliteConnection _conn;

    public AgentRepository(SqliteConnection conn) => _conn = conn;

    public async Task UpsertAsync(AgentInfo agent)
    {
        await _conn.ExecuteAsync("""
            INSERT INTO Agents (Id, AdapterType, Source, Live, LastSeen)
            VALUES (@Id, @AdapterType, @Source, @Live, @LastSeen)
            ON CONFLICT(Id) DO UPDATE SET
                AdapterType = excluded.AdapterType,
                Source = excluded.Source,
                Live = excluded.Live,
                LastSeen = excluded.LastSeen
            """,
            new
            {
                agent.Id,
                agent.AdapterType,
                Source = agent.Source.ToString(),
                Live = agent.Live.ToString(),
                LastSeen = agent.LastSeen.ToString("O")
            });
    }

    public async Task<IEnumerable<AgentInfo>> LoadAllAsync()
    {
        var rows = await _conn.QueryAsync<AgentRow>("SELECT * FROM Agents");
        return rows.Select(r => new AgentInfo(
            r.Id,
            r.AdapterType,
            Enum.Parse<AgentSource>(r.Source),
            Enum.Parse<AgentLiveStatus>(r.Live),
            DateTimeOffset.Parse(r.LastSeen)));
    }

    private sealed class AgentRow
    {
        public string Id { get; set; } = "";
        public string AdapterType { get; set; } = "";
        public string Source { get; set; } = "";
        public string Live { get; set; } = "";
        public string LastSeen { get; set; } = "";
    }
}
