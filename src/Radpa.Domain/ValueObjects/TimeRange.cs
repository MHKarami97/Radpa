namespace Radpa.Domain.ValueObjects;

/// <summary>
/// Represents a half-open UTC time interval [StartUtc, EndUtc).
/// </summary>
public readonly record struct TimeRange
{
    public TimeRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (endUtc < startUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endUtc), "End time cannot precede start time.");
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public bool Contains(DateTimeOffset timestampUtc)
    {
        var utc = timestampUtc.ToUniversalTime();
        return utc >= StartUtc && utc < EndUtc;
    }

    public TimeRange? Intersect(TimeRange other)
    {
        var start = StartUtc > other.StartUtc ? StartUtc : other.StartUtc;
        var end = EndUtc < other.EndUtc ? EndUtc : other.EndUtc;
        return end > start ? new TimeRange(start, end) : null;
    }

    public static TimeRange Today(DateTimeOffset nowLocal)
    {
        var startOfDayLocal = new DateTimeOffset(nowLocal.Date, nowLocal.Offset);
        return new TimeRange(startOfDayLocal.ToUniversalTime(), nowLocal.ToUniversalTime());
    }

    public static TimeRange LastDays(DateTimeOffset nowUtc, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var utc = nowUtc.ToUniversalTime();
        return new TimeRange(utc.AddDays(-days), utc);
    }
}
