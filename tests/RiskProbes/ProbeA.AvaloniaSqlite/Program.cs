using System.Diagnostics;
using Avalonia;
using Microsoft.Data.Sqlite;

namespace ProbeA.AvaloniaSqlite;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var sw = Stopwatch.StartNew();
        var dbPath = Path.Combine(Path.GetTempPath(), $"radpa_probe_a_{Environment.ProcessId}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var pragma = conn.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                var mode = pragma.ExecuteScalar()?.ToString();

                using var create = conn.CreateCommand();
                create.CommandText = "CREATE TABLE IF NOT EXISTS probe (id INTEGER PRIMARY KEY, label TEXT NOT NULL);";
                create.ExecuteNonQuery();

                using var insert = conn.CreateCommand();
                insert.CommandText = "INSERT INTO probe (label) VALUES ($label);";
                insert.Parameters.AddWithValue("$label", "ردپا ProbeA 123");
                insert.ExecuteNonQuery();

                using var select = conn.CreateCommand();
                select.CommandText = "SELECT label FROM probe LIMIT 1;";
                var label = select.ExecuteScalar()?.ToString();

                if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(label))
                {
                    return 2;
                }
            }

            if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            {
                _ = AppBuilder.Configure<App>().UsePlatformDetect();
                sw.Stop();
                Console.WriteLine($"PROBE_A_OK elapsed_ms={sw.ElapsedMilliseconds}");
                return 0;
            }

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
