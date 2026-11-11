using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class TrackingCoordinatorTests
{
    [Fact]
    public async Task CoordinatorGroupsMultiProcessChromeExcludesTitlesByDefaultAndTriggersUploadRule()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var now = t0;

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);
        clock.LocalNow.Returns(_ => now.ToLocalTime());

        var procMonitor = Substitute.For<IProcessMonitor>();
        procMonitor.GetRunningProcesses().Returns([
            new ProcessInfo(100, @"C:\Program Files\Google\Chrome\Application\chrome.exe", null, "Google Chrome", t0.AddMinutes(-5)),
            new ProcessInfo(101, @"C:\Program Files\Google\Chrome\Application\chrome.exe", null, "Google Chrome", t0.AddMinutes(-3)),
        ]);

        var fgMonitor = Substitute.For<IForegroundMonitor>();
        fgMonitor.GetCurrentForeground().Returns(new ForegroundAppInfo(100, null, "Secret Tab Title", t0));

        var idleProvider = Substitute.For<IIdleTimeProvider>();
        idleProvider.GetIdleDuration().Returns(TimeSpan.FromSeconds(5));

        var sysState = Substitute.For<ISystemStateMonitor>();
        sysState.IsScreenLocked.Returns(false);
        sysState.IsDisplayOff.Returns(false);

        var resourceSampler = Substitute.For<IResourceSampler>();
        resourceSampler
            .When(r => r.SampleProcesses(Arg.Any<IReadOnlyList<int>>(), Arg.Any<List<ProcessResourceReading>>()))
            .Do(call =>
            {
                var list = call.Arg<List<ProcessResourceReading>>();
                list.Add(new ProcessResourceReading(100, 12.5, 200_000_000, 150_000_000, 1000, 500));
                list.Add(new ProcessResourceReading(101, 7.5, 100_000_000, 80_000_000, 500, 250));
            });

        var gpuSampler = Substitute.For<IGpuSampler>();
        gpuSampler.IsAvailable.Returns(false);

        var netCollector = Substitute.For<INetworkCollector>();
        var repo = Substitute.For<IUsageRepository>();
        TelemetryFlushBatch? savedBatch = null;
        repo.SaveBatchAsync(Arg.Do<TelemetryFlushBatch>(b => savedBatch = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.EncryptField(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());

        var dnsCache = Substitute.For<IDnsResolverCache>();
        var notifications = Substitute.For<INotificationService>();
        var logger = Substitute.For<IAppLogger>();
        var focus = new FocusSessionManager(clock, notifications);

        var settings = new AppSettings
        {
            SamplingIntervalSeconds = 60,
            RecordWindowTitles = false,
            AlertRules = [new NetworkAlertRule("r1", "Chrome Upload Guard", "chrome", MaxUploadMegabytesPerHour: 10, IsEnabled: true)],
        };

        await using var coordinator = new TrackingCoordinator(
            clock, procMonitor, fgMonitor, idleProvider, sysState,
            resourceSampler, gpuSampler, netCollector, repo, encryption, dnsCache, notifications, logger, focus, settings);

        var ruleMessages = new List<string>();
        coordinator.RuleAlertTriggered += (_, msg) => ruleMessages.Add(msg);

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        // Advance 10 seconds and sample tick
        now = t0.AddSeconds(10);
        coordinator.IngestNetworkReadingsForTests([
            new NetworkTrafficReading(100, "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", 12 * 1024 * 1024, 2 * 1024 * 1024),
        ], now);

        coordinator.ProcessTickForTests(now);
        await coordinator.FlushNowAsync(TestContext.Current.CancellationToken);

        savedBatch.Should().NotBeNull();
        savedBatch!.Sessions.Should().ContainSingle();
        var session = savedBatch.Sessions[0];
        // Grouped Chrome must count 10s once, not 20s across 2 processes
        session.OpenSeconds.Should().Be(10);
        session.ActiveSeconds.Should().Be(10);
        session.OnScreenSeconds.Should().Be(10);

        // Combined resources across PIDs 100 + 101
        savedBatch.ResourceSamples.Should().ContainSingle();
        var res = savedBatch.ResourceSamples[0];
        res.CpuPercent.Should().BeApproximately(20.0, 0.01);
        res.WorkingSetBytes.Should().Be(300_000_000);
        res.GpuPercent.Should().BeNull();
        res.WindowTitle.Should().BeNull("window titles are off by default");

        // Separate sent & received bytes
        savedBatch.NetworkSamples.Should().ContainSingle();
        savedBatch.NetworkSamples[0].BytesSent.Should().Be(12 * 1024 * 1024);
        savedBatch.NetworkSamples[0].BytesReceived.Should().Be(2 * 1024 * 1024);

        ruleMessages.Should().ContainSingle().Which.Should().Contain("Chrome Upload Guard");
        dnsCache.Received().QueueResolve("1.1.1.1");

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task CoordinatorDetectsCpuAnomalyAfterBaselineHistory()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 11, 0, 0, TimeSpan.Zero);
        var now = t0;
        var cpuToReport = 4.0;

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);
        clock.LocalNow.Returns(_ => now.ToLocalTime());

        var procMonitor = Substitute.For<IProcessMonitor>();
        procMonitor.GetRunningProcesses().Returns([new ProcessInfo(200, @"C:\Tools\worker.exe", null, "Worker", t0)]);

        var fgMonitor = Substitute.For<IForegroundMonitor>();
        var idleProvider = Substitute.For<IIdleTimeProvider>();
        var sysState = Substitute.For<ISystemStateMonitor>();
        var resourceSampler = Substitute.For<IResourceSampler>();
        resourceSampler
            .When(r => r.SampleProcesses(Arg.Any<IReadOnlyList<int>>(), Arg.Any<List<ProcessResourceReading>>()))
            .Do(call => call.Arg<List<ProcessResourceReading>>().Add(
                new ProcessResourceReading(200, cpuToReport, 50_000_000, 40_000_000, 0, 0)));

        var gpuSampler = Substitute.For<IGpuSampler>();
        var netCollector = Substitute.For<INetworkCollector>();
        var repo = Substitute.For<IUsageRepository>();
        var encryption = Substitute.For<IEncryptionService>();
        var dnsCache = Substitute.For<IDnsResolverCache>();
        var notifications = Substitute.For<INotificationService>();
        var logger = Substitute.For<IAppLogger>();
        var focus = new FocusSessionManager(clock, notifications);

        await using var coordinator = new TrackingCoordinator(
            clock, procMonitor, fgMonitor, idleProvider, sysState,
            resourceSampler, gpuSampler, netCollector, repo, encryption, dnsCache, notifications, logger, focus,
            new AppSettings { SamplingIntervalSeconds = 60 });

        var alerts = new List<AnomalyAlert>();
        coordinator.AnomalyDetected += (_, a) => alerts.Add(a);

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        for (var i = 1; i <= 7; i++)
        {
            now = t0.AddSeconds(i * 5);
            coordinator.ProcessTickForTests(now);
        }

        alerts.Should().BeEmpty();

        cpuToReport = 92.0;
        now = t0.AddSeconds(40);
        coordinator.ProcessTickForTests(now);

        alerts.Should().ContainSingle();
        alerts[0].Metric.Should().Be(AnomalyMetricKind.CpuPercent);
        alerts[0].ObservedValue.Should().Be(92.0);
    }
}
