using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public sealed record ProjectSummaryRowViewModel(string ProjectName, string ActiveText, string OnScreenText);

public sealed partial class FocusAndProjectsViewModel : ObservableObject
{
    private readonly FocusSessionManager _focusManager;
    private readonly Func<AppSettings, Task> _saveSettingsAsync;
    private AppSettings _settings;

    public FocusAndProjectsViewModel(
        FocusSessionManager focusManager,
        Localizer loc,
        AppSettings initialSettings,
        Func<AppSettings, Task> saveSettingsAsync)
    {
        _focusManager = focusManager;
        Loc = loc;
        _settings = initialSettings;
        _saveSettingsAsync = saveSettingsAsync;
        _focusManager.SessionCompleted += (_, session) => LastCompletedReport = FormatSummary(session);
        ReloadProjectRules();
    }

    public Localizer Loc { get; }

    [ObservableProperty]
    public partial int FocusMinutes { get; set; } = 25;

    [ObservableProperty]
    public partial string DistractingAppsText { get; set; } = "discord, telegram, steam, youtube";

    [ObservableProperty]
    public partial bool IsFocusRunning { get; set; }

    [ObservableProperty]
    public partial string LiveFocusStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastCompletedReport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewProjectName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewProjectPattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ProjectMatchKind NewProjectMatchKind { get; set; } = ProjectMatchKind.ProcessName;

    public ObservableCollection<ProjectTagRule> ProjectRules { get; } = [];
    public ObservableCollection<ProjectSummaryRowViewModel> ProjectSummaries { get; } = [];

    [RelayCommand]
    public void StartFocus()
    {
        var mins = Math.Clamp(FocusMinutes, 1, 240);
        var apps = DistractingAppsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _focusManager.Start(TimeSpan.FromMinutes(mins), apps);
        IsFocusRunning = true;
        RefreshLiveStatus();
    }

    [RelayCommand]
    public void StopFocus()
    {
        var summary = _focusManager.Stop();
        IsFocusRunning = false;
        if (summary is not null)
        {
            LastCompletedReport = FormatSummary(summary);
        }

        RefreshLiveStatus();
    }

    public void RefreshLiveStatus()
    {
        IsFocusRunning = _focusManager.IsActive;
        var snap = _focusManager.GetCurrentSnapshot();
        LiveFocusStatusText = snap is null
            ? (Loc.IsPersian ? "جلسه فعالی در جریان نیست." : "No active focus session.")
            : FormatSummary(snap);
    }

    public void UpdateFromSnapshot(DashboardSnapshot snapshot, AppSettings settings)
    {
        _settings = settings;
        var fa = Loc.IsPersian;
        var digits = settings.UsePersianDigits;
        ProjectSummaries.Clear();
        foreach (var p in snapshot.Projects)
        {
            ProjectSummaries.Add(new ProjectSummaryRowViewModel(
                p.ProjectName,
                NumberFormatter.FormatDuration(p.ActiveDuration, digits, fa),
                NumberFormatter.FormatDuration(p.OnScreenDuration, digits, fa)));
        }

        ReloadProjectRules();
        RefreshLiveStatus();
    }

    [RelayCommand]
    public async Task AddProjectRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName) || string.IsNullOrWhiteSpace(NewProjectPattern))
        {
            return;
        }

        var list = _settings.ProjectRules.ToList();
        list.Add(new ProjectTagRule(
            Guid.NewGuid().ToString("N")[..8],
            NewProjectName.Trim(),
            NewProjectMatchKind,
            NewProjectPattern.Trim(),
            true));

        _settings = _settings with { ProjectRules = list };
        await _saveSettingsAsync(_settings);
        NewProjectName = string.Empty;
        NewProjectPattern = string.Empty;
        ReloadProjectRules();
    }

    [RelayCommand]
    public async Task RemoveProjectRuleAsync(ProjectTagRule rule)
    {
        var list = _settings.ProjectRules.Where(r => r.Id != rule.Id).ToList();
        _settings = _settings with { ProjectRules = list };
        await _saveSettingsAsync(_settings);
        ReloadProjectRules();
    }

    private void ReloadProjectRules()
    {
        ProjectRules.Clear();
        foreach (var r in _settings.ProjectRules)
        {
            ProjectRules.Add(r);
        }
    }

    private string FormatSummary(FocusSession session)
    {
        var fa = Loc.IsPersian;
        var digits = _settings.UsePersianDigits;
        var focused = NumberFormatter.FormatDuration(TimeSpan.FromSeconds(session.FocusedSeconds), digits, fa);
        var distracted = NumberFormatter.FormatDuration(TimeSpan.FromSeconds(session.DistractedSeconds), digits, fa);
        var topDistraction = session.DistractionBreakdownSeconds
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        var suffix = string.IsNullOrEmpty(topDistraction.Key)
            ? string.Empty
            : fa
                ? $" | بیشترین حواس‌پرتی: {topDistraction.Key}"
                : $" | Top distraction: {topDistraction.Key}";

        return fa
            ? $"تمرکز: {focused} | حواس‌پرتی: {distracted}{suffix}"
            : $"Focused: {focused} | Distracted: {distracted}{suffix}";
    }
}
