using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Domain.Services;

namespace Radpa.Application.Updates;

public sealed class UpdateCoordinator
{
    private readonly IUpdateChecker _checker;
    private readonly IClock _clock;
    private readonly ISettingsStore _settingsStore;
    private readonly INotificationService _notifications;

    public UpdateCoordinator(
        IUpdateChecker checker,
        IClock clock,
        ISettingsStore settingsStore,
        INotificationService notifications)
    {
        _checker = checker;
        _clock = clock;
        _settingsStore = settingsStore;
        _notifications = notifications;
    }

    public async Task<(UpdateCheckResult Result, AppSettings UpdatedSettings)> CheckIfDueAsync(
        AppSettings settings,
        SemanticVersion currentVersion,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.UpdateCheck.Enabled && !force)
        {
            return (new UpdateCheckResult(UpdateCheckStatus.Disabled, currentVersion, null, null, null, settings.LastUpdateETag, null), settings);
        }

        var now = _clock.UtcNow;
        var interval = TimeSpan.FromHours(Math.Max(1, settings.UpdateCheck.IntervalHours));
        if (!force && settings.LastUpdateCheckUtc is not null && now - settings.LastUpdateCheckUtc.Value < interval)
        {
            return (new UpdateCheckResult(UpdateCheckStatus.NotModified, currentVersion, null, null, null, settings.LastUpdateETag, null), settings);
        }

        var result = await _checker.CheckAsync(currentVersion, settings.LastUpdateETag, cancellationToken);
        var nextSettings = settings with
        {
            LastUpdateCheckUtc = now,
            LastUpdateETag = result.ETag ?? settings.LastUpdateETag,
        };

        await _settingsStore.SaveAsync(nextSettings, cancellationToken);

        if (result.Status == UpdateCheckStatus.UpdateAvailable &&
            result.LatestVersion is not null &&
            !string.Equals(settings.SkippedUpdateVersion, result.LatestVersion.Value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _notifications.ShowNotification(
                $"Radpa v{result.LatestVersion} Available",
                "A new release is available on GitHub.");
        }

        return (result, nextSettings);
    }
}
