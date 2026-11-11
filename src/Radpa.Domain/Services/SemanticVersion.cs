using System.Globalization;

namespace Radpa.Domain.Services;

/// <summary>
/// Strict Semantic Versioning 2.0.0 parser and precedence comparer.
/// </summary>
public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease.Trim();
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? PreRelease { get; }

    public bool IsPreRelease => PreRelease is not null;

    public static bool TryParse(string? input, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var span = input.AsSpan().Trim();
        if (span.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            span = span[1..];
        }

        var buildIdx = span.IndexOf('+');
        if (buildIdx >= 0)
        {
            span = span[..buildIdx];
        }

        string? preRelease = null;
        var dashIdx = span.IndexOf('-');
        if (dashIdx >= 0)
        {
            var preSpan = span[(dashIdx + 1)..];
            if (preSpan.IsEmpty)
            {
                return false;
            }

            preRelease = preSpan.ToString();
            span = span[..dashIdx];
        }

        Span<Range> parts = stackalloc Range[4];
        var count = span.Split(parts, '.');
        if (count != 3)
        {
            return false;
        }

        if (!int.TryParse(span[parts[0]], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(span[parts[1]], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(span[parts[2]], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0)
        {
            return cmp;
        }

        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        var leftParts = PreRelease.Split('.');
        var rightParts = other.PreRelease.Split('.');
        var len = Math.Min(leftParts.Length, rightParts.Length);
        for (var i = 0; i < len; i++)
        {
            var lNum = int.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var lVal);
            var rNum = int.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rVal);
            cmp = (lNum, rNum) switch
            {
                (true, true) => lVal.CompareTo(rVal),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[i], rightParts[i]),
            };
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        PreRelease is null
            ? FormattableString.Invariant($"{Major}.{Minor}.{Patch}")
            : FormattableString.Invariant($"{Major}.{Minor}.{Patch}-{PreRelease}");
}
