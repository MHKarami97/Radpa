using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Services;

/// <summary>
/// Pure domain logic for merging adjacent usage session segments and computing open/active/on-screen intervals.
/// </summary>
public static class SessionMerger
{
    public static void AccumulateTick(
        ref long openSeconds,
        ref long activeSeconds,
        ref long onScreenSeconds,
        long elapsedSeconds,
        bool isOpen,
        bool isForeground,
        bool isScreenLocked,
        bool isDisplayOff,
        TimeSpan idleDuration,
        TimeSpan idleThreshold)
    {
        if (elapsedSeconds <= 0 || !isOpen)
        {
            return;
        }

        openSeconds += elapsedSeconds;

        var screenVisible = !isScreenLocked && !isDisplayOff;
        if (isForeground && screenVisible)
        {
            onScreenSeconds += elapsedSeconds;
            if (idleDuration < idleThreshold)
            {
                activeSeconds += elapsedSeconds;
            }
        }
    }

    public static IReadOnlyList<UsageSession> MergeAdjacent(
        IEnumerable<UsageSession> sessions,
        TimeSpan maxGap)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var ordered = sessions
            .OrderBy(s => s.AppKey.Value, StringComparer.Ordinal)
            .ThenBy(s => s.StartUtc)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var merged = new List<UsageSession>(ordered.Count);
        var current = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            var sameApp = current.AppKey == next.AppKey &&
                          string.Equals(current.ProjectTag, next.ProjectTag, StringComparison.OrdinalIgnoreCase);

            if (sameApp && next.StartUtc - current.EndUtc <= maxGap)
            {
                var endUtc = next.EndUtc > current.EndUtc ? next.EndUtc : current.EndUtc;
                current = current with
                {
                    EndUtc = endUtc,
                    OpenSeconds = current.OpenSeconds + next.OpenSeconds,
                    ActiveSeconds = current.ActiveSeconds + next.ActiveSeconds,
                    OnScreenSeconds = current.OnScreenSeconds + next.OnScreenSeconds,
                };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }
}
