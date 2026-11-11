using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Event-driven foreground window tracker using SetWinEventHook (EVENT_SYSTEM_FOREGROUND)
/// on a dedicated minimal message loop thread, plus direct GetForegroundWindow lookup.
/// </summary>
public sealed unsafe class WindowsForegroundMonitor : IForegroundMonitor
{
    private static WindowsForegroundMonitor? s_activeInstance;

    private readonly IClock _clock;
    private Thread? _hookThread;
    private nint _hookHandle;
    private nint _messageWindow;
    private volatile bool _running;

    public WindowsForegroundMonitor(IClock clock)
    {
        _clock = clock;
    }

    public string Name => "WindowsForegroundMonitor";

    public event EventHandler<ForegroundAppInfo>? ForegroundChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_running)
        {
            return Task.CompletedTask;
        }

        s_activeInstance = this;
        _running = true;
        var ready = new ManualResetEventSlim(false);
        _hookThread = new Thread(() => RunHookPump(ready))
        {
            IsBackground = true,
            Name = "Radpa.ForegroundHook",
        };
        _hookThread.Start();
        ready.Wait(TimeSpan.FromSeconds(2), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _running = false;
        if (_messageWindow != 0)
        {
            User32Interop.PostMessageW(_messageWindow, User32Interop.WmClose, 0, 0);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        if (ReferenceEquals(s_activeInstance, this))
        {
            s_activeInstance = null;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(StopAsync(CancellationToken.None));
    }

    public ForegroundAppInfo? GetCurrentForeground()
    {
        var hwnd = User32Interop.GetForegroundWindow();
        return hwnd == 0 ? null : ReadWindowInfo(hwnd);
    }

    private void RunHookPump(ManualResetEventSlim ready)
    {
        var className = $"RadpaFgPump_{Environment.ProcessId}_{Environment.CurrentManagedThreadId}";
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

        _messageWindow = User32Interop.CreateWindowExW(0, className, "RadpaFg", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _hookHandle = User32Interop.SetWinEventHook(
            User32Interop.EventSystemForeground,
            User32Interop.EventSystemForeground,
            0,
            &OnWinEvent,
            0,
            0,
            User32Interop.WineventOutofcontext | User32Interop.WineventSkipownprocess);

        ready.Set();

        while (_running && User32Interop.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            User32Interop.TranslateMessage(ref msg);
            User32Interop.DispatchMessageW(ref msg);
        }

        if (_hookHandle != 0)
        {
            User32Interop.UnhookWinEvent(_hookHandle);
            _hookHandle = 0;
        }

        if (_messageWindow != 0)
        {
            User32Interop.DestroyWindow(_messageWindow);
            _messageWindow = 0;
        }
    }

    private ForegroundAppInfo? ReadWindowInfo(nint hwnd)
    {
        _ = User32Interop.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        string? title = null;
        Span<char> titleBuf = stackalloc char[256];
        fixed (char* ptr = titleBuf)
        {
            var len = User32Interop.GetWindowTextW(hwnd, ptr, titleBuf.Length);
            if (len > 0)
            {
                title = new string(titleBuf[..len]);
            }
        }

        string? exePath = null;
        var procHandle = Kernel32Interop.OpenProcess(Kernel32Interop.ProcessQueryLimitedInformation, false, pid);
        if (procHandle != 0)
        {
            try
            {
                exePath = WindowsProcessMonitor.QueryImagePath(procHandle);
            }
            finally
            {
                Kernel32Interop.CloseHandle(procHandle);
            }
        }

        return new ForegroundAppInfo((int)pid, exePath, title, _clock.UtcNow);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == 0 || idObject != 0)
        {
            return;
        }

        var instance = s_activeInstance;
        if (instance is null)
        {
            return;
        }

        var info = instance.ReadWindowInfo(hwnd);
        if (info is not null)
        {
            instance.ForegroundChanged?.Invoke(instance, info.Value);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == User32Interop.WmClose)
        {
            User32Interop.DestroyWindow(hwnd);
            User32Interop.PostQuitMessage(0);
            return 0;
        }

        return User32Interop.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
