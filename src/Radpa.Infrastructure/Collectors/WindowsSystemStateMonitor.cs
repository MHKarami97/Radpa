using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Event-driven screen lock (WTSRegisterSessionNotification) and display power state (RegisterPowerSettingNotification) monitor.
/// </summary>
public sealed unsafe class WindowsSystemStateMonitor : ISystemStateMonitor
{
    private static WindowsSystemStateMonitor? s_instance;

    private Thread? _pumpThread;
    private nint _hwnd;
    private nint _powerReg;
    private bool _wtsRegistered;
    private volatile bool _running;
    private volatile bool _isLocked;
    private volatile bool _isDisplayOff;

    public string Name => "WindowsSystemStateMonitor";
    public bool IsScreenLocked => _isLocked;
    public bool IsDisplayOff => _isDisplayOff;

    public event EventHandler? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_running)
        {
            return Task.CompletedTask;
        }

        s_instance = this;
        _running = true;
        var ready = new ManualResetEventSlim(false);
        _pumpThread = new Thread(() => RunMessageLoop(ready))
        {
            IsBackground = true,
            Name = "Radpa.SystemStateMonitor",
        };
        _pumpThread.Start();
        ready.Wait(TimeSpan.FromSeconds(2), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _running = false;
        if (_hwnd != 0)
        {
            User32Interop.PostMessageW(_hwnd, User32Interop.WmClose, 0, 0);
        }

        _pumpThread?.Join(TimeSpan.FromSeconds(2));
        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private void RunMessageLoop(ManualResetEventSlim ready)
    {
        var className = $"RadpaSysState_{Environment.ProcessId}_{Environment.CurrentManagedThreadId}";
        fixed (char* clsPtr = className)
        {
            var wc = new User32Interop.WndClassExW
            {
                CbSize = (uint)sizeof(User32Interop.WndClassExW),
                LpfnWndProc = &WndProc,
                LpszClassName = clsPtr,
            };
            User32Interop.RegisterClassExW(ref wc);
        }

        _hwnd = User32Interop.CreateWindowExW(0, className, "RadpaState", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (_hwnd != 0)
        {
            _wtsRegistered = User32Interop.WTSRegisterSessionNotification(_hwnd, User32Interop.NotifyForThisSession);
            var guid = User32Interop.GuidConsoleDisplayState;
            _powerReg = User32Interop.RegisterPowerSettingNotification(_hwnd, ref guid, User32Interop.DeviceNotifyWindowHandle);
        }

        ready.Set();

        while (_running && User32Interop.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            User32Interop.TranslateMessage(ref msg);
            User32Interop.DispatchMessageW(ref msg);
        }

        if (_powerReg != 0)
        {
            User32Interop.UnregisterPowerSettingNotification(_powerReg);
            _powerReg = 0;
        }

        if (_wtsRegistered && _hwnd != 0)
        {
            User32Interop.WTSUnRegisterSessionNotification(_hwnd);
            _wtsRegistered = false;
        }

        if (_hwnd != 0)
        {
            User32Interop.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = s_instance;
        if (inst is not null)
        {
            if (msg == User32Interop.WmWtssessionChange)
            {
                var evt = (int)wParam;
                if (evt == User32Interop.WtsSessionLock)
                {
                    inst._isLocked = true;
                    inst.StateChanged?.Invoke(inst, EventArgs.Empty);
                }
                else if (evt == User32Interop.WtsSessionUnlock)
                {
                    inst._isLocked = false;
                    inst.StateChanged?.Invoke(inst, EventArgs.Empty);
                }
            }
            else if (msg == User32Interop.WmPowerbroadcast && wParam == User32Interop.PbtPowersettingchange && lParam != 0)
            {
                var setting = *(User32Interop.PowerBroadcastSetting*)lParam;
                if (setting.PowerSetting == User32Interop.GuidConsoleDisplayState)
                {
                    inst._isDisplayOff = setting.Data == 0;
                    inst.StateChanged?.Invoke(inst, EventArgs.Empty);
                }
            }
        }

        if (msg == User32Interop.WmClose)
        {
            User32Interop.DestroyWindow(hwnd);
            User32Interop.PostQuitMessage(0);
            return 0;
        }

        return User32Interop.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
