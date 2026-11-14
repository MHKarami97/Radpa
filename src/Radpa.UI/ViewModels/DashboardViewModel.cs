using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Radpa.UI.Controls;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public enum DashboardRangePreset
{
    Today = 0,
    Last7Days = 1,
    Last30Days = 2,
    Custom = 3,
}

public sealed record AppMetricRowViewModel(
    AppAggregateStats Raw,
    string DisplayName,
    string ExecutablePath,
    string OpenText,
    string ActiveText,
    string OnScreenText,
    string CpuText,
    string RamText,
    string DiskText,
    string GpuText,
    string SentText,
    string ReceivedText,
    string HealthText);

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardQueryService _queryService;
    private readonly IClock _clock;
    private DashboardSnapshot? _lastSnapshot;

    public DashboardViewModel(DashboardQueryService queryService, IClock clock, Localizer loc)
    {
        _queryService = queryService;
        _clock = clock;
        Loc = loc;
        CustomStartDate = _clock.LocalNow.Date.AddDays(-7);
        CustomEndDate = _clock.LocalNow.Date;
        IsNetworkAccessAvailable = CheckIsAdministrator();
    }

    public Localizer Loc { get; }

    [ObservableProperty]
    public partial DashboardRangePreset SelectedRange { get; set; } = DashboardRangePreset.Today;

    [ObservableProperty]
    public partial DateTimeOffset? CustomStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? CustomEndDate { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalActiveText { get; set; } = "0s";

    [ObservableProperty]
    public partial string TotalSentText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string TotalReceivedText { get; set; } = "0 B";

    [ObservableProperty]

    public partial string TrackedAppsCountText { get; set; } = "0";


    [ObservableProperty]
    public partial string LastUpdatedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAppDetailOpen { get; set; }

    [ObservableProperty]
    public partial AppMetricRowViewModel? DetailedApp { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> DetailedAppDurationBars { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> DetailedAppNetworkBars { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PrivacyAuditRowViewModel> DetailedAppPrivacyRows { get; set; } = [];

    [ObservableProperty]
    public partial AppMetricRowViewModel? SelectedApp { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TimelineBucket> TimelineBuckets { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> TopAppBars { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> InterfaceNetworkBars { get; set; } = [];

    [ObservableProperty]
    public partial bool UsePersianDigits { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCollectingInitialData { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNetworkAccessAvailable { get; set; } = true;

    public bool IsAdminRequiredNoticeVisible => !IsNetworkAccessAvailable;

    public double NetworkSectionOpacity => IsNetworkAccessAvailable ? 1.0 : 0.35;

    partial void OnIsNetworkAccessAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAdminRequiredNoticeVisible));
        OnPropertyChanged(nameof(NetworkSectionOpacity));
    }

    public ObservableCollection<AppMetricRowViewModel> Rows { get; } = [];

    public bool IsCustomRange => SelectedRange == DashboardRangePreset.Custom;

    public DashboardSnapshot? LatestSnapshot => _lastSnapshot;

    public TimeRange GetEffectiveRange()
    {
        var nowUtc = _clock.UtcNow;
        return SelectedRange switch
        {
            DashboardRangePreset.Today => TimeRange.Today(_clock.LocalNow),
            DashboardRangePreset.Last7Days => TimeRange.LastDays(nowUtc, 7),
            DashboardRangePreset.Last30Days => TimeRange.LastDays(nowUtc, 30),
            DashboardRangePreset.Custom => BuildCustomRange(nowUtc),
            _ => TimeRange.Today(_clock.LocalNow),
        };
    }

    partial void OnSelectedRangeChanged(DashboardRangePreset value)
    {
        OnPropertyChanged(nameof(IsCustomRange));
        _ = RefreshAsync(CancellationToken.None);
    }

    partial void OnSearchTextChanged(string value) => ApplyProjection();

    [RelayCommand]
    public Task SelectPresetAsync(string preset)
    {
        SelectedRange = preset switch
        {
            "Today" => DashboardRangePreset.Today,
            "7d" => DashboardRangePreset.Last7Days,
            "30d" => DashboardRangePreset.Last30Days,
            "Custom" => DashboardRangePreset.Custom,
            _ => DashboardRangePreset.Today,
        };
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var range = GetEffectiveRange();
        _lastSnapshot = await _queryService.QueryAsync(range, cancellationToken);
        var now = _clock.LocalNow;
        var timeStr = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (UsePersianDigits)
        {
            timeStr = NumberFormatter.ToPersianDigits(timeStr);
        }
        LastUpdatedText = Loc.IsPersian ? $"آخرین بروزرسانی: {timeStr}" : $"Last updated: {timeStr}";
        ApplyProjection();
        if (DetailedApp is not null)
        {
            var updated = Rows.FirstOrDefault(r => r.Raw.AppKey == DetailedApp.Raw.AppKey) ?? DetailedApp;
            DetailedApp = updated;
            BuildDetailedAppMetrics(updated);
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UsePersianDigits = settings.UsePersianDigits;
        IsNetworkAccessAvailable = CheckIsAdministrator() || settings.DetailedNetworkEnabled;
        ApplyProjection();
    }

    public void ApplyProjection()
    {
        if (_lastSnapshot is null)
        {
            IsCollectingInitialData = true;
            return;
        }

        IsCollectingInitialData = _lastSnapshot.Apps.Count == 0;

        var fa = Loc.IsPersian;
        var digits = UsePersianDigits;

        TotalActiveText = NumberFormatter.FormatDuration(_lastSnapshot.TotalActive, digits, fa);
        TotalSentText = NumberFormatter.FormatBytes(_lastSnapshot.TotalNetworkSent, digits, fa);
        TotalReceivedText = NumberFormatter.FormatBytes(_lastSnapshot.TotalNetworkReceived, digits, fa);
        TrackedAppsCountText = NumberFormatter.FormatInteger(_lastSnapshot.Apps.Count, digits);

        var query = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _lastSnapshot.Apps
            : _lastSnapshot.Apps
                .Where(a => a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            a.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Rows.Clear();
        foreach (var app in filtered)
        {
            var diskTotal = app.DiskReadBytes + app.DiskWriteBytes;
            var gpuDisplay = app.AverageGpuPercent is null
                ? Loc.NotAvailable
                : app.PeakGpuVramBytes is null
                    ? NumberFormatter.FormatPercent(app.AverageGpuPercent, digits, Loc.NotAvailable)
                    : $"{NumberFormatter.FormatPercent(app.AverageGpuPercent, digits, Loc.NotAvailable)} / {NumberFormatter.FormatBytes(app.PeakGpuVramBytes.Value, digits, fa)}";

            var healthScoreStr = NumberFormatter.FormatNumber(app.HealthScore.TotalScore, "0.0", digits);

            Rows.Add(new AppMetricRowViewModel(
                app,
                app.DisplayName,
                app.ExecutablePath,
                NumberFormatter.FormatDuration(app.OpenDuration, digits, fa),
                NumberFormatter.FormatDuration(app.ActiveDuration, digits, fa),
                NumberFormatter.FormatDuration(app.OnScreenDuration, digits, fa),
                NumberFormatter.FormatPercent(app.AverageCpuPercent, digits, Loc.NotAvailable),
                NumberFormatter.FormatBytes(app.PeakWorkingSetBytes, digits, fa),
                NumberFormatter.FormatBytes(diskTotal, digits, fa),
                gpuDisplay,
                NumberFormatter.FormatBytes(app.NetworkSentBytes, digits, fa),
                NumberFormatter.FormatBytes(app.NetworkReceivedBytes, digits, fa),
                $"{healthScoreStr} ({app.HealthScore.Grade})"));
        }

        TimelineBuckets = _lastSnapshot.Timeline;

        TopAppBars = _lastSnapshot.Apps
            .Take(6)
            .Select(a => new HorizontalBarItem
            {
                Label = a.DisplayName,
                PrimaryValue = Math.Max(1, a.ActiveDuration.TotalSeconds),
                ValueText = NumberFormatter.FormatDuration(a.ActiveDuration, digits, fa),
            })
            .ToArray();

        InterfaceNetworkBars = _lastSnapshot.NetworkByInterface
            .Take(6)
            .Select(n => new HorizontalBarItem
            {
                Label = $"{n.InterfaceName} ({n.InterfaceKind})",
                PrimaryValue = n.BytesSent,
                SecondaryValue = n.BytesReceived,
                ValueText = $"{NumberFormatter.FormatBytes(n.BytesSent, digits, fa)} / {NumberFormatter.FormatBytes(n.BytesReceived, digits, fa)}",
            })
            .ToArray();
    }

    [RelayCommand]
    public void OpenAppDetail(AppMetricRowViewModel? app)
    {
        if (app is null)
        {
            return;
        }

        DetailedApp = app;
        BuildDetailedAppMetrics(app);
        IsAppDetailOpen = true;
    }

    [RelayCommand]
    public void CloseAppDetail()
    {
        IsAppDetailOpen = false;
        DetailedApp = null;
    }

    public void BuildDetailedAppMetrics(AppMetricRowViewModel app)
    {
        var fa = Loc.IsPersian;
        var digits = UsePersianDigits;
        var raw = app.Raw;

        DetailedAppDurationBars =
        [
            new HorizontalBarItem
            {
                Label = Loc.IsPersian ? "زمان فعال (فوکوس)" : "Active (Focused)",
                PrimaryValue = Math.Max(0.5, raw.ActiveDuration.TotalSeconds),
                ValueText = NumberFormatter.FormatDuration(raw.ActiveDuration, digits, fa),
            },
            new HorizontalBarItem
            {
                Label = Loc.IsPersian ? "زمان در صفحه (Visible)" : "On-Screen",
                PrimaryValue = Math.Max(0.5, raw.OnScreenDuration.TotalSeconds),
                ValueText = NumberFormatter.FormatDuration(raw.OnScreenDuration, digits, fa),
            },
            new HorizontalBarItem
            {
                Label = Loc.IsPersian ? "کل زمان باز بودن" : "Total Open",
                PrimaryValue = Math.Max(0.5, raw.OpenDuration.TotalSeconds),
                ValueText = NumberFormatter.FormatDuration(raw.OpenDuration, digits, fa),
            },
        ];

        DetailedAppNetworkBars =
        [
            new HorizontalBarItem
            {
                Label = Loc.IsPersian ? "حجم ارسال (Sent)" : "Data Sent",
                PrimaryValue = Math.Max(1, raw.NetworkSentBytes),
                ValueText = NumberFormatter.FormatBytes(raw.NetworkSentBytes, digits, fa),
            },
            new HorizontalBarItem
            {
                Label = Loc.IsPersian ? "حجم دریافت (Received)" : "Data Received",
                PrimaryValue = Math.Max(1, raw.NetworkReceivedBytes),
                ValueText = NumberFormatter.FormatBytes(raw.NetworkReceivedBytes, digits, fa),
            },
        ];

        if (_lastSnapshot is not null)
        {
            DetailedAppPrivacyRows = _lastSnapshot.PrivacyAudit
                .Where(p => p.AppKey == raw.AppKey)
                .Select(e => new PrivacyAuditRowViewModel(
                    e.DisplayName,
                    $"{e.InterfaceName} ({e.InterfaceKind})",
                    e.RemoteIp,
                    string.IsNullOrWhiteSpace(e.RemoteDomain) ? "—" : e.RemoteDomain,
                    NumberFormatter.FormatBytes(e.BytesSent, digits, fa),
                    NumberFormatter.FormatBytes(e.BytesReceived, digits, fa),
                    e.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    e.IsFirstTimeDestination,
                    e.IsFirstTimeDestination ? Loc.BadgeNewDestination : string.Empty))
                .ToList();
        }
        else
        {
            DetailedAppPrivacyRows = [];
        }
    }

    public static bool CheckIsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private TimeRange BuildCustomRange(DateTimeOffset nowUtc)
    {
        var start = CustomStartDate?.ToUniversalTime() ?? nowUtc.AddDays(-7);
        var end = (CustomEndDate?.ToUniversalTime() ?? nowUtc).AddDays(1);
        if (end <= start)
        {
            end = start.AddDays(1);
        }

        return new TimeRange(start, end);
    }
}
