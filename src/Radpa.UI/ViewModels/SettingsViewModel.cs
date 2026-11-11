using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Infrastructure.Persistence;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IUsageRepository _repository;
    private readonly INetworkCollector _networkCollector;
    private readonly IStartupRegistrationService _startupService;
    private readonly EncryptionMigrationService _encryptionMigration;
    private readonly ISelfMetricsProvider _selfMetrics;
    private readonly Func<AppSettings, Task> _onSettingsApplied;
    private readonly Func<Task> _onCheckUpdatesNow;
    private readonly Func<DashboardSnapshot?> _getLatestSnapshot;
    private AppSettings _current;

    public SettingsViewModel(
        AppSettings initial,
        Localizer loc,
        ISettingsStore settingsStore,
        IUsageRepository repository,
        INetworkCollector networkCollector,
        IStartupRegistrationService startupService,
        EncryptionMigrationService encryptionMigration,
        ISelfMetricsProvider selfMetrics,
        Func<AppSettings, Task> onSettingsApplied,
        Func<Task> onCheckUpdatesNow,
        Func<DashboardSnapshot?> getLatestSnapshot)
    {
        _current = initial;
        Loc = loc;
        _settingsStore = settingsStore;
        _repository = repository;
        _networkCollector = networkCollector;
        _startupService = startupService;
        _encryptionMigration = encryptionMigration;
        _selfMetrics = selfMetrics;
        _onSettingsApplied = onSettingsApplied;
        _onCheckUpdatesNow = onCheckUpdatesNow;
        _getLatestSnapshot = getLatestSnapshot;

        LoadFromSettings(initial);
    }

    public Localizer Loc { get; }

    [ObservableProperty]
    public partial bool IsPersianLanguage { get; set; }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; }

    [ObservableProperty]
    public partial bool UsePersianDigits { get; set; }

    [ObservableProperty]
    public partial int IdleThresholdSeconds { get; set; }

    [ObservableProperty]
    public partial int SamplingIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial int RetentionDays { get; set; }

    [ObservableProperty]
    public partial bool PortableMode { get; set; }

    [ObservableProperty]
    public partial bool RecordWindowTitles { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool DetailedNetworkEnabled { get; set; }

    [ObservableProperty]
    public partial bool EncryptionEnabled { get; set; }

    [ObservableProperty]
    public partial bool UpdateCheckEnabled { get; set; }

    [ObservableProperty]
    public partial string UpdateRepository { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double MigrationProgress { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DatabaseSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelfMonitorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewGroupPattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewGroupTarget { get; set; } = string.Empty;

    public ObservableCollection<GroupingRule> GroupingRules { get; } = [];

    public void RefreshSelfMetrics()
    {
        var snap = _selfMetrics.Capture();
        var fa = Loc.IsPersian;
        var cpu = NumberFormatter.FormatPercent(snap.CpuPercent, UsePersianDigits);
        var ram = NumberFormatter.FormatBytes(snap.WorkingSetBytes, UsePersianDigits, fa);
        SelfMonitorText = fa
            ? $"CPU ردپا: {cpu} | رم مصرفی: {ram}"
            : $"Radpa CPU: {cpu} | RAM: {ram}";
    }

    public async Task RefreshStorageSizeAsync(CancellationToken ct)
    {
        var bytes = await _repository.GetDatabaseSizeBytesAsync(ct);
        DatabaseSizeText = NumberFormatter.FormatBytes(bytes, UsePersianDigits, Loc.IsPersian);
        RefreshSelfMetrics();
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var next = _current with
            {
                Language = IsPersianLanguage ? UiLanguage.Persian : UiLanguage.English,
                Theme = IsDarkTheme ? UiThemeMode.Dark : UiThemeMode.Light,
                UsePersianDigits = UsePersianDigits,
                IdleThresholdSeconds = Math.Clamp(IdleThresholdSeconds, 15, 3600),
                SamplingIntervalSeconds = Math.Clamp(SamplingIntervalSeconds, 2, 60),
                RetentionDays = Math.Clamp(RetentionDays, 1, 3650),
                PortableMode = PortableMode,
                RecordWindowTitles = RecordWindowTitles,
                StartWithWindows = StartWithWindows,
                MinimizeToTray = MinimizeToTray,
                GroupingRules = GroupingRules.ToArray(),
                UpdateCheck = _current.UpdateCheck with
                {
                    Enabled = UpdateCheckEnabled,
                    Repository = string.IsNullOrWhiteSpace(UpdateRepository) ? _current.UpdateCheck.Repository : UpdateRepository.Trim(),
                },
            };

            StoragePathResolver.SetPortableMarker(next.PortableMode);

            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Radpa.exe");
            _startupService.SetEnabled(next.StartWithWindows, exePath);

            if (next.EncryptionEnabled != EncryptionEnabled)
            {
                var progress = new Progress<double>(p => MigrationProgress = p);
                next = await _encryptionMigration.MigrateAsync(next, EncryptionEnabled, progress, CancellationToken.None);
            }

            if (DetailedNetworkEnabled != _current.DetailedNetworkEnabled)
            {
                if (DetailedNetworkEnabled)
                {
                    var ok = await _networkCollector.EnableElevatedAsync(CancellationToken.None);
                    if (!ok)
                    {
                        DetailedNetworkEnabled = false;
                        next = next with { DetailedNetworkEnabled = false };
                        StatusMessage = Loc.IsPersian
                            ? "دسترسی مدیر (UAC) اعطا نشد. سایر قابلیت‌های ردپا بدون نیاز به مدیر فعال هستند."
                            : "Administrator elevation was declined. All non-network features continue normally.";
                    }
                    else
                    {
                        next = next with { DetailedNetworkEnabled = true };
                    }
                }
                else
                {
                    await _networkCollector.DisableAsync(CancellationToken.None);
                    next = next with { DetailedNetworkEnabled = false };
                }
            }

            _current = next;
            await _settingsStore.SaveAsync(next, CancellationToken.None);
            await _onSettingsApplied(next);

            if (string.IsNullOrEmpty(StatusMessage))
            {
                StatusMessage = Loc.IsPersian ? "تنظیمات با موفقیت ذخیره شد." : "Settings saved.";
            }
        }
        catch (Exception ex)
        {
            EncryptionEnabled = _current.EncryptionEnabled;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void AddGroupingRule()
    {
        if (string.IsNullOrWhiteSpace(NewGroupPattern) || string.IsNullOrWhiteSpace(NewGroupTarget))
        {
            return;
        }

        GroupingRules.Add(new GroupingRule(
            Guid.NewGuid().ToString("N")[..8],
            NewGroupTarget.Trim(),
            GroupingMatchKind.ExecutableName,
            NewGroupPattern.Trim(),
            NewGroupTarget.Trim(),
            true,
            50));

        NewGroupPattern = string.Empty;
        NewGroupTarget = string.Empty;
    }

    [RelayCommand]
    public void RemoveGroupingRule(GroupingRule rule) => GroupingRules.Remove(rule);

    [RelayCommand]
    public Task CheckForUpdatesNowAsync() => _onCheckUpdatesNow();

    [RelayCommand]
    public async Task ExportCsvAsync() => await WriteExportFileAsync("csv", snap => ReportExporter.ToCsv(snap));

    [RelayCommand]
    public async Task ExportJsonAsync() => await WriteExportFileAsync("json", snap => ReportExporter.ToJson(snap));

    [RelayCommand]
    public async Task ExportHtmlAsync() =>
        await WriteExportFileAsync("html", snap => ReportExporter.ToHtmlReport(snap, Loc.IsPersian, UsePersianDigits, Loc.AppTitle));

    [RelayCommand]
    public async Task ApplyRetentionAndVacuumAsync()
    {
        IsBusy = true;
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, RetentionDays));
            var deleted = await _repository.ApplyRetentionAsync(cutoff, CancellationToken.None);
            await _repository.VacuumAsync(CancellationToken.None);
            await RefreshStorageSizeAsync(CancellationToken.None);
            StatusMessage = Loc.IsPersian
                ? $"پاک‌سازی انجام شد ({NumberFormatter.FormatInteger(deleted, UsePersianDigits)} ردیف قدیمی حذف و پایگاه داده بهینه‌سازی شد)."
                : $"Compacted database ({NumberFormatter.FormatInteger(deleted, UsePersianDigits)} expired rows removed).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAllDataAsync()
    {
        IsBusy = true;
        try
        {
            await _repository.DeleteAllDataAsync(CancellationToken.None);
            await _repository.VacuumAsync(CancellationToken.None);
            await RefreshStorageSizeAsync(CancellationToken.None);
            StatusMessage = Loc.IsPersian ? "تمام داده‌های محلی حذف شدند." : "All local data deleted.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WriteExportFileAsync(string ext, Func<DashboardSnapshot, string> formatter)
    {
        var snap = _getLatestSnapshot();
        if (snap is null)
        {
            return;
        }

        var dir = Path.Combine(StoragePathResolver.ResolveDataDirectory(PortableMode), "Exports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"radpa-report-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{ext}");
        await File.WriteAllTextAsync(path, formatter(snap));
        StatusMessage = Loc.IsPersian ? $"خروجی ذخیره شد: {path}" : $"Exported: {path}";
    }

    private void LoadFromSettings(AppSettings s)
    {
        IsPersianLanguage = s.Language == UiLanguage.Persian;
        IsDarkTheme = s.Theme == UiThemeMode.Dark;
        UsePersianDigits = s.UsePersianDigits;
        IdleThresholdSeconds = s.IdleThresholdSeconds;
        SamplingIntervalSeconds = s.SamplingIntervalSeconds;
        RetentionDays = s.RetentionDays;
        PortableMode = s.PortableMode;
        RecordWindowTitles = s.RecordWindowTitles;
        StartWithWindows = s.StartWithWindows;
        MinimizeToTray = s.MinimizeToTray;
        DetailedNetworkEnabled = s.DetailedNetworkEnabled;
        EncryptionEnabled = s.EncryptionEnabled;
        UpdateCheckEnabled = s.UpdateCheck.Enabled;
        UpdateRepository = s.UpdateCheck.Repository;

        GroupingRules.Clear();
        foreach (var g in s.GroupingRules)
        {
            GroupingRules.Add(g);
        }
    }
}
