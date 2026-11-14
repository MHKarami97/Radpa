using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Tracks Windows process lifetimes using Toolhelp32 discovery + event-driven RegisterWaitForSingleObject exit callbacks.
/// </summary>
public sealed class WindowsProcessMonitor : IProcessMonitor
{
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<int, TrackedProcess> _tracked = new();
    private readonly ConcurrentDictionary<string, string?> _productNameCache = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _discoveryTask;

    public WindowsProcessMonitor(IClock clock, IAppLogger logger)
    {
        _clock = clock;
        _logger = logger;
    }

    public string Name => "WindowsProcessMonitor";

    public event EventHandler<ProcessInfo>? ProcessStarted;
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ScanProcesses(raiseStartedEvents: false);
        _discoveryTask = Task.Run(() => RunDiscoveryLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        foreach (var entry in _tracked.Values)
        {
            entry.Dispose();
        }

        _tracked.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<ProcessInfo> GetRunningProcesses() =>
        _tracked.Values.Select(t => t.Info).ToArray();

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        foreach (var entry in _tracked.Values)
        {
            entry.Dispose();
        }

        _tracked.Clear();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RunDiscoveryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                ScanProcesses(raiseStartedEvents: true);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Process discovery tick error: {ex.Message}");
            }
        }
    }

    private unsafe void ScanProcesses(bool raiseStartedEvents)
    {
        var snapshot = Kernel32Interop.CreateToolhelp32Snapshot(Kernel32Interop.Th32csSnapProcess, 0);
        if (snapshot == -1 || snapshot == 0)
        {
            return;
        }

        try
        {
            var entry = new Kernel32Interop.ProcessEntry32W
            {
                DwSize = (uint)sizeof(Kernel32Interop.ProcessEntry32W),
            };

            if (!Kernel32Interop.Process32FirstW(snapshot, ref entry))
            {
                return;
            }

            do
            {
                var pid = (int)entry.Th32ProcessId;
                if (pid <= 4 || _tracked.ContainsKey(pid))
                {
                    continue;
                }

                TryTrackProcess(pid, raiseStartedEvents);
            }
            while (Kernel32Interop.Process32NextW(snapshot, ref entry));
        }
        finally
        {
            Kernel32Interop.CloseHandle(snapshot);
        }
    }

    private void TryTrackProcess(int pid, bool raiseStartedEvents)
    {
        var handle = Kernel32Interop.OpenProcess(
            Kernel32Interop.ProcessQueryLimitedInformation | Kernel32Interop.Synchronize,
            false,
            (uint)pid);

        if (handle == 0)
        {
            return;
        }

        var exePath = QueryImagePath(handle);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Kernel32Interop.CloseHandle(handle);
            return;
        }

        var pkgName = QueryPackageFamilyName(handle);
        var productName = _productNameCache.GetOrAdd(exePath, static path =>
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return string.IsNullOrWhiteSpace(info.FileDescription)
                    ? info.ProductName
                    : info.FileDescription;
            }
            catch
            {
                return null;
            }
        });

        var info = new ProcessInfo(pid, exePath, pkgName, productName, _clock.UtcNow);
        var tracked = new TrackedProcess(this, pid, handle, info);

        if (!_tracked.TryAdd(pid, tracked))
        {
            tracked.Dispose();
            return;
        }

        tracked.ArmExitWait();
        if (raiseStartedEvents)
        {
            ProcessStarted?.Invoke(this, info);
        }
    }

    internal static unsafe string? QueryImagePath(nint processHandle)
    {
        Span<char> buffer = stackalloc char[512];
        var len = (uint)buffer.Length;
        fixed (char* ptr = buffer)
        {
            if (Kernel32Interop.QueryFullProcessImageNameW(processHandle, 0, ptr, ref len) && len > 0)
            {
                return new string(buffer[..(int)len]);
            }
        }

        return null;
    }

    private static unsafe string? QueryPackageFamilyName(nint processHandle)
    {
        uint len = 0;
        var res = Kernel32Interop.GetPackageFamilyName(processHandle, ref len, null);
        if (res != Kernel32Interop.ErrorInsufficientBuffer || len is 0 or > 256)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[(int)len];
        fixed (char* ptr = buffer)
        {
            res = Kernel32Interop.GetPackageFamilyName(processHandle, ref len, ptr);
            if (res == 0 && len > 1)
            {
                return new string(buffer[..(int)(len - 1)]);
            }
        }

        return null;
    }

    private void NotifyExited(int pid)
    {
        if (_tracked.TryRemove(pid, out var tracked))
        {
            tracked.Dispose();
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs(pid, _clock.UtcNow));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnProcessWaitCallback(nint context, byte timedOut)
    {
        if (context == 0)
        {
            return;
        }

        var gch = GCHandle.FromIntPtr(context);
        if (gch.Target is TrackedProcess tracked)
        {
            tracked.Owner.NotifyExited(tracked.Pid);
        }
    }

    private sealed class TrackedProcess(WindowsProcessMonitor owner, int pid, nint handle, ProcessInfo info) : IDisposable
    {
        private nint _waitHandle;
        private GCHandle _selfHandle;
        private int _disposed;

        public WindowsProcessMonitor Owner { get; } = owner;
        public int Pid { get; } = pid;
        public ProcessInfo Info { get; } = info;

        public unsafe void ArmExitWait()
        {
            _selfHandle = GCHandle.Alloc(this);
            if (!Kernel32Interop.RegisterWaitForSingleObject(
                    out _waitHandle,
                    handle,
                    &OnProcessWaitCallback,
                    GCHandle.ToIntPtr(_selfHandle),
                    Kernel32Interop.Infinite,
                    Kernel32Interop.WtExecuteOnlyOnce | Kernel32Interop.WtExecuteInWaitThread))
            {
                _selfHandle.Free();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_waitHandle != 0)
            {
                Kernel32Interop.UnregisterWaitEx(_waitHandle, 0);
                _waitHandle = 0;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            if (handle != 0)
            {
                Kernel32Interop.CloseHandle(handle);
            }
        }
    }
}
