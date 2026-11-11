using Radpa.Application.Abstractions;
using Radpa.Domain.Entities;

namespace Radpa.Application.Engine;

public sealed class FocusSessionManager
{
    private readonly IClock _clock;
    private readonly INotificationService _notifications;
    private readonly Lock _sync = new();

    private string? _sessionId;
    private DateTimeOffset _startedUtc;
    private DateTimeOffset _plannedEndUtc;
    private HashSet<string> _distractingApps = new(StringComparer.OrdinalIgnoreCase);
    private long _focusedSeconds;
    private long _distractedSeconds;
    private readonly Dictionary<string, long> _distractionBreakdown = new(StringComparer.OrdinalIgnoreCase);

    public FocusSessionManager(IClock clock, INotificationService notifications)
    {
        _clock = clock;
        _notifications = notifications;
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _sessionId is not null;
            }
        }
    }

    public event EventHandler<FocusSession>? SessionCompleted;

    public void Start(TimeSpan duration, IEnumerable<string> distractingAppPatterns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        lock (_sync)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _startedUtc = _clock.UtcNow;
            _plannedEndUtc = _startedUtc.Add(duration);
            _distractingApps = new HashSet<string>(
                distractingAppPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);
            _focusedSeconds = 0;
            _distractedSeconds = 0;
            _distractionBreakdown.Clear();
        }
    }

    public void RecordForegroundActivity(AppIdentity app, long elapsedSeconds, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (elapsedSeconds <= 0)
        {
            return;
        }

        FocusSession? completed = null;
        lock (_sync)
        {
            if (_sessionId is null)
            {
                return;
            }

            var isDistracting = false;
            foreach (var pattern in _distractingApps)
            {
                if (app.DisplayName.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                    app.ExecutablePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    isDistracting = true;
                    break;
                }
            }

            if (isDistracting)
            {
                _distractedSeconds += elapsedSeconds;
                _distractionBreakdown[app.DisplayName] = _distractionBreakdown.GetValueOrDefault(app.DisplayName) + elapsedSeconds;
            }
            else
            {
                _focusedSeconds += elapsedSeconds;
            }

            if (nowUtc >= _plannedEndUtc)
            {
                completed = BuildSummaryLocked(nowUtc);
                _sessionId = null;
            }
        }

        if (completed is not null)
        {
            SessionCompleted?.Invoke(this, completed);
            _notifications.ShowNotification(
                "Radpa Focus Session Complete",
                $"Focused: {completed.FocusedSeconds / 60}m | Distracted: {completed.DistractedSeconds / 60}m");
        }
    }

    public FocusSession? Stop()
    {
        lock (_sync)
        {
            if (_sessionId is null)
            {
                return null;
            }

            var summary = BuildSummaryLocked(_clock.UtcNow);
            _sessionId = null;
            SessionCompleted?.Invoke(this, summary);
            return summary;
        }
    }

    public FocusSession? GetCurrentSnapshot()
    {
        lock (_sync)
        {
            return _sessionId is null ? null : BuildSummaryLocked(_clock.UtcNow);
        }
    }

    private FocusSession BuildSummaryLocked(DateTimeOffset endedUtc) =>
        new(
            _sessionId!,
            _startedUtc,
            _plannedEndUtc,
            endedUtc,
            _distractingApps.ToArray(),
            _focusedSeconds,
            _distractedSeconds,
            new Dictionary<string, long>(_distractionBreakdown, StringComparer.OrdinalIgnoreCase));
}
