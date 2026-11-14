using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Application.Reports;
using Radpa.Application.Updates;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Radpa.Infrastructure.Persistence;
using Radpa.Infrastructure.Security;
using Radpa.UI.Controls;
using Radpa.UI.Localization;
using Radpa.UI.ViewModels;
using Radpa.UI.Views;
using Xunit;

namespace Radpa.UI.Tests;

public sealed class MainWindowUiTests : IDisposable
{
    private readonly string _tempDir;

    public MainWindowUiTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"radpa-ui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task MainWindowRendersAllTabsRtlAndMixedBidiDataGridCleanly()
    {
        var (window, vm) = await CreateSeededMainWindowAsync(TestContext.Current.CancellationToken);

        try
        {
            window.Show();
            await vm.RefreshAllAsync();
            window.UpdateLayout();

            window.FlowDirection.Should().Be(FlowDirection.RightToLeft);
            vm.Dashboard.Rows.Should().HaveCount(2);

            var dataGrid = window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault(g => g.Name == "AppsDataGrid");
            dataGrid.Should().NotBeNull();
            dataGrid!.Columns[0].Header.Should().Be("برنامه");

            var barCharts = window.GetVisualDescendants().OfType<HorizontalBarChartControl>().ToArray();
            barCharts.Should().NotBeEmpty();

            // Navigate across all 5 views and ensure clean layout without XAML or binding failures
            for (var tab = 0; tab < 5; tab++)
            {
                vm.NavigateToCommand.Execute(tab.ToString(System.Globalization.CultureInfo.InvariantCulture));
                window.UpdateLayout();
                vm.SelectedTabIndex.Should().Be(tab);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwitchingLanguageAndThemeUpdatesFlowDirectionColumnHeadersAndThemeVariant()
    {
        var (window, vm) = await CreateSeededMainWindowAsync(TestContext.Current.CancellationToken);

        try
        {
            window.Show();
            await vm.RefreshAllAsync();
            window.UpdateLayout();

            var dataGrid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "AppsDataGrid");
            dataGrid.Columns[0].Header.Should().Be("برنامه");
            vm.Dashboard.Rows[0].GpuText.Should().Be("ناموجود");

            vm.Settings.NotificationsEnabled.Should().BeTrue();
            vm.Settings.NotificationsEnabled = false;
            vm.Settings.IsPersianLanguage = false;
            vm.Settings.IsDarkTheme = false;
            vm.Settings.UsePersianDigits = false;
            await vm.Settings.SaveSettingsCommand.ExecuteAsync(null);
            window.UpdateLayout();
            vm.Settings.NotificationsEnabled.Should().BeFalse();

            window.FlowDirection.Should().Be(FlowDirection.LeftToRight);
            vm.Loc.IsPersian.Should().BeFalse();
            dataGrid.Columns[0].Header.Should().Be("Application");
            vm.Dashboard.Rows[0].GpuText.Should().Be("N/A");
            global::Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void VazirmatnFontResourceAndChartsRenderInRtlAndLtrWindows()
    {
        global::Avalonia.Application.Current!.TryFindResource("RadpaFont", out var fontRes).Should().BeTrue();
        fontRes.Should().BeOfType<FontFamily>().Which.Name.Should().Contain("Vazirmatn UI");

        var barChart = new HorizontalBarChartControl
        {
            Width = 360,
            Height = 160,
            FlowDirection = FlowDirection.RightToLeft,
            Items =
            [
                new HorizontalBarItem { Label = "گوگل کروم (Chrome)", PrimaryValue = 65, SecondaryValue = 15, ValueText = "۸۰ مگابایت" },
                new HorizontalBarItem { Label = "Visual Studio Code (کد)", PrimaryValue = 40, ValueText = "۴۰ مگابایت" },
            ],
        };

        var timeline = new TimelineChartControl
        {
            Width = 360,
            Height = 150,
            UsePersianDigits = true,
            Buckets =
            [
                new TimelineBucket(DateTimeOffset.UtcNow.AddHours(-2), 1200, 1024, 4096),
                new TimelineBucket(DateTimeOffset.UtcNow.AddHours(-1), 2400, 2048, 8192),
            ],
        };

        var w = new Window
        {
            Width = 420,
            Height = 400,
            FlowDirection = FlowDirection.RightToLeft,
            Content = new StackPanel { Children = { barChart, timeline } },
        };

        w.Show();
        w.UpdateLayout();
        barChart.Bounds.Width.Should().BeGreaterThan(0);
        timeline.Bounds.Height.Should().Be(150);
        w.Close();
    }

    private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> CreateSeededMainWindowAsync(CancellationToken ct)
    {
        var dbPath = Path.Combine(_tempDir, "ui-test.db");
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var repo = new SqliteUsageRepository(dbPath);
        await repo.InitializeAsync(ct);

        var nowUtc = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var chromeKey = new AppKey("group:Google Chrome");
        var radpaTestKey = new AppKey("exe:persian-tool");

        await repo.SaveBatchAsync(new TelemetryFlushBatch(
            [
                new AppIdentity(chromeKey, "مرورگر Chrome 130", @"C:\Chrome\chrome.exe"),
                new AppIdentity(radpaTestKey, "ابزار فارسی تست ۱۲", @"D:\Tools\app.exe"),
            ],
            [
                new UsageSession(0, chromeKey, nowUtc.AddMinutes(-20), nowUtc.AddMinutes(-5), 900, 600, 750, "Research"),
                new UsageSession(0, radpaTestKey, nowUtc.AddMinutes(-10), nowUtc.AddMinutes(-2), 480, 300, 400),
            ],
            [
                new ResourceSample(chromeKey, nowUtc.AddMinutes(-10), 12.4, 256_000_000, 180_000_000, 1024, 2048, null, null),
                new ResourceSample(radpaTestKey, nowUtc.AddMinutes(-5), 4.2, 64_000_000, 48_000_000, 512, 512, 8.5, 64_000_000),
            ],
            [
                new NetworkSample(chromeKey, nowUtc.AddMinutes(-6), "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", "one.one.one.one", 10_000, 50_000),
            ]),
            ct);

        var store = new JsonSettingsStore(settingsPath);
        var settings = new AppSettings
        {
            Language = UiLanguage.Persian,
            Theme = UiThemeMode.Dark,
            UsePersianDigits = true,
        };
        await store.SaveAsync(settings, ct);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(nowUtc);
        clock.LocalNow.Returns(nowUtc);

        var loc = new Localizer { Language = settings.Language };
        var encryption = new AesGcmEncryptionService(new DpapiKeyProtector());
        var query = new DashboardQueryService(repo, encryption);
        var dashboardVm = new DashboardViewModel(query, clock, loc);
        var privacyVm = new PrivacyAuditorViewModel(loc);
        var insightsVm = new InsightsViewModel(query, clock, loc, settings, async s => await store.SaveAsync(s, ct));
        var focusManager = new FocusSessionManager(clock, Substitute.For<INotificationService>());
        var focusVm = new FocusAndProjectsViewModel(focusManager, loc, settings, async s => await store.SaveAsync(s, ct));

        var migration = new EncryptionMigrationService(repo, encryption, store);
        var startup = Substitute.For<IStartupRegistrationService>();
        var netCollector = Substitute.For<INetworkCollector>();
        var selfMetrics = Substitute.For<ISelfMetricsProvider>();
        selfMetrics.Capture().Returns(new SelfMetricsSnapshot(0.2, 24_000_000, 18_000_000));

        MainWindowViewModel? mainVm = null;
        var settingsVm = new SettingsViewModel(
            settings,
            loc,
            store,
            repo,
            netCollector,
            startup,
            migration,
            selfMetrics,
            async next =>
            {
                if (mainVm is not null)
                {
                    await mainVm.OnSettingsSavedAsync(next);
                }
            },
            () => Task.CompletedTask,
            () => dashboardVm.LatestSnapshot);

        var updateCoord = new UpdateCoordinator(Substitute.For<IUpdateChecker>(), clock, store, Substitute.For<INotificationService>());
        mainVm = new MainWindowViewModel(settings, loc, dashboardVm, privacyVm, insightsVm, focusVm, settingsVm, updateCoord, store, new SemanticVersion(1, 0, 0));

        var window = new MainWindow(mainVm, enableAutoRefresh: false);
        return (window, mainVm);
    }
}
