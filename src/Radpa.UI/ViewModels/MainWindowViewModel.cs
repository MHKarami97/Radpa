using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Application.Updates;
using Radpa.Domain.Services;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly ISettingsStore _settingsStore;
    private readonly SemanticVersion _appVersion;
    private readonly Action<AppSettings>? _applyCollectorSettings;
    private AppSettings _settings;

    public MainWindowViewModel(
        AppSettings initialSettings,
        Localizer loc,
        DashboardViewModel dashboard,
        PrivacyAuditorViewModel privacy,
        InsightsViewModel insights,
        FocusAndProjectsViewModel focus,
        SettingsViewModel settingsVm,
        UpdateCoordinator updateCoordinator,
        ISettingsStore settingsStore,
        SemanticVersion appVersion,
        Action<AppSettings>? applyCollectorSettings = null)
    {
        _settings = initialSettings;
        Loc = loc;
        Dashboard = dashboard;
        Privacy = privacy;
        Insights = insights;
        Focus = focus;
        Settings = settingsVm;
        _updateCoordinator = updateCoordinator;
        _settingsStore = settingsStore;
        _appVersion = appVersion;
        _applyCollectorSettings = applyCollectorSettings;

        ApplyVisualThemeAndLanguage(initialSettings);
    }

    public Localizer Loc { get; }
    public DashboardViewModel Dashboard { get; }
    public PrivacyAuditorViewModel Privacy { get; }
    public InsightsViewModel Insights { get; }
    public FocusAndProjectsViewModel Focus { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial FlowDirection WindowFlowDirection { get; set; } = FlowDirection.RightToLeft;

    [ObservableProperty]
    public partial bool HasUpdateBanner { get; set; }

    [ObservableProperty]
    public partial string UpdateBannerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? UpdateReleaseUrl { get; set; }

    [ObservableProperty]
    public partial string? LatestVersionTag { get; set; }

    [RelayCommand]
    public void NavigateTo(string indexStr)
    {
        if (int.TryParse(indexStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        await Dashboard.RefreshAsync(CancellationToken.None);
        if (Dashboard.LatestSnapshot is { } snap)
        {
            Privacy.UpdateFromSnapshot(snap, _settings.UsePersianDigits);
            Insights.UpdateFromSnapshot(snap, _settings);
            Focus.UpdateFromSnapshot(snap, _settings);
        }

        await Settings.RefreshStorageSizeAsync(CancellationToken.None);
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync(bool force)
    {
        var (result, updated) = await _updateCoordinator.CheckIfDueAsync(_settings, _appVersion, force, CancellationToken.None);
        _settings = updated;

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.LatestVersion is not null)
        {
            var versionStr = result.LatestVersion.Value.ToString();
            if (!force && string.Equals(_settings.SkippedUpdateVersion, versionStr, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LatestVersionTag = versionStr;
            UpdateReleaseUrl = result.ReleaseUrl;
            UpdateBannerText = Loc.IsPersian
                ? $"نسخه جدید ردپا (v{versionStr}) منتشر شده است."
                : $"A new Radpa release (v{versionStr}) is available.";
            HasUpdateBanner = true;
        }
    }

    [RelayCommand]
    public void OpenUpdateReleasePage()
    {
        if (!string.IsNullOrWhiteSpace(UpdateReleaseUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo(UpdateReleaseUrl) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }

    [RelayCommand]
    public async Task SkipUpdateVersionAsync()
    {
        if (!string.IsNullOrWhiteSpace(LatestVersionTag))
        {
            _settings = _settings with { SkippedUpdateVersion = LatestVersionTag };
            await _settingsStore.SaveAsync(_settings, CancellationToken.None);
        }

        HasUpdateBanner = false;
    }

    public async Task OnSettingsSavedAsync(AppSettings next)
    {
        _settings = next;
        ApplyVisualThemeAndLanguage(next);
        _applyCollectorSettings?.Invoke(next);
        Dashboard.ApplySettings(next);
        await RefreshAllAsync();
    }

    public void ApplyVisualThemeAndLanguage(AppSettings settings)
    {
        Loc.Language = settings.Language;
        WindowFlowDirection = Loc.FlowDirection;

        if (global::Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = settings.Theme == UiThemeMode.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
    }
}
