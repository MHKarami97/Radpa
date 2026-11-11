using FluentAssertions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class AnomalyDetectorTests
{
    private static readonly AppKey SampleAppKey = new("exe:app.exe");
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EvaluateReturnsNullWhenBaselineHasTooFewSamples()
    {
        double[] history = [10, 12, 11, 10, 12];
        var alert = AnomalyDetector.Evaluate(SampleAppKey, "App", AnomalyMetricKind.CpuPercent, 90.0, history, NowUtc);
        alert.Should().BeNull();
    }

    [Fact]
    public void EvaluateReturnsNullWhenObservedValueBelowMeaningfulFloor()
    {
        double[] history = [1, 1, 1, 1, 1, 1, 1, 1];
        var cpuAlert = AnomalyDetector.Evaluate(SampleAppKey, "App", AnomalyMetricKind.CpuPercent, 20.0, history, NowUtc);
        var netAlert = AnomalyDetector.Evaluate(SampleAppKey, "App", AnomalyMetricKind.UploadBytesPerMinute, 2 * 1024 * 1024, history, NowUtc);

        cpuAlert.Should().BeNull();
        netAlert.Should().BeNull();
    }

    [Fact]
    public void EvaluateZeroVarianceBaselineHandlesMadFloorWithoutDivideByZeroAndFlagsTrueCpuSpike()
    {
        double[] steadyHistory = [5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0];

        var alert = AnomalyDetector.Evaluate(SampleAppKey, "HeavyApp", AnomalyMetricKind.CpuPercent, 75.0, steadyHistory, NowUtc);

        alert.Should().NotBeNull();
        alert!.DisplayName.Should().Be("HeavyApp");
        alert.BaselineMedian.Should().Be(5.0);
        alert.BaselineMad.Should().Be(0.0);
        alert.ZScore.Should().BeGreaterThan(3.5);
        alert.Explanation.Should().Contain("CpuPercent");
    }

    [Fact]
    public void EvaluateDetectsUnusualUploadSpikeOverRollingBaseline()
    {
        const double OneMb = 1024.0 * 1024.0;
        double[] baseline = [0.2 * OneMb, 0.3 * OneMb, 0.25 * OneMb, 0.4 * OneMb, 0.2 * OneMb, 0.35 * OneMb, 0.3 * OneMb];

        var alert = AnomalyDetector.Evaluate(SampleAppKey, "Browser", AnomalyMetricKind.UploadBytesPerMinute, 40 * OneMb, baseline, NowUtc);

        alert.Should().NotBeNull();
        alert!.Metric.Should().Be(AnomalyMetricKind.UploadBytesPerMinute);
        alert.ObservedValue.Should().Be(40 * OneMb);
        alert.ZScore.Should().BeGreaterThan(3.5);
    }
}
