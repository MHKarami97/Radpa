using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Entities;

public sealed record AppIdentity
{
    public AppIdentity(
        AppKey key,
        string displayName,
        string executablePath,
        string? packageFamilyName = null,
        string? publisher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        Key = key;
        DisplayName = displayName.Trim();
        ExecutablePath = executablePath.Trim();
        PackageFamilyName = string.IsNullOrWhiteSpace(packageFamilyName) ? null : packageFamilyName.Trim();
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher.Trim();
    }

    public AppKey Key { get; }

    public string DisplayName { get; }

    public string ExecutablePath { get; }

    public string? PackageFamilyName { get; }

    public string? Publisher { get; }
}
