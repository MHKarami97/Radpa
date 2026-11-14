using System.Diagnostics;
using Avalonia;
using Radpa.Application.Options;
using Radpa.Infrastructure.Platform;
using Radpa.UI.Views;

namespace Radpa.UI;

internal static class Program
{
    private const string CollectorMutexName = "Radpa.Collector.SingleInstance";
    private const string UiMutexName = "Radpa.UI.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            return RunSmokeCheck();
        }

        if (args.Contains("--collector", StringComparer.OrdinalIgnoreCase))
        {
            return RunCollectorHostAsync().GetAwaiter().GetResult();
        }

        if (Debugger.IsAttached || args.Contains("--single-process", StringComparer.OrdinalIgnoreCase))
        {
            return RunSingleProcessUi(args);
        }

        // Default UI launch: ensure the lightweight background collector tray host is active,
        // then start the Avalonia UI process (closing the UI window exits only the UI process to free RAM).
        EnsureCollectorRunning();
        return RunUiProcess(args);
    }

    private static int RunSmokeCheck()
    {
        var sw = Stopwatch.StartNew();
        var tempDir = Path.Combine(Path.GetTempPath(), $"radpa_smoke_{Environment.ProcessId}");
        try
        {
            using var root = new CompositionRoot(tempDir).AsSyncDisposable();
            var settings = root.Instance.InitializeStorageAsync(CancellationToken.None).GetAwaiter().GetResult();
            _ = root.Instance.BuildMainWindowViewModel(settings);
            _ = AppBuilder.Configure<App>().UsePlatformDetect();
            sw.Stop();
            Console.WriteLine($"RADPA_SMOKE_OK version={CompositionRoot.GetAppVersion()} elapsed_ms={sw.ElapsedMilliseconds}");
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<int> RunCollectorHostAsync()
    {
        using var guard = SingleInstanceGuard.Acquire(CollectorMutexName);
        if (!guard.IsPrimaryInstance)
        {
            return 0;
        }

        await using var root = new CompositionRoot();
        var settings = await root.InitializeStorageAsync(CancellationToken.None);
        var coordinator = root.BuildCollectorEngine(settings);
        await coordinator.StartAsync(CancellationToken.None);

        using var exitCts = new CancellationTokenSource();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "radpa.ico");
        using var tray = new Win32TrayHost(
            onOpenUi: SpawnUiProcess,
            onExit: () => exitCts.Cancel(),
            tooltip: "ردپا | Radpa",
            iconPath: File.Exists(iconPath) ? iconPath : null);

        root.Notifications.AttachHandler((title, msg, sev) => tray.ShowBalloon(title, msg, sev));
        tray.Start();

        var wakeTask = guard.WaitForWakeSignalAsync(SpawnUiProcess, exitCts.Token);

        // Periodic retention + update check loop in background collector
        _ = RunMaintenanceLoopAsync(root, settings, exitCts.Token);

        try
        {
            await Task.Delay(Timeout.Infinite, exitCts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        tray.Stop();
        await coordinator.StopAsync();
        try
        {
            await wakeTask;
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    private static async Task RunMaintenanceLoopAsync(CompositionRoot root, AppSettings settings, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        var lastRetentionUtc = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var latestSettings = await root.SettingsStore.LoadAsync(ct);
                root.Coordinator?.UpdateSettings(latestSettings);

                var now = DateTimeOffset.UtcNow;
                if (now - lastRetentionUtc >= TimeSpan.FromHours(1))
                {
                    lastRetentionUtc = now;
                    var cutoff = now.AddDays(-Math.Max(1, latestSettings.RetentionDays));
                    await root.Repository.ApplyRetentionAsync(cutoff, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                root.Logger.Warning($"Background maintenance error: {ex.Message}");
            }
        }
    }

    private static int RunUiProcess(string[] args)
    {
        using var uiGuard = SingleInstanceGuard.Acquire(UiMutexName);
        if (!uiGuard.IsPrimaryInstance)
        {
            uiGuard.SignalExistingInstance();
            return 0;
        }

        using var root = new CompositionRoot().AsSyncDisposable();
        var settings = root.Instance.InitializeStorageAsync(CancellationToken.None).GetAwaiter().GetResult();
        var vm = root.Instance.BuildMainWindowViewModel(settings);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .AfterSetup(builder =>
            {
                if (builder.Instance is App app)
                {
                    app.WindowFactory = () =>
                    {
                        var win = new MainWindow(vm);
                        _ = vm.CheckForUpdatesAsync(force: false);
                        return win;
                    };
                }
            })
            .StartWithClassicDesktopLifetime(args);
    }

    private static int RunSingleProcessUi(string[] args)
    {
        using var root = new CompositionRoot().AsSyncDisposable();
        var settings = root.Instance.InitializeStorageAsync(CancellationToken.None).GetAwaiter().GetResult();
        var coordinator = root.Instance.BuildCollectorEngine(settings);
        coordinator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var vm = root.Instance.BuildMainWindowViewModel(settings, s => coordinator.UpdateSettings(s));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "radpa.ico");
        using var tray = new Win32TrayHost(
            onOpenUi: () =>
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is { } win)
                {
                    win.WindowState = global::Avalonia.Controls.WindowState.Normal;
                    win.Activate();
                }
            },
            onExit: () =>
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            },
            tooltip: "ردپا | Radpa",
            iconPath: File.Exists(iconPath) ? iconPath : null);

        root.Instance.Notifications.AttachHandler((title, msg, sev) => tray.ShowBalloon(title, msg, sev));
        tray.Start();

        var exitCode = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .AfterSetup(builder =>
            {
                if (builder.Instance is App app)
                {
                    app.WindowFactory = () => new MainWindow(vm);
                }
            })
            .StartWithClassicDesktopLifetime(args);

        tray.Stop();
        return exitCode;
    }

    private static void EnsureCollectorRunning()
    {
        using var probe = SingleInstanceGuard.Acquire(CollectorMutexName);
        if (!probe.IsPrimaryInstance)
        {
            return;
        }

        probe.Dispose();
        try
        {
            var psi = CreateChildProcessStartInfo("--collector");
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
        catch
        {
        }
    }

    private static void SpawnUiProcess()
    {
        try
        {
            var psi = CreateChildProcessStartInfo("--ui");
            Process.Start(psi);
        }
        catch
        {
        }
    }

    private static ProcessStartInfo CreateChildProcessStartInfo(string arguments)
    {
        var exe = Environment.ProcessPath ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(exe);
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "Radpa.dll");
            return new ProcessStartInfo(exe, $"exec \"{dllPath}\" {arguments}")
            {
                UseShellExecute = false,
            };
        }

        return new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
        };
    }

    private static SyncDisposableWrapper<T> AsSyncDisposable<T>(this T instance)
        where T : IAsyncDisposable =>
        new(instance);

    private readonly struct SyncDisposableWrapper<T>(T instance) : IDisposable
        where T : IAsyncDisposable
    {
        public T Instance { get; } = instance;

        public void Dispose() => Instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
