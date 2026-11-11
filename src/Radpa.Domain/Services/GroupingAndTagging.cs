using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Services;

/// <summary>
/// Resolves multi-process applications (browsers, Electron, packaged UWP/MSIX) into unified AppIdentities.
/// </summary>
public sealed class GroupingResolver
{
    private readonly IReadOnlyList<GroupingRule> _rules;

    public GroupingResolver(IEnumerable<GroupingRule>? rules = null)
    {
        _rules = (rules ?? [])
            .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Pattern))
            .OrderBy(r => r.Priority)
            .ToArray();
    }

    public AppIdentity Resolve(string executablePath, string? packageFamilyName = null, string? productName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var normalizedPath = executablePath.Trim();
        var fileName = Path.GetFileName(normalizedPath);
        var baseName = Path.GetFileNameWithoutExtension(normalizedPath);

        foreach (var rule in _rules)
        {
            var matched = rule.MatchKind switch
            {
                GroupingMatchKind.ExecutablePath => string.Equals(normalizedPath, rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                GroupingMatchKind.ExecutableName => string.Equals(fileName, rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(baseName, rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                GroupingMatchKind.DirectoryPrefix => normalizedPath.StartsWith(rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                GroupingMatchKind.PackageFamily => !string.IsNullOrWhiteSpace(packageFamilyName)
                    && string.Equals(packageFamilyName.Trim(), rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (matched)
            {
                var groupKey = new AppKey($"group:{rule.TargetGroupName}");
                return new AppIdentity(groupKey, rule.TargetGroupName, normalizedPath, packageFamilyName);
            }
        }

        // Default multi-process grouping: by PackageFamilyName if MSIX/Store app, otherwise by normalized executable path.
        if (!string.IsNullOrWhiteSpace(packageFamilyName))
        {
            var pkgKey = new AppKey($"pkg:{packageFamilyName}");
            var display = !string.IsNullOrWhiteSpace(productName) ? productName : baseName;
            return new AppIdentity(pkgKey, display, normalizedPath, packageFamilyName);
        }

        var exeKey = new AppKey($"exe:{normalizedPath}");
        var displayName = !string.IsNullOrWhiteSpace(productName) ? productName : PrettifyName(baseName);
        return new AppIdentity(exeKey, displayName, normalizedPath);
    }

    private static string PrettifyName(string baseName) =>
        string.IsNullOrWhiteSpace(baseName)
            ? "Unknown"
            : char.ToUpperInvariant(baseName[0]) + baseName[1..];
}

public sealed class ProjectTagMatcher
{
    private readonly IReadOnlyList<ProjectTagRule> _rules;

    public ProjectTagMatcher(IEnumerable<ProjectTagRule>? rules = null)
    {
        _rules = (rules ?? [])
            .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Pattern))
            .ToArray();
    }

    public string? Match(AppIdentity app, string? windowTitle)
    {
        ArgumentNullException.ThrowIfNull(app);

        var exeName = Path.GetFileNameWithoutExtension(app.ExecutablePath);
        foreach (var rule in _rules)
        {
            var pattern = rule.Pattern.Trim();
            var matched = rule.MatchKind switch
            {
                ProjectMatchKind.ProcessName => string.Equals(exeName, pattern, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(app.DisplayName, pattern, StringComparison.OrdinalIgnoreCase),
                ProjectMatchKind.ExecutablePath => app.ExecutablePath.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                ProjectMatchKind.WindowTitleContains => !string.IsNullOrWhiteSpace(windowTitle)
                    && windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (matched)
            {
                return rule.ProjectName;
            }
        }

        return null;
    }
}
