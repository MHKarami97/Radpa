using System.Collections.ObjectModel;
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
    public partial AppMetricRowViewModel? SelectedApp { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TimelineBucket> TimelineBuckets { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> TopAppBars { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HorizontalBarItem> InterfaceNetworkBars { get; set; } = [];

    [ObservableProperty]
    public partial bool UsePersianDigits { get; set; } = true;

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
        ApplyProjection();
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UsePersianDigits = settings.UsePersianDigits;
        ApplyProjection();
    }

    public void ApplyProjection()
    {
        if (_lastSnapshot is null)
        {
            return;
        }

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
