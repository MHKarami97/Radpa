using Microsoft.Data.Sqlite;
using Radpa.Application.Abstractions;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Infrastructure.Persistence;

/// <summary>
/// Hand-written parameterized ADO.NET SQLite repository using WAL mode and batched transactions.
/// </summary>
public sealed class SqliteUsageRepository : IUsageRepository, IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SqliteUsageRepository(string? databasePath = null)
    {
        _dbPath = databasePath ?? Path.Combine(StoragePathResolver.ResolveDataDirectory(), "radpa.db");
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath => _dbPath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        await SqliteSchemaMigrator.MigrateAsync(conn, cancellationToken);
    }

    public async Task SaveBatchAsync(TelemetryFlushBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                if (batch.Apps.Count > 0)
                {
                    await using var appCmd = conn.CreateCommand();
                    appCmd.Transaction = tx;
                    appCmd.CommandText = """
                        INSERT INTO apps (app_key, display_name, executable_path, package_family_name, publisher)
                        VALUES ($key, $name, $path, $pkg, $pub)
                        ON CONFLICT(app_key) DO UPDATE SET
                            display_name = excluded.display_name,
                            executable_path = excluded.executable_path,
                            package_family_name = COALESCE(excluded.package_family_name, apps.package_family_name),
                            publisher = COALESCE(excluded.publisher, apps.publisher);
                        """;
                    var pKey = appCmd.Parameters.Add("$key", SqliteType.Text);
                    var pName = appCmd.Parameters.Add("$name", SqliteType.Text);
                    var pPath = appCmd.Parameters.Add("$path", SqliteType.Text);
                    var pPkg = appCmd.Parameters.Add("$pkg", SqliteType.Text);
                    var pPub = appCmd.Parameters.Add("$pub", SqliteType.Text);
                    appCmd.Prepare();

                    foreach (var app in batch.Apps)
                    {
                        pKey.Value = app.Key.Value;
                        pName.Value = app.DisplayName;
                        pPath.Value = app.ExecutablePath;
                        pPkg.Value = (object?)app.PackageFamilyName ?? DBNull.Value;
                        pPub.Value = (object?)app.Publisher ?? DBNull.Value;
                        await appCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                if (batch.Sessions.Count > 0)
                {
                    await using var sessCmd = conn.CreateCommand();
                    sessCmd.Transaction = tx;
                    sessCmd.CommandText = """
                        INSERT INTO usage_sessions (app_key, start_utc_ticks, end_utc_ticks, open_seconds, active_seconds, on_screen_seconds, project_tag)
                        VALUES ($key, $start, $end, $open, $active, $onscreen, $proj);
                        """;
                    var pKey = sessCmd.Parameters.Add("$key", SqliteType.Text);
                    var pStart = sessCmd.Parameters.Add("$start", SqliteType.Integer);
                    var pEnd = sessCmd.Parameters.Add("$end", SqliteType.Integer);
                    var pOpen = sessCmd.Parameters.Add("$open", SqliteType.Integer);
                    var pActive = sessCmd.Parameters.Add("$active", SqliteType.Integer);
                    var pOnScreen = sessCmd.Parameters.Add("$onscreen", SqliteType.Integer);
                    var pProj = sessCmd.Parameters.Add("$proj", SqliteType.Text);
                    sessCmd.Prepare();

                    foreach (var s in batch.Sessions)
                    {
                        pKey.Value = s.AppKey.Value;
                        pStart.Value = s.StartUtc.UtcTicks;
                        pEnd.Value = s.EndUtc.UtcTicks;
                        pOpen.Value = s.OpenSeconds;
                        pActive.Value = s.ActiveSeconds;
                        pOnScreen.Value = s.OnScreenSeconds;
                        pProj.Value = (object?)s.ProjectTag ?? DBNull.Value;
                        await sessCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                if (batch.ResourceSamples.Count > 0)
                {
                    await using var resCmd = conn.CreateCommand();
                    resCmd.Transaction = tx;
                    resCmd.CommandText = """
                        INSERT INTO resource_samples (app_key, timestamp_utc_ticks, cpu_percent, working_set_bytes, private_bytes, disk_read_bytes, disk_write_bytes, gpu_percent, gpu_vram_bytes, window_title)
                        VALUES ($key, $ts, $cpu, $ws, $priv, $dr, $dw, $gpu, $vram, $title);
                        """;
                    var pKey = resCmd.Parameters.Add("$key", SqliteType.Text);
                    var pTs = resCmd.Parameters.Add("$ts", SqliteType.Integer);
                    var pCpu = resCmd.Parameters.Add("$cpu", SqliteType.Real);
                    var pWs = resCmd.Parameters.Add("$ws", SqliteType.Integer);
                    var pPriv = resCmd.Parameters.Add("$priv", SqliteType.Integer);
                    var pDr = resCmd.Parameters.Add("$dr", SqliteType.Integer);
                    var pDw = resCmd.Parameters.Add("$dw", SqliteType.Integer);
                    var pGpu = resCmd.Parameters.Add("$gpu", SqliteType.Real);
                    var pVram = resCmd.Parameters.Add("$vram", SqliteType.Integer);
                    var pTitle = resCmd.Parameters.Add("$title", SqliteType.Text);
                    resCmd.Prepare();

                    foreach (var r in batch.ResourceSamples)
                    {
                        pKey.Value = r.AppKey.Value;
                        pTs.Value = r.TimestampUtc.UtcTicks;
                        pCpu.Value = r.CpuPercent;
                        pWs.Value = r.WorkingSetBytes;
                        pPriv.Value = r.PrivateBytes;
                        pDr.Value = r.DiskReadBytesDelta;
                        pDw.Value = r.DiskWriteBytesDelta;
                        pGpu.Value = r.GpuPercent.HasValue ? r.GpuPercent.Value : DBNull.Value;
                        pVram.Value = r.GpuVramBytes.HasValue ? r.GpuVramBytes.Value : DBNull.Value;
                        pTitle.Value = (object?)r.WindowTitle ?? DBNull.Value;
                        await resCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                if (batch.NetworkSamples.Count > 0)
                {
                    await using var netCmd = conn.CreateCommand();
                    netCmd.Transaction = tx;
                    netCmd.CommandText = """
                        INSERT INTO network_samples (app_key, timestamp_utc_ticks, interface_name, interface_kind, protocol, remote_ip, remote_domain, bytes_sent, bytes_received)
                        VALUES ($key, $ts, $iface, $kind, $proto, $ip, $dom, $sent, $recv);
                        """;
                    var pKey = netCmd.Parameters.Add("$key", SqliteType.Text);
                    var pTs = netCmd.Parameters.Add("$ts", SqliteType.Integer);
                    var pIface = netCmd.Parameters.Add("$iface", SqliteType.Text);
                    var pKind = netCmd.Parameters.Add("$kind", SqliteType.Integer);
                    var pProto = netCmd.Parameters.Add("$proto", SqliteType.Integer);
                    var pIp = netCmd.Parameters.Add("$ip", SqliteType.Text);
                    var pDom = netCmd.Parameters.Add("$dom", SqliteType.Text);
                    var pSent = netCmd.Parameters.Add("$sent", SqliteType.Integer);
                    var pRecv = netCmd.Parameters.Add("$recv", SqliteType.Integer);
                    netCmd.Prepare();

                    foreach (var n in batch.NetworkSamples)
                    {
                        pKey.Value = n.AppKey.Value;
                        pTs.Value = n.TimestampUtc.UtcTicks;
                        pIface.Value = n.InterfaceName;
                        pKind.Value = (int)n.InterfaceKind;
                        pProto.Value = (int)n.Protocol;
                        pIp.Value = n.RemoteIp;
                        pDom.Value = (object?)n.RemoteDomain ?? DBNull.Value;
                        pSent.Value = n.BytesSent;
                        pRecv.Value = n.BytesReceived;
                        await netCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<AppIdentity>> GetAllAppsAsync(CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT app_key, display_name, executable_path, package_family_name, publisher FROM apps;";
        var result = new List<AppIdentity>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AppIdentity(
                new AppKey(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<UsageSession>> QuerySessionsAsync(TimeRange range, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, app_key, start_utc_ticks, end_utc_ticks, open_seconds, active_seconds, on_screen_seconds, project_tag
            FROM usage_sessions
            WHERE end_utc_ticks >= $start AND start_utc_ticks <= $end;
            """;
        cmd.Parameters.AddWithValue("$start", range.StartUtc.UtcTicks);
        cmd.Parameters.AddWithValue("$end", range.EndUtc.UtcTicks);

        var result = new List<UsageSession>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new UsageSession(
                reader.GetInt64(0),
                new AppKey(reader.GetString(1)),
                new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ResourceSample>> QueryResourceSamplesAsync(TimeRange range, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT app_key, timestamp_utc_ticks, cpu_percent, working_set_bytes, private_bytes, disk_read_bytes, disk_write_bytes, gpu_percent, gpu_vram_bytes, window_title
            FROM resource_samples
            WHERE timestamp_utc_ticks >= $start AND timestamp_utc_ticks < $end;
            """;
        cmd.Parameters.AddWithValue("$start", range.StartUtc.UtcTicks);
        cmd.Parameters.AddWithValue("$end", range.EndUtc.UtcTicks);

        var result = new List<ResourceSample>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ResourceSample(
                new AppKey(reader.GetString(0)),
                new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
                reader.GetDouble(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return result;
    }

    public async Task<IReadOnlyList<NetworkSample>> QueryNetworkSamplesAsync(TimeRange range, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT app_key, timestamp_utc_ticks, interface_name, interface_kind, protocol, remote_ip, remote_domain, bytes_sent, bytes_received
            FROM network_samples
            WHERE timestamp_utc_ticks >= $start AND timestamp_utc_ticks < $end;
            """;
        cmd.Parameters.AddWithValue("$start", range.StartUtc.UtcTicks);
        cmd.Parameters.AddWithValue("$end", range.EndUtc.UtcTicks);

        var result = new List<NetworkSample>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NetworkSample(
                new AppKey(reader.GetString(0)),
                new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
                reader.GetString(2),
                (NetworkInterfaceKind)reader.GetInt32(3),
                (NetworkProtocol)reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8)));
        }

        return result;
    }

    public async Task<IReadOnlyList<SensitiveRowRecord>> GetAllSensitiveRowsAsync(CancellationToken cancellationToken)
    {
        await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
        var rows = new List<SensitiveRowRecord>();

        await using (var resCmd = conn.CreateCommand())
        {
            resCmd.CommandText = "SELECT id, window_title FROM resource_samples WHERE window_title IS NOT NULL;";
            await using var reader = await resCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SensitiveRowRecord(reader.GetInt64(0), "resource_samples", reader.GetString(1), null, null));
            }
        }

        await using (var netCmd = conn.CreateCommand())
        {
            netCmd.CommandText = "SELECT id, remote_ip, remote_domain FROM network_samples;";
            await using var reader = await netCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SensitiveRowRecord(
                    reader.GetInt64(0),
                    "network_samples",
                    null,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        return rows;
    }

    public async Task UpdateSensitiveRowsAsync(IReadOnlyList<SensitiveRowRecord> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var resCmd = conn.CreateCommand();
                resCmd.Transaction = tx;
                resCmd.CommandText = "UPDATE resource_samples SET window_title = $title WHERE id = $id;";
                var pTitle = resCmd.Parameters.Add("$title", SqliteType.Text);
                var pResId = resCmd.Parameters.Add("$id", SqliteType.Integer);
                resCmd.Prepare();

                await using var netCmd = conn.CreateCommand();
                netCmd.Transaction = tx;
                netCmd.CommandText = "UPDATE network_samples SET remote_ip = $ip, remote_domain = $dom WHERE id = $id;";
                var pIp = netCmd.Parameters.Add("$ip", SqliteType.Text);
                var pDom = netCmd.Parameters.Add("$dom", SqliteType.Text);
                var pNetId = netCmd.Parameters.Add("$id", SqliteType.Integer);
                netCmd.Prepare();

                foreach (var row in rows)
                {
                    if (row.TableName == "resource_samples")
                    {
                        pTitle.Value = (object?)row.WindowTitle ?? DBNull.Value;
                        pResId.Value = row.Id;
                        await resCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    else if (row.TableName == "network_samples")
                    {
                        pIp.Value = row.RemoteIp ?? string.Empty;
                        pDom.Value = (object?)row.RemoteDomain ?? DBNull.Value;
                        pNetId.Value = row.Id;
                        await netCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<int> ApplyRetentionAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var ticks = cutoffUtc.UtcTicks;
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                var deleted = 0;
                deleted += await DeleteOlderAsync(conn, tx, "DELETE FROM usage_sessions WHERE end_utc_ticks < $cutoff;", ticks, cancellationToken);
                deleted += await DeleteOlderAsync(conn, tx, "DELETE FROM resource_samples WHERE timestamp_utc_ticks < $cutoff;", ticks, cancellationToken);
                deleted += await DeleteOlderAsync(conn, tx, "DELETE FROM network_samples WHERE timestamp_utc_ticks < $cutoff;", ticks, cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return deleted;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task DeleteAppDataAsync(AppKey appKey, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM apps WHERE app_key = $key;";
            cmd.Parameters.AddWithValue("$key", appKey.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task DeleteAllDataAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM network_samples;
                DELETE FROM resource_samples;
                DELETE FROM usage_sessions;
                DELETE FROM apps;
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task VacuumAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await OpenConfiguredConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken)
    {
        long total = 0;
        if (File.Exists(_dbPath))
        {
            total += new FileInfo(_dbPath).Length;
        }

        if (File.Exists(_dbPath + "-wal"))
        {
            total += new FileInfo(_dbPath + "-wal").Length;
        }

        return Task.FromResult(total);
    }

    private async Task<SqliteConnection> OpenConfiguredConnectionAsync(CancellationToken cancellationToken)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return conn;
    }

    public void Dispose()
    {
        _writeGate.Dispose();
    }

    private static async Task<int> DeleteOlderAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string sql,
        long cutoffTicks,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
