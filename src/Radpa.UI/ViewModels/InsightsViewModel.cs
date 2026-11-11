using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public sealed record HealthBreakdownRowViewModel(
    string AppName,
    string TotalText,
    string ResourceText,
    string NetworkText,
    string StartupText,
    string Grade);

public sealed record ComparisonRowViewModel(
    string AppName,
    string BeforeActiveText,
    string AfterActiveText,
    string ActiveDeltaText,
    string CpuDeltaText,
    string NetDeltaText);

public sealed partial class InsightsViewModel : ObservableObject
{
    private readonly DashboardQueryService _queryService;
    private readonly IClock _clock;
    private readonly Func<AppSettings, Task> _saveSettingsAsync;
    private AppSettings _settings;

    public InsightsViewModel(
        DashboardQueryService queryService,
        IClock clock,
        Localizer loc,
        AppSettings initialSettings,
        Func<AppSettings, Task> saveSettingsAsync)
    {
        _queryService = queryService;
        _clock = clock;
        Loc = loc;
        _settings = initialSettings;
        _saveSettingsAsync = saveSettingsAsync;
        ReloadRules();
    }

    public Localizer Loc { get; }

    [ObservableProperty]
    public partial string NewRuleName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewRuleAppPattern { get; set; } = "*";

    [ObservableProperty]
    public partial int NewRuleMaxUploadMbPerHour { get; set; } = 500;

    public ObservableCollection<AnomalyAlert> Anomalies { get; } = [];
    public ObservableCollection<HealthBreakdownRowViewModel> HealthRows { get; } = [];
    public ObservableCollection<NetworkAlertRule> Rules { get; } = [];
    public ObservableCollection<ComparisonRowViewModel> ComparisonRows { get; } = [];

    public void AddAnomaly(AnomalyAlert alert)
    {
        Anomalies.Insert(0, alert);
        while (Anomalies.Count > 50)
        {
            Anomalies.RemoveAt(Anomalies.Count - 1);
        }
    }

    public void UpdateFromSnapshot(DashboardSnapshot snapshot, AppSettings settings)
    {
        _settings = settings;
        var digits = settings.UsePersianDigits;
        HealthRows.Clear();
        foreach (var app in snapshot.Apps.Take(30))
        {
            HealthRows.Add(new HealthBreakdownRowViewModel(
                app.DisplayName,
                NumberFormatter.FormatNumber(app.HealthScore.TotalScore, "0.0", digits),
                NumberFormatter.FormatNumber(app.HealthScore.ResourceScore, "0.0", digits),
                NumberFormatter.FormatNumber(app.HealthScore.NetworkScore, "0.0", digits),
                NumberFormatter.FormatNumber(app.HealthScore.StartupImpactScore, "0.0", digits),
                app.HealthScore.Grade));
        }

        ReloadRules();
    }

    [RelayCommand]
    public async Task AddRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRuleName) || NewRuleMaxUploadMbPerHour <= 0)
        {
            return;
        }

        var updated = _settings.AlertRules.ToList();
        updated.Add(new NetworkAlertRule(
            Guid.NewGuid().ToString("N")[..8],
            NewRuleName.Trim(),
            string.IsNullOrWhiteSpace(NewRuleAppPattern) ? "*" : NewRuleAppPattern.Trim(),
            NewRuleMaxUploadMbPerHour,
            true));

        _settings = _settings with { AlertRules = updated };
        await _saveSettingsAsync(_settings);
        NewRuleName = string.Empty;
        ReloadRules();
    }

    [RelayCommand]
    public async Task RemoveRuleAsync(NetworkAlertRule rule)
    {
        var updated = _settings.AlertRules.Where(r => r.Id != rule.Id).ToList();
        _settings = _settings with { AlertRules = updated };
        await _saveSettingsAsync(_settings);
        ReloadRules();
    }

    [RelayCommand]
    public async Task CompareLastWeekVsPreviousAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var after = new TimeRange(now.AddDays(-7), now);
        var before = new TimeRange(now.AddDays(-14), now.AddDays(-7));
        var results = await _queryService.ComparePeriodsAsync(before, after, ct);

        var fa = Loc.IsPersian;
        var digits = _settings.UsePersianDigits;
        ComparisonRows.Clear();
        foreach (var item in results.Take(30))
        {
            var netDeltaStr = (item.NetDeltaBytes >= 0 ? "+" : "-") + NumberFormatter.FormatBytes(Math.Abs(item.NetDeltaBytes), digits, fa);
            var activeDeltaSign = item.ActiveDelta.TotalSeconds >= 0 ? "+" : "-";
            var activeDeltaStr = activeDeltaSign + NumberFormatter.FormatDuration(item.ActiveDelta.Duration(), digits, fa);
            var cpuDeltaStr = (item.CpuDelta >= 0 ? "+" : string.Empty) + item.CpuDelta.ToString("0.0", CultureInfo.InvariantCulture) + "%";

            ComparisonRows.Add(new ComparisonRowViewModel(
                item.DisplayName,
                NumberFormatter.FormatDuration(item.BeforeActive, digits, fa),
                NumberFormatter.FormatDuration(item.AfterActive, digits, fa),
                activeDeltaStr,
                cpuDeltaStr,
                netDeltaStr));
        }
    }

    private void ReloadRules()
    {
        Rules.Clear();
        foreach (var r in _settings.AlertRules)
        {
            Rules.Add(r);
        }
    }
}
