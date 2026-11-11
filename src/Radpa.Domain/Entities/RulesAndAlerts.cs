using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Entities;

public enum GroupingMatchKind
{
    ExecutablePath = 0,
    ExecutableName = 1,
    DirectoryPrefix = 2,
    PackageFamily = 3,
}

public sealed record GroupingRule(
    string Id,
    string Name,
    GroupingMatchKind MatchKind,
    string Pattern,
    string TargetGroupName,
    bool IsEnabled = true,
    int Priority = 100);

public enum ProjectMatchKind
{
    ProcessName = 0,
    ExecutablePath = 1,
    WindowTitleContains = 2,
}

public sealed record ProjectTagRule(
    string Id,
    string ProjectName,
    ProjectMatchKind MatchKind,
    string Pattern,
    bool IsEnabled = true);

public sealed record NetworkAlertRule(
    string Id,
    string Name,
    string AppPattern,
    long MaxUploadMegabytesPerHour,
    bool IsEnabled = true);

public enum AnomalyMetricKind
{
    CpuPercent = 0,
    UploadBytesPerMinute = 1,
}

public sealed record AnomalyAlert(
    string Id,
    AppKey AppKey,
    string DisplayName,
    AnomalyMetricKind Metric,
    double ObservedValue,
    double BaselineMedian,
    double BaselineMad,
    double ZScore,
    DateTimeOffset DetectedUtc,
    string Explanation);

public sealed record FocusSession(
    string Id,
    DateTimeOffset StartedUtc,
    DateTimeOffset PlannedEndUtc,
    DateTimeOffset? EndedUtc,
    IReadOnlyList<string> DistractingApps,
    long FocusedSeconds,
    long DistractedSeconds,
    IReadOnlyDictionary<string, long> DistractionBreakdownSeconds);
