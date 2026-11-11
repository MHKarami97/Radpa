using Radpa.Domain.Entities;

namespace Radpa.Application.Options;

public enum UiLanguage
{
    Persian = 0,
    English = 1,
}

public enum UiThemeMode
{
    Dark = 0,
    Light = 1,
}

public sealed record UpdateCheckOptions
{
    public string Repository { get; init; } = "MHKarami97/Radpa";
    public int IntervalHours { get; init; } = 24;
    public bool Enabled { get; init; } = true;
}

public sealed record AppSettings
{
    public UiLanguage Language { get; init; } = UiLanguage.Persian;
    public UiThemeMode Theme { get; init; } = UiThemeMode.Dark;
    public bool UsePersianDigits { get; init; } = true;
    public int IdleThresholdSeconds { get; init; } = 120;
    public int SamplingIntervalSeconds { get; init; } = 2;
    public int FlushIntervalSeconds { get; init; } = 30;
    public int RetentionDays { get; init; } = 90;
    public bool PortableMode { get; init; }
    public bool RecordWindowTitles { get; init; }
    public bool DetailedNetworkEnabled { get; init; }
    public bool EncryptionEnabled { get; init; }
    public string? ProtectedEncryptionKeyBase64 { get; init; }
    public bool StartWithWindows { get; init; }
    public bool MinimizeToTray { get; init; } = true;
    public string? SkippedUpdateVersion { get; init; }
    public string? LastUpdateETag { get; init; }
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }
    public UpdateCheckOptions UpdateCheck { get; init; } = new();
    public IReadOnlyList<GroupingRule> GroupingRules { get; init; } = CreateDefaultGroupingRules();
    public IReadOnlyList<ProjectTagRule> ProjectRules { get; init; } = [];
    public IReadOnlyList<NetworkAlertRule> AlertRules { get; init; } = [];

    public static IReadOnlyList<GroupingRule> CreateDefaultGroupingRules() =>
    [
        new("grp-chrome", "Google Chrome", GroupingMatchKind.ExecutableName, "chrome.exe", "Google Chrome", true, 10),
        new("grp-edge", "Microsoft Edge", GroupingMatchKind.ExecutableName, "msedge.exe", "Microsoft Edge", true, 20),
        new("grp-firefox", "Mozilla Firefox", GroupingMatchKind.ExecutableName, "firefox.exe", "Mozilla Firefox", true, 30),
        new("grp-vscode", "Visual Studio Code", GroupingMatchKind.ExecutableName, "Code.exe", "Visual Studio Code", true, 40),
    ];
}
