namespace Radpa.Domain.ValueObjects;

/// <summary>
/// Canonical, case-insensitive key identifying an application or grouped process family.
/// </summary>
public readonly record struct AppKey
{
    public AppKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
