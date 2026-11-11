using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Services;

/// <summary>
/// Transparent deterministic App Health Score (0-100):
/// 60% Resource efficiency (CPU, RAM, Disk) + 25% Network discipline + 15% Startup/Background residency efficiency.
/// </summary>
public static class HealthScoreCalculator
{
    public static AppHealthScore Calculate(
        double averageCpuPercent,
        long peakWorkingSetBytes,
        long diskReadWriteBytes,
        TimeSpan openDuration,
        TimeSpan activeDuration,
        long networkSentBytes,
        long networkReceivedBytes,
        bool isStartupApp = false)
    {
        // Resource sub-score (0..100)
        var cpuPenalty = Math.Min(50.0, Math.Max(0.0, averageCpuPercent) * 1.25);
        var ramGb = Math.Max(0.0, peakWorkingSetBytes) / (1024.0 * 1024.0 * 1024.0);
        var ramPenalty = Math.Min(35.0, ramGb * 12.0);
        var diskGb = Math.Max(0.0, diskReadWriteBytes) / (1024.0 * 1024.0 * 1024.0);
        var diskPenalty = Math.Min(15.0, diskGb * 2.5);
        var resourceScore = Math.Clamp(100.0 - cpuPenalty - ramPenalty - diskPenalty, 0.0, 100.0);

        // Network sub-score (0..100) - penalize heavy unexplained uploads or high volume
        var totalNetMb = Math.Max(0.0, networkSentBytes + networkReceivedBytes) / (1024.0 * 1024.0);
        var sentMb = Math.Max(0.0, networkSentBytes) / (1024.0 * 1024.0);
        var volumePenalty = Math.Min(50.0, Math.Log10(1.0 + totalNetMb) * 12.0);
        var uploadPenalty = Math.Min(50.0, Math.Log10(1.0 + sentMb) * 16.0);
        var networkScore = Math.Clamp(100.0 - volumePenalty - uploadPenalty, 0.0, 100.0);

        // Startup & background residency sub-score (0..100)
        var startupPenalty = isStartupApp ? 25.0 : 0.0;
        var idleOpenPenalty = 0.0;
        if (openDuration.TotalMinutes > 30)
        {
            var activeRatio = activeDuration.TotalSeconds / Math.Max(1.0, openDuration.TotalSeconds);
            if (activeRatio < 0.05)
            {
                idleOpenPenalty = 35.0;
            }
            else if (activeRatio < 0.2)
            {
                idleOpenPenalty = 15.0;
            }
        }

        var startupScore = Math.Clamp(100.0 - startupPenalty - idleOpenPenalty, 0.0, 100.0);

        var total = Math.Round((resourceScore * 0.60) + (networkScore * 0.25) + (startupScore * 0.15), 1);
        var explanationKey = total switch
        {
            >= 85 => "Health.Optimal",
            >= 70 => "Health.Moderate",
            >= 55 => "Health.Heavy",
            _ => "Health.Critical",
        };

        return new AppHealthScore(
            total,
            Math.Round(resourceScore, 1),
            Math.Round(networkScore, 1),
            Math.Round(startupScore, 1),
            explanationKey);
    }
}

public sealed class RulesEvaluator
{
    private readonly Dictionary<string, DateTimeOffset> _lastFiredUtc = new(StringComparer.Ordinal);
    private readonly TimeSpan _cooldown;

    public RulesEvaluator(TimeSpan? cooldown = null)
    {
        _cooldown = cooldown ?? TimeSpan.FromMinutes(30);
    }

    public IReadOnlyList<NetworkAlertRule> EvaluateUploadPerHourRules(
        IReadOnlyList<NetworkAlertRule> rules,
        AppIdentity app,
        long uploadBytesLastHour,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(app);

        var triggered = new List<NetworkAlertRule>();
        var uploadMb = uploadBytesLastHour / (1024.0 * 1024.0);
        var exeName = Path.GetFileNameWithoutExtension(app.ExecutablePath);

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled || rule.MaxUploadMegabytesPerHour <= 0)
            {
                continue;
            }

            var appMatches = rule.AppPattern == "*"
                || app.DisplayName.Contains(rule.AppPattern, StringComparison.OrdinalIgnoreCase)
                || exeName.Contains(rule.AppPattern, StringComparison.OrdinalIgnoreCase)
                || app.ExecutablePath.Contains(rule.AppPattern, StringComparison.OrdinalIgnoreCase);

            if (!appMatches || uploadMb < rule.MaxUploadMegabytesPerHour)
            {
                continue;
            }

            var stateKey = $"{rule.Id}|{app.Key.Value}";
            if (_lastFiredUtc.TryGetValue(stateKey, out var last) && nowUtc - last < _cooldown)
            {
                continue;
            }

            _lastFiredUtc[stateKey] = nowUtc;
            triggered.Add(rule);
        }

        return triggered;
    }
}
