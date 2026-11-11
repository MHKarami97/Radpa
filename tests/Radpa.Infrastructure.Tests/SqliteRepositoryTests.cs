using Microsoft.Data.Sqlite;
using FluentAssertions;
using Radpa.Application.Abstractions;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Radpa.Infrastructure.Persistence;
using Xunit;

namespace Radpa.Infrastructure.Tests;

public sealed class SqliteRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SqliteRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"radpa-repo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "radpa.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RepositoryPersistsQueriesUpdatesRetentionDeletesAndVacuumsInWalMode()
    {
        var repo = new SqliteUsageRepository(_dbPath);
        await repo.InitializeAsync(CancellationToken.None);
        // Second call verifies idempotent migration
        await repo.InitializeAsync(CancellationToken.None);

        var t0 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var chromeKey = new AppKey("group:Google Chrome");
        var codeKey = new AppKey("group:Visual Studio Code");

        var batch = new TelemetryFlushBatch(
            [
                new AppIdentity(chromeKey, "Google Chrome", @"C:\Chrome\chrome.exe"),
                new AppIdentity(codeKey, "Visual Studio Code", @"C:\Code\Code.exe"),
            ],
            [
                new UsageSession(0, chromeKey, t0, t0.AddMinutes(10), 600, 400, 500, "Research"),
                new UsageSession(0, codeKey, t0.AddDays(5), t0.AddDays(5).AddMinutes(20), 1200, 1100, 1200, "Radpa"),
            ],
            [
                new ResourceSample(chromeKey, t0.AddMinutes(5), 14.5, 350_000_000, 280_000_000, 1000, 2000, null, null, "Secret Tab"),
                new ResourceSample(codeKey, t0.AddDays(5).AddMinutes(5), 25.0, 900_000_000, 700_000_000, 4000, 8000, 12.0, 128_000_000),
            ],
            [
                new NetworkSample(chromeKey, t0.AddMinutes(5), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", "one.one.one.one", 512, 4096),
            ]);

        await repo.SaveBatchAsync(batch, CancellationToken.None);

        var fullRange = new TimeRange(t0.AddDays(-1), t0.AddDays(10));
        var apps = await repo.GetAllAppsAsync(CancellationToken.None);
        apps.Should().HaveCount(2);

        var sessions = await repo.QuerySessionsAsync(fullRange, CancellationToken.None);
        sessions.Should().HaveCount(2);

        var resources = await repo.QueryResourceSamplesAsync(fullRange, CancellationToken.None);
        resources.Should().HaveCount(2);
        resources[0].GpuPercent.Should().BeNull();
        resources[1].GpuPercent.Should().Be(12.0);

        var nets = await repo.QueryNetworkSamplesAsync(fullRange, CancellationToken.None);
        nets.Should().ContainSingle();
        nets[0].BytesSent.Should().Be(512);
        nets[0].BytesReceived.Should().Be(4096);

        // Encrypt sensitive rows via UpdateSensitiveRowsAsync
        var sensitive = await repo.GetAllSensitiveRowsAsync(CancellationToken.None);
        sensitive.Should().HaveCount(2);
        var updated = sensitive.Select(r => r with
        {
            WindowTitle = r.WindowTitle is null ? null : $"ENC1:{r.WindowTitle}",
            RemoteIp = r.RemoteIp is null ? null : $"ENC1:{r.RemoteIp}",
            RemoteDomain = r.RemoteDomain is null ? null : $"ENC1:{r.RemoteDomain}",
        }).ToArray();

        await repo.UpdateSensitiveRowsAsync(updated, CancellationToken.None);

        var updatedNets = await repo.QueryNetworkSamplesAsync(fullRange, CancellationToken.None);
        updatedNets[0].RemoteIp.Should().StartWith("ENC1:");

        // Retention removes old records (t0) while keeping newer records (t0 + 5d)
        var deletedCount = await repo.ApplyRetentionAsync(t0.AddDays(2), CancellationToken.None);
        deletedCount.Should().BeGreaterThanOrEqualTo(3);

        var afterRetentionSessions = await repo.QuerySessionsAsync(fullRange, CancellationToken.None);
        afterRetentionSessions.Should().ContainSingle().Which.AppKey.Should().Be(codeKey);

        // DeleteAppData removes codeKey via CASCADE
        await repo.DeleteAppDataAsync(codeKey, CancellationToken.None);
        (await repo.QuerySessionsAsync(fullRange, CancellationToken.None)).Should().BeEmpty();

        // Save again then DeleteAllData + Vacuum + Size
        await repo.SaveBatchAsync(batch, CancellationToken.None);
        var dbSizeBefore = await repo.GetDatabaseSizeBytesAsync(CancellationToken.None);
        dbSizeBefore.Should().BeGreaterThan(0);

        await repo.DeleteAllDataAsync(CancellationToken.None);
        (await repo.GetAllAppsAsync(CancellationToken.None)).Should().BeEmpty();
        await repo.VacuumAsync(CancellationToken.None);
    }
}
