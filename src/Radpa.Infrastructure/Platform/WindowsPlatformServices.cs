using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Radpa.Application.Abstractions;

namespace Radpa.Infrastructure.Platform;

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Radpa";

    public bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\" --collector --minimized", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

public sealed class WindowsElevationService : IElevationService
{
    private const int ErrorCancelled = 1223;
    private readonly IAppLogger _logger;
    private Process? _helperProcess;

    public WindowsElevationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public static string HelperExecutablePath => ResolveHelperPath() ?? Path.Combine(AppContext.BaseDirectory, "Radpa.NetCollector.exe");

    public bool IsHelperAvailable => ResolveHelperPath() is not null;

    private static string? ResolveHelperPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var exact = Path.Combine(baseDir, "Radpa.NetCollector.exe");
        if (File.Exists(exact))
        {
            return exact;
        }

        var ridSuffix = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var archSpecific = Path.Combine(baseDir, $"Radpa.NetCollector-{ridSuffix}.exe");
        if (File.Exists(archSpecific))
        {
            return archSpecific;
        }

        // Development / debug fallback: search parent directories for sibling Radpa.NetCollector build outputs
        var candidateDir = baseDir;
        for (var i = 0; i < 8 && candidateDir is not null; i++)
        {
            var netCollectorBin = Path.Combine(candidateDir, "src", "Radpa.NetCollector", "bin");
            if (Directory.Exists(netCollectorBin))
            {
                const string tfm = "net10.0-windows10.0.17763.0";
                var debugExe = Path.Combine(netCollectorBin, "Debug", tfm, "Radpa.NetCollector.exe");
                if (File.Exists(debugExe))
                {
                    return debugExe;
                }

                var releaseExe = Path.Combine(netCollectorBin, "Release", tfm, "Radpa.NetCollector.exe");
                if (File.Exists(releaseExe))
                {
                    return releaseExe;
                }

                var winX64Exe = Path.Combine(netCollectorBin, "Release", tfm, "win-x64", "Radpa.NetCollector.exe");
                if (File.Exists(winX64Exe))
                {
                    return winX64Exe;
                }
            }

            var parent = Directory.GetParent(candidateDir);
            candidateDir = parent?.FullName;
        }

        return null;
    }

    private static bool IsCurrentProcessAdmin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> StartNetworkHelperElevatedAsync(string pipeName, CancellationToken cancellationToken)
    {
        if (!IsHelperAvailable)
        {
            return Task.FromResult(false);
        }

        StopNetworkHelper();

        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
            var helperExe = HelperExecutablePath;
            var helperDir = Path.GetDirectoryName(helperExe);
            var isAdmin = IsCurrentProcessAdmin();

            var psi = new ProcessStartInfo
            {
                FileName = helperExe,
                Arguments = $"--pipe \"{pipeName}\" --sid \"{sid}\" --parent {Environment.ProcessId}",
                WorkingDirectory = string.IsNullOrWhiteSpace(helperDir) ? AppContext.BaseDirectory : helperDir,
                UseShellExecute = !isAdmin,
                Verb = isAdmin ? string.Empty : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            _helperProcess = Process.Start(psi);
            return Task.FromResult(_helperProcess is not null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            _logger.Info("User declined UAC elevation for Radpa.NetCollector.");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to start Radpa.NetCollector elevated.", ex);
            return Task.FromResult(false);
        }
    }

    public void StopNetworkHelper()
    {
        try
        {
            if (_helperProcess is { HasExited: false })
            {
                _helperProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            _helperProcess?.Dispose();
            _helperProcess = null;
        }
    }
}

public sealed class WindowsNotificationService : INotificationService
{
    private Action<string, string, NotificationSeverity>? _handler;

    public bool Enabled { get; set; } = true;

    public void AttachHandler(Action<string, string, NotificationSeverity> handler) => _handler = handler;

    public void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        if (!Enabled)
        {
            return;
        }

        _handler?.Invoke(title, message, severity);
    }
}

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _wakeEvent;
    private readonly bool _isOwner;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle wakeEvent, bool isOwner)
    {
        _mutex = mutex;
        _wakeEvent = wakeEvent;
        _isOwner = isOwner;
    }

    public bool IsPrimaryInstance => _isOwner;

    public static SingleInstanceGuard Acquire(string channelName)
    {
        var mutex = new Mutex(initiallyOwned: true, $"Local\\{channelName}.Mutex", out var createdNew);
        var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\{channelName}.Wake");
        return new SingleInstanceGuard(mutex, wakeEvent, createdNew);
    }

    public void SignalExistingInstance()
    {
        try
        {
            _wakeEvent.Set();
        }
        catch
        {
        }
    }

    public Task WaitForWakeSignalAsync(Action onWake, CancellationToken ct) =>
        Task.Run(() =>
        {
            WaitHandle[] handles = [_wakeEvent, ct.WaitHandle];
            while (!ct.IsCancellationRequested)
            {
                var idx = WaitHandle.WaitAny(handles);
                if (idx == 0)
                {
                    onWake();
                }
                else
                {
                    break;
                }
            }
        }, ct);

    public void Dispose()
    {
        if (_isOwner)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
            }
        }

        _mutex.Dispose();
        _wakeEvent.Dispose();
    }
}
