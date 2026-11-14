using FluentAssertions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class DomainCoverageTests
{
    [Fact]
    public void TimeRangeLastDaysAndNonOverlappingIntersectAndAppKeyToStringWorkCleanly()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var last7 = TimeRange.LastDays(now, 7);
        last7.Duration.Should().Be(TimeSpan.FromDays(7));
        last7.Contains(now.AddDays(-1)).Should().BeTrue();
        last7.Contains(now.AddDays(-8)).Should().BeFalse();
        TimeRange.Today(now).Contains(now.AddMinutes(-5)).Should().BeTrue();

        var disjoint = new TimeRange(now.AddDays(1), now.AddDays(2));
        last7.Intersect(disjoint).Should().BeNull();

        var key = new AppKey("exe:Test.exe");
        key.ToString().Should().Be("exe:test.exe");
    }

    [Fact]
    public void PeriodComparisonEntryAndAppHealthScoreCoverAllGradesAndDeltas()
    {
        var key = new AppKey("exe:sample");
        var entry = new PeriodComparisonEntry(
            key,
            "Sample",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(25),
            5.0,
            12.5,
            100_000_000,
            150_000_000,
            10_000,
            25_000);

        entry.ActiveDelta.Should().Be(TimeSpan.FromMinutes(15));
        entry.CpuDelta.Should().BeApproximately(7.5, 0.01);
        entry.RamDeltaBytes.Should().Be(50_000_000);
        entry.NetDeltaBytes.Should().Be(15_000);

        var moderate = HealthScoreCalculator.Calculate(22.0, 600L * 1024 * 1024, 50L * 1024 * 1024, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(9), 1024 * 1024, 2 * 1024 * 1024, false);
        moderate.Grade.Should().BeOneOf("B", "C");

        var heavy = HealthScoreCalculator.Calculate(35.0, 2L * 1024 * 1024 * 1024, 500L * 1024 * 1024, TimeSpan.FromHours(2), TimeSpan.FromMinutes(2), 100L * 1024 * 1024, 300L * 1024 * 1024, true);
        heavy.Grade.Should().BeOneOf("C", "D", "F");

        var bGrade = new AppHealthScore(75, 75, 75, 75, "Health.Moderate");
        var cGrade = new AppHealthScore(60, 60, 60, 60, "Health.Heavy");
        var dGrade = new AppHealthScore(45, 45, 45, 45, "Health.Critical");
        bGrade.Grade.Should().Be("B");
        cGrade.Grade.Should().Be("C");
        dGrade.Grade.Should().Be("D");
        var fGrade = new AppHealthScore(20, 20, 20, 20, "Health.Critical");
        fGrade.Grade.Should().Be("F");
    }

    [Fact]
    public void SemanticVersionHandlesMinorPatchDiffsAndPrereleasePrecedenceOperators()
    {
        SemanticVersion.TryParse("1.2.3", out var v123).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3", out var v123Same).Should().BeTrue();
        SemanticVersion.TryParse("1.3.0", out var v130).Should().BeTrue();
        SemanticVersion.TryParse("2.0.0", out var v200).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha", out var alpha).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha.1", out var alpha1).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha.beta", out var alphaBeta).Should().BeTrue();

        (v130 > v123).Should().BeTrue();
        (v200 >= v130).Should().BeTrue();
        (v123 <= alpha1).Should().BeFalse();
        (v123 <= v123Same).Should().BeTrue();
        (v123 == v123Same).Should().BeTrue();
        (v123 != v130).Should().BeTrue();
        (alpha1 < alphaBeta).Should().BeTrue();
        (alphaBeta > alpha1).Should().BeTrue();
        v123.ToString().Should().Be("1.2.3");
        alpha1.CompareTo(alpha).Should().BeGreaterThan(0);
    }

    [Fact]
    public void NumberFormatterCoversBytesGigabytesSecondsHoursAndHelpers()
    {
        NumberFormatter.ToPersianDigits(string.Empty).Should().BeEmpty();
        NumberFormatter.FormatNumber(12.34, "0.00", true).Should().Be("۱۲٫۳۴");
        NumberFormatter.FormatNumber(12.34, "0.00", false).Should().Be("12.34");
        NumberFormatter.FormatInteger(987, true).Should().Be("۹۸۷");
        NumberFormatter.FormatInteger(987, false).Should().Be("987");

        NumberFormatter.FormatBytes(500, true, true).Should().Be("۵۰۰ بایت");
        NumberFormatter.FormatBytes(500, false, false).Should().Be("\u200E500 B");
        NumberFormatter.FormatBytes(3L * 1024 * 1024 * 1024, true, true).Should().Be("۳ گیگابایت");
        NumberFormatter.FormatBytes(2048, false, false).Should().Be("\u200E2 KB");

        NumberFormatter.FormatDuration(TimeSpan.FromSeconds(15), true, true).Should().Be("۱۵ ثانیه");
        NumberFormatter.FormatDuration(TimeSpan.FromSeconds(15), false, false).Should().Be("15s");
        NumberFormatter.FormatDuration(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5), true, true).Should().Be("۲ ساعت و ۵ دقیقه");
        NumberFormatter.FormatDuration(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(10), false, false).Should().Be("3m 10s");
    }

    [Fact]
    public void AnomalyDetectorAndRulesAndGroupingCoverEdgeBranches()
    {
        AnomalyDetector.ComputeMedian([]).Should().Be(0);
        AnomalyDetector.ComputeMad([], 0).Should().Be(0);

        double[] odd = [10, 30, 20];
        AnomalyDetector.ComputeMedian(odd).Should().Be(20);

        // False-spike guard: observed <= 1.5 * median must return null even if z-score could trigger
        double[] flat40 = [40, 40, 40, 40, 40, 40, 40];
        var notASpike = AnomalyDetector.Evaluate(new AppKey("exe:a"), "App", AnomalyMetricKind.CpuPercent, 55, flat40, DateTimeOffset.UtcNow);
        notASpike.Should().BeNull();

        var evaluator = new RulesEvaluator();
        var app = new AppIdentity(new AppKey("exe:sync"), "SyncTool", @"C:\Tools\sync.exe");
        var wildcard = new NetworkAlertRule("w", "Any Upload", "*", 5);
        var zeroRule = new NetworkAlertRule("z", "Zero", "*", 0);
        var fired = evaluator.EvaluateUploadPerHourRules([zeroRule, wildcard], app, 10L * 1024 * 1024, DateTimeOffset.UtcNow);
        fired.Should().ContainSingle().Which.Id.Should().Be("w");

        var resolver = new GroupingResolver();
        var rootExe = resolver.Resolve(@"\.exe");
        rootExe.DisplayName.Should().Be("Unknown");
    }
    [Fact]
    public void TelemetryRecordsAndValueObjectsExposeAllPropertiesCleanly()
    {
        var now = DateTimeOffset.UtcNow;
        var key = new AppKey("exe:app");
        var health = new AppHealthScore(90, 95, 85, 90, "Healthy");

        var stats = new AppAggregateStats(
            key, "App", @"C:\App.exe",
            TimeSpan.FromHours(2), TimeSpan.FromHours(1), TimeSpan.FromMinutes(30),
            15.5, 45.0, 100_000, 200_000, 150_000, 10_000, 20_000,
            12.0, 30.0, 500_000_000, 1_000_000, 2_000_000, health, "Work");

        stats.AppKey.Should().Be(key);
        stats.DisplayName.Should().Be("App");
        stats.ExecutablePath.Should().Be(@"C:\App.exe");
        stats.OpenDuration.Should().Be(TimeSpan.FromHours(2));
        stats.ActiveDuration.Should().Be(TimeSpan.FromHours(1));
        stats.OnScreenDuration.Should().Be(TimeSpan.FromMinutes(30));
        stats.AverageCpuPercent.Should().Be(15.5);
        stats.PeakCpuPercent.Should().Be(45.0);
        stats.AverageWorkingSetBytes.Should().Be(100_000);
        stats.PeakWorkingSetBytes.Should().Be(200_000);
        stats.PeakPrivateBytes.Should().Be(150_000);
        stats.DiskReadBytes.Should().Be(10_000);
        stats.DiskWriteBytes.Should().Be(20_000);
        stats.AverageGpuPercent.Should().Be(12.0);
        stats.PeakGpuPercent.Should().Be(30.0);
        stats.PeakGpuVramBytes.Should().Be(500_000_000);
        stats.NetworkSentBytes.Should().Be(1_000_000);
        stats.NetworkReceivedBytes.Should().Be(2_000_000);
        stats.HealthScore.Should().Be(health);
        stats.ProjectTag.Should().Be("Work");

        var audit = new PrivacyAuditEntry(key, "App", "Wi-Fi", NetworkInterfaceKind.WiFi, "1.2.3.4", "example.com", 100, 200, now, now, true);
        audit.AppKey.Should().Be(key);
        audit.DisplayName.Should().Be("App");
        audit.InterfaceName.Should().Be("Wi-Fi");
        audit.InterfaceKind.Should().Be(NetworkInterfaceKind.WiFi);
        audit.RemoteIp.Should().Be("1.2.3.4");
        audit.RemoteDomain.Should().Be("example.com");
        audit.BytesSent.Should().Be(100);
        audit.BytesReceived.Should().Be(200);
        audit.FirstSeenUtc.Should().Be(now);
        audit.LastSeenUtc.Should().Be(now);
        audit.IsFirstTimeDestination.Should().BeTrue();

        var focus = new FocusSession("fs-1", now, now.AddMinutes(25), now.AddMinutes(25), ["game.exe"], 1200, 300, new Dictionary<string, long> { ["game.exe"] = 300 });
        focus.Id.Should().Be("fs-1");
        focus.StartedUtc.Should().Be(now);
        focus.PlannedEndUtc.Should().Be(now.AddMinutes(25));
        focus.EndedUtc.Should().Be(now.AddMinutes(25));
        focus.DistractingApps.Should().ContainSingle();
        focus.FocusedSeconds.Should().Be(1200);
        focus.DistractedSeconds.Should().Be(300);
        focus.DistractionBreakdownSeconds.Should().ContainKey("game.exe");

        var res = new ResourceSample(key, now, 10.0, 1000, 2000, 3000, 4000, 5.0, 6000, "Window");
        res.AppKey.Should().Be(key);
        res.TimestampUtc.Should().Be(now);
        res.CpuPercent.Should().Be(10.0);
        res.WorkingSetBytes.Should().Be(1000);
        res.PrivateBytes.Should().Be(2000);
        res.DiskReadBytesDelta.Should().Be(3000);
        res.DiskWriteBytesDelta.Should().Be(4000);
        res.GpuPercent.Should().Be(5.0);
        res.GpuVramBytes.Should().Be(6000);
        res.WindowTitle.Should().Be("Window");

        var net = new NetworkSample(key, now, "Eth", NetworkInterfaceKind.Ethernet, NetworkProtocol.Tcp, "4.3.2.1", "host.com", 50, 60);
        net.AppKey.Should().Be(key);
        net.TimestampUtc.Should().Be(now);
        net.InterfaceName.Should().Be("Eth");
        net.InterfaceKind.Should().Be(NetworkInterfaceKind.Ethernet);
        net.Protocol.Should().Be(NetworkProtocol.Tcp);
        net.RemoteIp.Should().Be("4.3.2.1");
        net.RemoteDomain.Should().Be("host.com");
        net.BytesSent.Should().Be(50);
        net.BytesReceived.Should().Be(60);

        var sess = new UsageSession(42, key, now, now.AddHours(1), 3600, 1800, 2400, "Tag");
        sess.Id.Should().Be(42);
        sess.AppKey.Should().Be(key);
        sess.StartUtc.Should().Be(now);
        sess.EndUtc.Should().Be(now.AddHours(1));
        sess.OpenSeconds.Should().Be(3600);
        sess.ActiveSeconds.Should().Be(1800);
        sess.OnScreenSeconds.Should().Be(2400);
        sess.ProjectTag.Should().Be("Tag");

        var projRule = new ProjectTagRule("p1", "Project", ProjectMatchKind.ExecutablePath, "work", true);
        projRule.Id.Should().Be("p1");
        projRule.ProjectName.Should().Be("Project");
        projRule.MatchKind.Should().Be(ProjectMatchKind.ExecutablePath);
        projRule.Pattern.Should().Be("work");
        projRule.IsEnabled.Should().BeTrue();

        var netRule = new NetworkAlertRule("n1", "Alert", "*", 100, true);
        netRule.Id.Should().Be("n1");
        netRule.Name.Should().Be("Alert");
        netRule.AppPattern.Should().Be("*");
        netRule.MaxUploadMegabytesPerHour.Should().Be(100);
        netRule.IsEnabled.Should().BeTrue();

        var alert = new AnomalyAlert("a1", key, "App", AnomalyMetricKind.CpuPercent, 95.0, 20.0, 5.0, 3.5, now, "High CPU");
        alert.Id.Should().Be("a1");
        alert.AppKey.Should().Be(key);
        alert.DisplayName.Should().Be("App");
        alert.Metric.Should().Be(AnomalyMetricKind.CpuPercent);
        alert.ObservedValue.Should().Be(95.0);
        alert.BaselineMedian.Should().Be(20.0);
        alert.BaselineMad.Should().Be(5.0);
        alert.ZScore.Should().Be(3.5);
        alert.DetectedUtc.Should().Be(now);
        alert.Explanation.Should().Be("High CPU");
    }
}
