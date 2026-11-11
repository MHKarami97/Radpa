using System.Globalization;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Services;

/// <summary>
/// Explainable rolling median / MAD anomaly detector requiring no external ML libraries.
/// Uses modified Z-score: M = 0.6745 * (x - median) / MAD.
/// </summary>
public static class AnomalyDetector
{
    private const double ConsistencyConstant = 0.6745;
    private const double DefaultZThreshold = 3.5;

    public static AnomalyAlert? Evaluate(
        AppKey appKey,
        string displayName,
        AnomalyMetricKind metric,
        double observedValue,
        ReadOnlySpan<double> baselineHistory,
        DateTimeOffset nowUtc,
        double zThreshold = DefaultZThreshold)
    {
        if (baselineHistory.Length < 6)
        {
            return null;
        }

        var minimumMeaningfulValue = metric switch
        {
            AnomalyMetricKind.CpuPercent => 25.0,
            AnomalyMetricKind.UploadBytesPerMinute => 5.0 * 1024 * 1024,
            _ => 0.0,
        };

        if (observedValue < minimumMeaningfulValue)
        {
            return null;
        }

        var median = ComputeMedian(baselineHistory);
        var mad = ComputeMad(baselineHistory, median);

        // Floor MAD to avoid divide-by-zero when baseline has zero variance
        var effectiveMad = Math.Max(mad, metric == AnomalyMetricKind.CpuPercent ? 3.0 : 512.0 * 1024);
        var zScore = ConsistencyConstant * (observedValue - median) / effectiveMad;

        if (zScore < zThreshold || observedValue <= median * 1.5)
        {
            return null;
        }

        var explanation = string.Format(
            CultureInfo.InvariantCulture,
            "{0} reached {1:F1} vs median {2:F1} (MAD {3:F1}, z={4:F2})",
            metric,
            observedValue,
            median,
            mad,
            zScore);

        return new AnomalyAlert(
            Guid.NewGuid().ToString("N"),
            appKey,
            displayName,
            metric,
            observedValue,
            median,
            mad,
            zScore,
            nowUtc,
            explanation);
    }

    public static double ComputeMedian(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return 0;
        }

        var buffer = values.ToArray();
        Array.Sort(buffer);
        var mid = buffer.Length / 2;
        return buffer.Length % 2 == 0
            ? (buffer[mid - 1] + buffer[mid]) / 2.0
            : buffer[mid];
    }

    public static double ComputeMad(ReadOnlySpan<double> values, double median)
    {
        if (values.IsEmpty)
        {
            return 0;
        }

        var deviations = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            deviations[i] = Math.Abs(values[i] - median);
        }

        return ComputeMedian(deviations);
    }
}
