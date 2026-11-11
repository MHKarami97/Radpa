using FluentAssertions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class HealthScoreAndRulesTests
{
    [Fact]
    public void CalculateLightAppProducesOptimalGradeA()
    {
        var score = HealthScoreCalculator.Calculate(
            averageCpuPercent: 2.0,
            peakWorkingSetBytes: 120L * 1024 * 1024,
            diskReadWriteBytes: 10L * 1024 * 1024,
            openDuration: TimeSpan.FromMinutes(20),
            activeDuration: TimeSpan.FromMinutes(18),
            networkSentBytes: 0,
            networkReceivedBytes: 1024 * 1024,
            isStartupApp: false);

        score.TotalScore.Should().BeGreaterThanOrEqualTo(85);
        score.Grade.Should().Be("A");
        score.ExplanationKey.Should().Be("Health.Optimal");
    }

    [Fact]
    public void CalculateHeavyBackgroundStartupAppProducesLowGradeAndBreakdown()
    {
        var score = HealthScoreCalculator.Calculate(
            averageCpuPercent: 45.0,
            peakWorkingSetBytes: 4L * 1024 * 1024 * 1024,
            diskReadWriteBytes: 8L * 1024 * 1024 * 1024,
            openDuration: TimeSpan.FromHours(4),
            activeDuration: TimeSpan.FromMinutes(2),
            networkSentBytes: 500L * 1024 * 1024,
            networkReceivedBytes: 2L * 1024 * 1024 * 1024,
            isStartupApp: true);

        score.ResourceScore.Should().BeLessThan(20);
        score.NetworkScore.Should().BeLessThan(40);
        score.StartupImpactScore.Should().Be(40.0);
        score.TotalScore.Should().BeLessThan(40);
        score.Grade.Should().Be("F");
        score.ExplanationKey.Should().Be("Health.Critical");
    }

    [Fact]
    public void RulesEvaluatorMatchesThresholdAndHonorsCooldown()
    {
        var evaluator = new RulesEvaluator(TimeSpan.FromMinutes(30));
        var app = new AppIdentity(new AppKey("exe:uploader"), "Uploader", @"C:\Apps\uploader.exe");
        var rules = new[]
        {
            new NetworkAlertRule("r1", "High Upload", "uploader", MaxUploadMegabytesPerHour: 100, IsEnabled: true),
            new NetworkAlertRule("r2", "Disabled", "*", MaxUploadMegabytesPerHour: 1, IsEnabled: false),
            new NetworkAlertRule("r3", "Unmatched", "chrome", MaxUploadMegabytesPerHour: 10, IsEnabled: true),
        };

        var t0 = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        // Below threshold -> no triggers
        evaluator.EvaluateUploadPerHourRules(rules, app, 50L * 1024 * 1024, t0).Should().BeEmpty();

        // Crosses threshold -> fires r1
        var first = evaluator.EvaluateUploadPerHourRules(rules, app, 120L * 1024 * 1024, t0.AddMinutes(1));
        first.Should().ContainSingle().Which.Id.Should().Be("r1");

        // Within cooldown -> suppressed
        evaluator.EvaluateUploadPerHourRules(rules, app, 200L * 1024 * 1024, t0.AddMinutes(15)).Should().BeEmpty();

        // After cooldown -> fires again
        var afterCooldown = evaluator.EvaluateUploadPerHourRules(rules, app, 200L * 1024 * 1024, t0.AddMinutes(32));
        afterCooldown.Should().ContainSingle().Which.Id.Should().Be("r1");
    }
}
