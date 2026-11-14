using System.Reflection;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Application.Updates;
using Radpa.Domain.Services;
using Radpa.Infrastructure.Collectors;
using Radpa.Infrastructure.Logging;
using Radpa.Infrastructure.Persistence;
using Radpa.Infrastructure.Platform;
using Radpa.Infrastructure.Security;
using Radpa.Infrastructure.Updates;
using Radpa.UI.Localization;
using Radpa.UI.ViewModels;

namespace Radpa.UI;

/// <summary>
/// Manual, reflection-free, AOT-safe composition root for both --collector and --ui execution roles.
/// </summary>
public sealed class CompositionRoot : IAsyncDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public CompositionRoot(string? customDataDir = null)
    {
        var dataDir = customDataDir ?? StoragePathResolver.ResolveDataDirectory();
        var logDir = Path.Combine(dataDir, "Logs");

        Logger = new RollingFileLogger(logDir);
        Clock = new SystemClock();
        SettingsStore = new JsonSettingsStore(Path.Combine(dataDir, "settings.json"));
        Repository = new SqliteUsageRepository(Path.Combine(dataDir, "radpa.db"));
        KeyProtector = new DpapiKeyProtector();
        Encryption = new AesGcmEncryptionService(KeyProtector);
        Notifications = new WindowsNotificationService();
        StartupService = new WindowsStartupRegistrationService();
        ElevationService = new WindowsElevationService(Logger);
        InterfaceClassifier = new InterfaceClassifier();
        DnsCache = new ReverseDnsCache();
        SelfMetrics = new SelfMetricsProvider();
        FocusManager = new FocusSessionManager(Clock, Notifications);
        QueryService = new DashboardQueryService(Repository, Encryption);
        MigrationService = new EncryptionMigrationService(Repository, Encryption, SettingsStore);
        Localizer = new Localizer();
    }

    public RollingFileLogger Logger { get; }
    public SystemClock Clock { get; }
    public JsonSettingsStore SettingsStore { get; }
    public SqliteUsageRepository Repository { get; }
    public DpapiKeyProtector KeyProtector { get; }
    public AesGcmEncryptionService Encryption { get; }
    public WindowsNotificationService Notifications { get; }
    public WindowsStartupRegistrationService StartupService { get; }
    public WindowsElevationService ElevationService { get; }
    public InterfaceClassifier InterfaceClassifier { get; }
    public ReverseDnsCache DnsCache { get; }
    public SelfMetricsProvider SelfMetrics { get; }
    public FocusSessionManager FocusManager { get; }
    public DashboardQueryService QueryService { get; }
    public EncryptionMigrationService MigrationService { get; }
    public Localizer Localizer { get; }

    public TrackingCoordinator? Coordinator { get; private set; }
    private WindowsProcessMonitor? _processMonitor;
    private WindowsForegroundMonitor? _foregroundMonitor;
    private WindowsSystemStateMonitor? _systemStateMonitor;
    private WindowsGpuSampler? _gpuSampler;
    private PipeNetworkCollector? _networkCollector;

    public static SemanticVersion GetAppVersion()
    {
        var infoVer = typeof(CompositionRoot).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (SemanticVersion.TryParse(infoVer, out var parsed))
        {
            return parsed;
        }

        var v = typeof(CompositionRoot).Assembly.GetName().Version;
        return v is null ? new SemanticVersion(1, 0, 0) : new SemanticVersion(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public async Task<AppSettings> InitializeStorageAsync(CancellationToken ct)
    {
        var settings = await SettingsStore.LoadAsync(ct);
        if (settings.EncryptionEnabled && !string.IsNullOrWhiteSpace(settings.ProtectedEncryptionKeyBase64))
        {
            var blob = Convert.FromBase64String(settings.ProtectedEncryptionKeyBase64);
            Encryption.Configure(true, blob);
        }

        await Repository.InitializeAsync(ct);
        return settings;
    }

    public TrackingCoordinator BuildCollectorEngine(AppSettings settings)
    {
        _processMonitor = new WindowsProcessMonitor(Clock, Logger);
        _foregroundMonitor = new WindowsForegroundMonitor(Clock);
        _systemStateMonitor = new WindowsSystemStateMonitor();
        var idleProvider = new WindowsIdleTimeProvider();
        var resourceSampler = new WindowsResourceSampler();
        _gpuSampler = new WindowsGpuSampler(Logger);
        _networkCollector = new PipeNetworkCollector(ElevationService, InterfaceClassifier, Logger);

        var runtimeSettings = settings with { FlushIntervalSeconds = Math.Clamp(settings.FlushIntervalSeconds, 3, 5) };
        Coordinator = new TrackingCoordinator(
            Clock,
            _processMonitor,
            _foregroundMonitor,
            idleProvider,
            _systemStateMonitor,
            resourceSampler,
            _gpuSampler,
            _networkCollector,
            Repository,
            Encryption,
            DnsCache,
            Notifications,
            Logger,
            FocusManager,
            runtimeSettings);

        return Coordinator;
    }

    public MainWindowViewModel BuildMainWindowViewModel(AppSettings settings, Action<AppSettings>? onCollectorSettings = null)
    {
        _networkCollector ??= new PipeNetworkCollector(ElevationService, InterfaceClassifier, Logger);
        var updateChecker = new GitHubUpdateChecker(_httpClient, settings.UpdateCheck, Clock);
        var updateCoordinator = new UpdateCoordinator(updateChecker, Clock, SettingsStore, Notifications);

        var dashboardVm = new DashboardViewModel(QueryService, Clock, Localizer);
        dashboardVm.ApplySettings(settings);

        var privacyVm = new PrivacyAuditorViewModel(Localizer);
        var insightsVm = new InsightsViewModel(
            QueryService,
            Clock,
            Localizer,
            settings,
            async s => await SettingsStore.SaveAsync(s, CancellationToken.None));

        if (Coordinator is not null)
        {
            Coordinator.AnomalyDetected += (_, alert) => insightsVm.AddAnomaly(alert);
        }

        var focusVm = new FocusAndProjectsViewModel(
            FocusManager,
            Localizer,
            settings,
            async s => await SettingsStore.SaveAsync(s, CancellationToken.None));

        MainWindowViewModel? mainVm = null;
        var settingsVm = new SettingsViewModel(
            settings,
            Localizer,
            SettingsStore,
            Repository,
            _networkCollector,
            StartupService,
            MigrationService,
            SelfMetrics,
            async next =>
            {
                if (mainVm is not null)
                {
                    await mainVm.OnSettingsSavedAsync(next);
                }
            },
            async () =>
            {
                if (mainVm is not null)
                {
                    await mainVm.CheckForUpdatesAsync(force: true);
                }
            },
            () => dashboardVm.LatestSnapshot);

        mainVm = new MainWindowViewModel(
            settings,
            Localizer,
            dashboardVm,
            privacyVm,
            insightsVm,
            focusVm,
            settingsVm,
            updateCoordinator,
            SettingsStore,
            GetAppVersion(),
            onCollectorSettings);

        return mainVm;
    }

    public async ValueTask DisposeAsync()
    {
        if (Coordinator is not null)
        {
            await Coordinator.DisposeAsync();
        }

        if (_networkCollector is not null)
        {
            await _networkCollector.DisposeAsync();
        }

        if (Coordinator is null)
        {
            _gpuSampler?.Dispose();
        }
        DnsCache.Dispose();
        _httpClient.Dispose();
        Logger.Dispose();
    }
}
