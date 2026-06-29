using Microsoft.Data.Sqlite;

namespace Nexus.State;

public static class DatabaseInitializer
{
    public static string GetDbPath()
    {
        var basePath = AppContext.BaseDirectory;
        var nexusDir = Path.Combine(basePath, ".nexus");
        Directory.CreateDirectory(nexusDir);
        return Path.Combine(nexusDir, "state.db");
    }

    public static SqliteConnection CreateConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var wal = conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
        return conn;
    }

    public static void EnsureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Tasks (
                Id TEXT PRIMARY KEY,
                AgentId TEXT NOT NULL,
                Status TEXT NOT NULL,
                ProgressPercent INTEGER NOT NULL DEFAULT 0,
                OwnsFiles TEXT NOT NULL DEFAULT '',
                DependsOn TEXT NOT NULL DEFAULT '',
                RetryCount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Agents (
                Id TEXT PRIMARY KEY,
                AdapterType TEXT NOT NULL,
                Source TEXT NOT NULL,
                Live TEXT NOT NULL,
                LastSeen TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
