using Radpa.Domain.Services;

namespace Radpa.Application.Abstractions;

public enum UpdateCheckStatus
{
    Disabled = 0,
    UpToDate = 1,
    UpdateAvailable = 2,
    NotModified = 3,
    RateLimited = 4,
    OfflineOrFailed = 5,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    SemanticVersion CurrentVersion,
    SemanticVersion? LatestVersion,
    string? ReleaseUrl,
    string? ReleaseNotes,
    string? ETag,
    DateTimeOffset? RetryAfterUtc,
    string? ErrorMessage = null);

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(
        SemanticVersion currentVersion,
        string? lastETag,
        CancellationToken cancellationToken);
}

public interface IElevationService
{
    bool IsHelperAvailable { get; }
    Task<bool> StartNetworkHelperElevatedAsync(string pipeName, CancellationToken cancellationToken);
    void StopNetworkHelper();
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Alert = 2,
}

public interface INotificationService
{
    void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info);
}

public interface IStartupRegistrationService
{
    bool IsRegistered();
    void SetEnabled(bool enabled, string executablePath);
}

public interface IDnsResolverCache
{
    string? TryGetCachedHost(string ipAddress);
    void QueueResolve(string ipAddress);
}

public readonly record struct SelfMetricsSnapshot(double CpuPercent, long WorkingSetBytes, long PrivateBytes);

public interface ISelfMetricsProvider
{
    SelfMetricsSnapshot Capture();
}

public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void LogError(string message, Exception? exception = null);
}
