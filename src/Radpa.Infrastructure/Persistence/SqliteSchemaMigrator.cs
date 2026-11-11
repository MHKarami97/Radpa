using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Radpa.Infrastructure.Persistence;

public static class SqliteSchemaMigrator
{
    public const int CurrentSchemaVersion = 1;

    public static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await ExecuteNonQueryAsync(connection, """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """, cancellationToken);

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """, cancellationToken);

        var version = await GetCurrentVersionAsync(connection, cancellationToken);
        if (version < 1)
        {
            await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await ExecuteNonQueryAsync(connection, """
                    CREATE TABLE IF NOT EXISTS apps (
                        app_key TEXT NOT NULL PRIMARY KEY,
                        display_name TEXT NOT NULL,
                        executable_path TEXT NOT NULL,
                        package_family_name TEXT NULL,
                        publisher TEXT NULL
                    );

                    CREATE TABLE IF NOT EXISTS usage_sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        app_key TEXT NOT NULL,
                        start_utc_ticks INTEGER NOT NULL,
                        end_utc_ticks INTEGER NOT NULL,
                        open_seconds INTEGER NOT NULL,
                        active_seconds INTEGER NOT NULL,
                        on_screen_seconds INTEGER NOT NULL,
                        project_tag TEXT NULL,
                        FOREIGN KEY (app_key) REFERENCES apps(app_key) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS idx_sessions_time ON usage_sessions(start_utc_ticks, end_utc_ticks);
                    CREATE INDEX IF NOT EXISTS idx_sessions_app ON usage_sessions(app_key, start_utc_ticks);

                    CREATE TABLE IF NOT EXISTS resource_samples (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        app_key TEXT NOT NULL,
                        timestamp_utc_ticks INTEGER NOT NULL,
                        cpu_percent REAL NOT NULL,
                        working_set_bytes INTEGER NOT NULL,
                        private_bytes INTEGER NOT NULL,
                        disk_read_bytes INTEGER NOT NULL,
                        disk_write_bytes INTEGER NOT NULL,
                        gpu_percent REAL NULL,
                        gpu_vram_bytes INTEGER NULL,
                        window_title TEXT NULL,
                        FOREIGN KEY (app_key) REFERENCES apps(app_key) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS idx_resources_time ON resource_samples(timestamp_utc_ticks);
                    CREATE INDEX IF NOT EXISTS idx_resources_app_time ON resource_samples(app_key, timestamp_utc_ticks);

                    CREATE TABLE IF NOT EXISTS network_samples (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        app_key TEXT NOT NULL,
                        timestamp_utc_ticks INTEGER NOT NULL,
                        interface_name TEXT NOT NULL,
                        interface_kind INTEGER NOT NULL,
                        protocol INTEGER NOT NULL,
                        remote_ip TEXT NOT NULL,
                        remote_domain TEXT NULL,
                        bytes_sent INTEGER NOT NULL,
                        bytes_received INTEGER NOT NULL,
                        FOREIGN KEY (app_key) REFERENCES apps(app_key) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS idx_network_time ON network_samples(timestamp_utc_ticks);
                    CREATE INDEX IF NOT EXISTS idx_network_app_time ON network_samples(app_key, timestamp_utc_ticks);
                    """, cancellationToken, tx);

                await using var insertVer = connection.CreateCommand();
                insertVer.Transaction = tx;
                insertVer.CommandText = "INSERT INTO schema_version (version, applied_utc) VALUES ($v, $ts);";
                insertVer.Parameters.AddWithValue("$v", 1);
                insertVer.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                await insertVer.ExecuteNonQueryAsync(cancellationToken);

                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection conn,
        string sql,
        CancellationToken ct,
        SqliteTransaction? tx = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
