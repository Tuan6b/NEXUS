using Dapper;
using Microsoft.Data.Sqlite;
using Nexus.Core.Domain;
using TaskStatus = Nexus.Core.Domain.TaskStatus;

namespace Nexus.State.Repositories;

public sealed class TaskRepository
{
    private readonly SqliteConnection _conn;

    public TaskRepository(SqliteConnection conn) => _conn = conn;

    public async Task UpsertAsync(TaskItem task)
    {
        await _conn.ExecuteAsync("""
            INSERT INTO Tasks (Id, AgentId, Status, ProgressPercent, OwnsFiles, DependsOn, RetryCount)
            VALUES (@Id, @AgentId, @Status, @ProgressPercent, @OwnsFiles, @DependsOn, @RetryCount)
            ON CONFLICT(Id) DO UPDATE SET
                AgentId = excluded.AgentId,
                Status = excluded.Status,
                ProgressPercent = excluded.ProgressPercent,
                OwnsFiles = excluded.OwnsFiles,
                DependsOn = excluded.DependsOn,
                RetryCount = excluded.RetryCount
            """,
            new
            {
                task.Id,
                task.AgentId,
                Status = task.Status.ToString(),
                task.ProgressPercent,
                OwnsFiles = string.Join("|", task.OwnsFiles),
                DependsOn = string.Join("|", task.DependsOn),
                task.RetryCount
            });
    }

    public async Task<IEnumerable<TaskItem>> LoadOpenTasksAsync()
    {
        var rows = await _conn.QueryAsync<TaskRow>("""
            SELECT * FROM Tasks WHERE Status IN ('Pending','Running')
            """);
        return rows.Select(MapRow);
    }

    public async Task<TaskItem?> GetByIdAsync(string id)
    {
        var row = await _conn.QueryFirstOrDefaultAsync<TaskRow>(
            "SELECT * FROM Tasks WHERE Id = @Id", new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<IEnumerable<TaskItem>> LoadAllAsync()
    {
        var rows = await _conn.QueryAsync<TaskRow>("SELECT * FROM Tasks");
        return rows.Select(MapRow);
    }

    private static TaskItem MapRow(TaskRow r) => new(
        r.Id,
        r.AgentId,
        Enum.Parse<TaskStatus>(r.Status),
        r.ProgressPercent,
        r.OwnsFiles.Length > 0 ? r.OwnsFiles.Split('|') : Array.Empty<string>(),
        r.DependsOn.Length > 0 ? r.DependsOn.Split('|') : Array.Empty<string>(),
        r.RetryCount);

    private sealed class TaskRow
    {
        public string Id { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string Status { get; set; } = "";
        public int ProgressPercent { get; set; }
        public string OwnsFiles { get; set; } = "";
        public string DependsOn { get; set; } = "";
        public int RetryCount { get; set; }
    }
}
