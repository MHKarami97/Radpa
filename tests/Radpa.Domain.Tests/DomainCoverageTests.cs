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
    }

    [Fact]
    public void SemanticVersionHandlesMinorPatchDiffsAndPrereleasePrecedenceOperators()
    {
        SemanticVersion.TryParse("1.2.3", out var v123).Should().BeTrue();
        SemanticVersion.TryParse("1.3.0", out var v130).Should().BeTrue();
        SemanticVersion.TryParse("2.0.0", out var v200).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha", out var alpha).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha.1", out var alpha1).Should().BeTrue();
        SemanticVersion.TryParse("1.2.3-alpha.beta", out var alphaBeta).Should().BeTrue();

        (v130 > v123).Should().BeTrue();
        (v200 >= v130).Should().BeTrue();
        (v123 <= alpha1).Should().BeFalse();
        (v123 <= v123).Should().BeTrue();
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
}
