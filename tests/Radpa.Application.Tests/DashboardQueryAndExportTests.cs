using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Reports;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class DashboardQueryAndExportTests
{
    [Fact]
    public async Task QueryAggregatesMetricsDetectsFirstTimeEndpointsAndExportsFormats()
    {
        var tStart = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        var tEnd = tStart.AddHours(12);
        var range = new TimeRange(tStart, tEnd);

        var chromeKey = new AppKey("group:Google Chrome");
        var codeKey = new AppKey("group:Visual Studio Code");

        var repo = Substitute.For<IUsageRepository>();
        repo.GetAllAppsAsync(Arg.Any<CancellationToken>()).Returns([
            new AppIdentity(chromeKey, "Google Chrome", @"C:\Chrome\chrome.exe"),
            new AppIdentity(codeKey, "Visual Studio Code", @"C:\Code\Code.exe"),
        ]);

        repo.QuerySessionsAsync(range, Arg.Any<CancellationToken>()).Returns([
            new UsageSession(1, chromeKey, tStart.AddHours(1), tStart.AddHours(2), 3600, 1800, 2400, "Research"),
            new UsageSession(2, codeKey, tStart.AddHours(2), tStart.AddHours(4), 7200, 6000, 7000, "Radpa"),
        ]);

        repo.QueryResourceSamplesAsync(range, Arg.Any<CancellationToken>()).Returns([
            new ResourceSample(chromeKey, tStart.AddHours(1), 10.0, 400_000_000, 300_000_000, 1024, 2048, null, null),
            new ResourceSample(codeKey, tStart.AddHours(2), 20.0, 800_000_000, 600_000_000, 4096, 8192, 15.0, 256_000_000),
        ]);

        var rangeSamples = new NetworkSample[]
        {
            new(chromeKey, tStart.AddHours(1), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", "one.one.one.one", 5000, 20000),
            new(chromeKey, tStart.AddHours(1), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "93.184.216.34", "example.com", 2000, 9000),
        };
        var historicalAndRange = new[]
        {
            new NetworkSample(chromeKey, tStart.AddDays(-2), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", "one.one.one.one", 100, 200),
        }.Concat(rangeSamples).ToArray();

        repo.QueryNetworkSamplesAsync(Arg.Any<TimeRange>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<TimeRange>() == range ? rangeSamples : historicalAndRange);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.DecryptField(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());

        var query = new DashboardQueryService(repo, encryption);
        var snapshot = await query.QueryAsync(range, TestContext.Current.CancellationToken);

        snapshot.Apps.Should().HaveCount(2);
        snapshot.Apps[0].DisplayName.Should().Be("Visual Studio Code");
        snapshot.Apps[0].AverageGpuPercent.Should().Be(15.0);
        snapshot.Apps[1].DisplayName.Should().Be("Google Chrome");
        snapshot.Apps[1].AverageGpuPercent.Should().BeNull();
        snapshot.Apps[1].NetworkSentBytes.Should().Be(7000);
        snapshot.Apps[1].NetworkReceivedBytes.Should().Be(29000);

        var knownEndpoint = snapshot.PrivacyAudit.Single(e => e.RemoteIp == "1.1.1.1");
        knownEndpoint.IsFirstTimeDestination.Should().BeFalse();

        var newEndpoint = snapshot.PrivacyAudit.Single(e => e.RemoteIp == "93.184.216.34");
        newEndpoint.IsFirstTimeDestination.Should().BeTrue();
        newEndpoint.RemoteDomain.Should().Be("example.com");

        snapshot.Projects.Should().HaveCount(2);

        // Verify CSV, JSON, and HTML exports
        var csv = ReportExporter.ToCsv(snapshot);
        csv.Should().Contain("Google Chrome");
        csv.Should().Contain(",N/A,");

        var json = ReportExporter.ToJson(snapshot);
        var roundTrip = JsonSerializer.Deserialize(json, ReportExportJsonContext.Default.DashboardSnapshot);
        roundTrip.Should().NotBeNull();
        roundTrip!.TotalNetworkSent.Should().Be(7000);

        var html = ReportExporter.ToHtmlReport(snapshot, persianRtl: true, usePersianDigits: true, "گزارش <Test>");
        html.Should().Contain("lang=\"fa\" dir=\"rtl\"");
        html.Should().Contain("گزارش &lt;Test&gt;");
    }

    [Fact]
    public async Task ComparePeriodsComputesDeltasBetweenRanges()
    {
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new TimeRange(t0, t0.AddDays(1));
        var after = new TimeRange(t0.AddDays(1), t0.AddDays(2));
        var key = new AppKey("exe:app");

        var repo = Substitute.For<IUsageRepository>();
        repo.GetAllAppsAsync(Arg.Any<CancellationToken>()).Returns([new AppIdentity(key, "App", @"C:\app.exe")]);
        repo.QuerySessionsAsync(before, Arg.Any<CancellationToken>()).Returns([new UsageSession(1, key, t0, t0.AddHours(1), 600, 300, 300)]);
        repo.QuerySessionsAsync(after, Arg.Any<CancellationToken>()).Returns([new UsageSession(2, key, t0.AddDays(1), t0.AddDays(1).AddHours(1), 900, 900, 900)]);
        repo.QueryResourceSamplesAsync(Arg.Any<TimeRange>(), Arg.Any<CancellationToken>()).Returns([]);
        repo.QueryNetworkSamplesAsync(Arg.Any<TimeRange>(), Arg.Any<CancellationToken>()).Returns([]);

        var encryption = Substitute.For<IEncryptionService>();
        var service = new DashboardQueryService(repo, encryption);

        var cmp = await service.ComparePeriodsAsync(before, after, TestContext.Current.CancellationToken);
        cmp.Should().ContainSingle();
        cmp[0].ActiveDelta.Should().Be(TimeSpan.FromSeconds(600));
    }
}
