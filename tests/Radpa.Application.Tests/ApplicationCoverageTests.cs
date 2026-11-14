using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class ApplicationCoverageTests
{
    [Fact]
    public async Task CoordinatorHandlesProcessEventsSettingsUpdatesTitleEncryptionAndNetworkAnomalies()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);
        var now = t0;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);
        clock.LocalNow.Returns(_ => now.ToLocalTime());

        var procMonitor = Substitute.For<IProcessMonitor>();
        procMonitor.GetRunningProcesses().Returns([]);
        var fgMonitor = Substitute.For<IForegroundMonitor>();
        var idleProvider = Substitute.For<IIdleTimeProvider>();
        var sysState = Substitute.For<ISystemStateMonitor>();
        var resourceSampler = Substitute.For<IResourceSampler>();
        var gpuSampler = Substitute.For<IGpuSampler>();
        gpuSampler.IsAvailable.Returns(true);
        gpuSampler.When(g => g.SampleProcesses(Arg.Any<Dictionary<int, ProcessGpuReading>>()))
            .Do(call => call.Arg<Dictionary<int, ProcessGpuReading>>()[501] = new ProcessGpuReading(501, 22.0, 512_000_000));

        var netCollector = Substitute.For<INetworkCollector>();
        var repo = Substitute.For<IUsageRepository>();
        TelemetryFlushBatch? lastBatch = null;
        repo.SaveBatchAsync(Arg.Do<TelemetryFlushBatch>(b => lastBatch = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.EncryptField(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>() is { } s ? $"ENC:{s}" : null);

        var dns = Substitute.For<IDnsResolverCache>();
        dns.TryGetCachedHost("8.8.8.8").Returns("dns.google");

        var notifications = Substitute.For<INotificationService>();
        var logger = Substitute.For<IAppLogger>();
        var focus = new FocusSessionManager(clock, notifications);

        var settings = new AppSettings
        {
            SamplingIntervalSeconds = 60,
            RecordWindowTitles = true,
        };

        await using var coordinator = new TrackingCoordinator(
            clock, procMonitor, fgMonitor, idleProvider, sysState,
            resourceSampler, gpuSampler, netCollector, repo, encryption, dns, notifications, logger, focus, settings);

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        // Simulate ForegroundChanged discovering a new PID + window title
        fgMonitor.ForegroundChanged += Raise.Event<EventHandler<ForegroundAppInfo>>(
            fgMonitor,
            new ForegroundAppInfo(501, @"C:\Tools\editor.exe", "notes.txt", t0));

        resourceSampler.When(r => r.SampleProcesses(Arg.Any<IReadOnlyList<int>>(), Arg.Any<List<ProcessResourceReading>>()))
            .Do(call => call.Arg<List<ProcessResourceReading>>().Add(new ProcessResourceReading(501, 5.0, 100_000_000, 80_000_000, 100, 200)));

        // Build upload baseline with 7 small ticks + trigger flush with encrypted title & GPU
        for (var i = 1; i <= 7; i++)
        {
            now = t0.AddSeconds(i * 10);
            netCollector.TrafficBatchReceived += Raise.Event<EventHandler<IReadOnlyList<NetworkTrafficReading>>>(
                netCollector,
                new NetworkTrafficReading[] { new NetworkTrafficReading(501, "Ethernet", NetworkInterfaceKind.Ethernet, NetworkProtocol.Udp, "8.8.8.8", 500_000, 1_000_000) });
            coordinator.ProcessTickForTests(now);
        }

        // Now send a large upload burst and process exit
        var alerts = new List<AnomalyAlert>();
        coordinator.AnomalyDetected += (_, a) => alerts.Add(a);
        now = t0.AddSeconds(80);
        netCollector.TrafficBatchReceived += Raise.Event<EventHandler<IReadOnlyList<NetworkTrafficReading>>>(
            netCollector,
            new NetworkTrafficReading[] { new NetworkTrafficReading(501, "Ethernet", NetworkInterfaceKind.Ethernet, NetworkProtocol.Udp, "8.8.8.8", 25 * 1024 * 1024, 1_000_000) });
        coordinator.ProcessTickForTests(now);

        alerts.Should().Contain(a => a.Metric == AnomalyMetricKind.UploadBytesPerMinute);

        await coordinator.FlushNowAsync(TestContext.Current.CancellationToken);
        lastBatch.Should().NotBeNull();
        lastBatch!.ResourceSamples[0].WindowTitle.Should().Be("ENC:notes.txt");
        lastBatch.ResourceSamples[0].GpuPercent.Should().Be(22.0);
        lastBatch.NetworkSamples[0].RemoteDomain.Should().Be("ENC:dns.google");

        // UpdateSettings re-resolves running process identities
        coordinator.UpdateSettings(settings with
        {
            GroupingRules = [new GroupingRule("custom", "Custom Editor", GroupingMatchKind.ExecutableName, "editor.exe", "My Editor")],
        });

        procMonitor.ProcessStarted += Raise.Event<EventHandler<ProcessInfo>>(
            procMonitor,
            new ProcessInfo(502, @"C:\Tools\other.exe", null, "Other", now));
        procMonitor.ProcessExited += Raise.Event<EventHandler<ProcessExitedEventArgs>>(
            procMonitor,
            new ProcessExitedEventArgs(502, now.AddSeconds(1)));

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task EncryptionMigrationNoOpsWhenAlreadyInDesiredStateAndExportsCsvEscapingWorks()
    {
        var repo = Substitute.For<IUsageRepository>();
        var enc = Substitute.For<IEncryptionService>();
        var store = Substitute.For<ISettingsStore>();
        var mig = new EncryptionMigrationService(repo, enc, store);

        var current = new AppSettings { EncryptionEnabled = false };
        var same = await mig.MigrateAsync(current, enableEncryption: false, cancellationToken: TestContext.Current.CancellationToken);
        same.Should().BeSameAs(current);
        await repo.DidNotReceiveWithAnyArgs().GetAllSensitiveRowsAsync(Arg.Any<CancellationToken>());

        var app = new AppAggregateStats(
            new AppKey("exe:a"),
            "App, \"Quoted\"",
            @"C:\Path\app.exe",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(6),
            12.3,
            45.6,
            100,
            200,
            150,
            10,
            20,
            null,
            null,
            null,
            1000,
            2000,
            new AppHealthScore(90, 90, 90, 90, "Health.Optimal"));

        var snapshot = new DashboardSnapshot(
            [app],
            [],
            [new TimelineBucket(DateTimeOffset.UtcNow, 300, 1000, 2000)],
            [],
            [],
            TimeSpan.FromMinutes(5),
            1000,
            2000);

        var csv = ReportExporter.ToCsv(snapshot);
        csv.Should().Contain("\"App, \"\"Quoted\"\"\"");

        var htmlEn = ReportExporter.ToHtmlReport(snapshot, persianRtl: false, usePersianDigits: false, "Weekly Report");
        htmlEn.Should().Contain("lang=\"en\" dir=\"ltr\"");
    }

    [Fact]
    public async Task DashboardQueryServiceHandlesEmptyMultiDayRangesAndTimelineBucketing()
    {
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var multiDay = new TimeRange(t0, t0.AddDays(5));

        var repo = Substitute.For<IUsageRepository>();
        repo.GetAllAppsAsync(Arg.Any<CancellationToken>()).Returns([]);
        repo.QuerySessionsAsync(multiDay, Arg.Any<CancellationToken>()).Returns([
            new UsageSession(1, new AppKey("exe:a"), t0.AddDays(1), t0.AddDays(1).AddHours(1), 600, 300, 300),
        ]);
        repo.QueryResourceSamplesAsync(multiDay, Arg.Any<CancellationToken>()).Returns([]);
        repo.QueryNetworkSamplesAsync(Arg.Any<TimeRange>(), Arg.Any<CancellationToken>()).Returns([
            new NetworkSample(new AppKey("exe:a"), t0.AddDays(1), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", null, 100, 200),
        ]);

        var service = new DashboardQueryService(repo, Substitute.For<IEncryptionService>());
        var snap = await service.QueryAsync(multiDay, TestContext.Current.CancellationToken);

        snap.Timeline.Should().ContainSingle();
        snap.Apps.Should().ContainSingle();
    }
}
