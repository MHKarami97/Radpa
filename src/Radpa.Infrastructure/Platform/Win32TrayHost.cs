using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Platform;

/// <summary>
/// Ultra-lightweight native Win32 Tray Icon host for Radpa's background --collector process.
/// Allows closing the Avalonia UI process completely to free memory while keeping tray + collector active.
/// </summary>
public sealed unsafe class Win32TrayHost : IDisposable
{
    private const uint TrayCallbackMsg = User32Interop.WmApp + 42;
    private const uint CmdOpenDashboard = 1001;
    private const uint CmdExitCollector = 1002;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;

    private static Win32TrayHost? s_instance;

    private readonly Action _onOpenUi;
    private readonly Action _onExit;
    private readonly string _tooltip;
    private readonly string? _iconPath;

    private Thread? _uiThread;
    private nint _hwnd;
    private nint _hIcon;
    private volatile bool _running;

    public Win32TrayHost(Action onOpenUi, Action onExit, string tooltip, string? iconPath = null)
    {
        _onOpenUi = onOpenUi;
        _onExit = onExit;
        _tooltip = tooltip;
        _iconPath = iconPath;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        s_instance = this;
        _running = true;
        var ready = new ManualResetEventSlim(false);
        _uiThread = new Thread(() => RunTrayLoop(ready))
        {
            IsBackground = false,
            Name = "Radpa.TrayHost",
        };
        _uiThread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void ShowBalloon(string title, string message, NotificationSeverity severity)
    {
        if (_hwnd == 0)
        {
            return;
        }

        var nid = CreateBaseIconData();
        nid.UFlags = PdhAndShellInterop.NifInfo;
        nid.DwInfoFlags = severity switch
        {
            NotificationSeverity.Warning => PdhAndShellInterop.NiifWarning,
            NotificationSeverity.Alert => PdhAndShellInterop.NiifError,
            _ => PdhAndShellInterop.NiifInfo,
        };

        CopyFixed(title, nid.SzInfoTitle, 64);
        CopyFixed(message, nid.SzInfo, 256);
        PdhAndShellInterop.Shell_NotifyIconW(PdhAndShellInterop.NimModify, ref nid);
    }

    public void Stop()
    {
        _running = false;
        if (_hwnd != 0)
        {
            User32Interop.PostMessageW(_hwnd, User32Interop.WmClose, 0, 0);
        }
    }

    public void Dispose()
    {
        Stop();
        _uiThread?.Join(TimeSpan.FromSeconds(2));
        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
        }
    }

    public static void SetAppUserModelId(string appId = "Radpa.App")
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                PdhAndShellInterop.SetCurrentProcessExplicitAppUserModelID(appId);
            }
            catch
            {
            }
        }
    }

    public static void ApplyWindowIcon(nint hwnd, string? iconPath = null)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return;
        }

        try
        {
            var path = iconPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "radpa.ico");
                if (!File.Exists(path))
                {
                    var alt = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Icons", "radpa.ico");
                    if (File.Exists(alt))
                    {
                        path = Path.GetFullPath(alt);
                    }
                }
            }

            nint hIconBig = 0;
            nint hIconSmall = 0;

            if (File.Exists(path))
            {
                hIconBig = User32Interop.LoadImageW(0, path, ImageIcon, 32, 32, LrLoadFromFile);
                hIconSmall = User32Interop.LoadImageW(0, path, ImageIcon, 16, 16, LrLoadFromFile);
            }

            if (hIconBig == 0 || hIconSmall == 0)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    nint hBig = 0;
                    nint hSmall = 0;
                    PdhAndShellInterop.ExtractIconExW(exe, 0, &hBig, &hSmall, 1);
                    if (hIconBig == 0 && hBig != 0)
                    {
                        hIconBig = hBig;
                    }
                    if (hIconSmall == 0 && hSmall != 0)
                    {
                        hIconSmall = hSmall;
                    }
                }
            }

            const uint wmSetIcon = 0x0080;
            if (hIconBig != 0)
            {
                User32Interop.SendMessageW(hwnd, wmSetIcon, 1, hIconBig);
            }
            if (hIconSmall != 0)
            {
                User32Interop.SendMessageW(hwnd, wmSetIcon, 0, hIconSmall);
            }
        }
        catch
        {
        }
    }

    private void RunTrayLoop(ManualResetEventSlim ready)
    {
        if (!string.IsNullOrWhiteSpace(_iconPath) && File.Exists(_iconPath))
        {
            _hIcon = User32Interop.LoadImageW(0, _iconPath, ImageIcon, 16, 16, LrLoadFromFile);
        }

        if (_hIcon == 0)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                nint hSmall = 0;
                PdhAndShellInterop.ExtractIconExW(exe, 0, null, &hSmall, 1);
                if (hSmall != 0)
                {
                    _hIcon = hSmall;
                }
            }
        }

        var className = $"RadpaTrayClass_{Environment.ProcessId}";
        fixed (char* clsPtr = className)
        {
            var wc = new User32Interop.WndClassExW
            {
                CbSize = (uint)sizeof(User32Interop.WndClassExW),
                LpfnWndProc = &WndProc,
                LpszClassName = clsPtr,
                HIcon = _hIcon,
                HIconSm = _hIcon,
            };
            User32Interop.RegisterClassExW(ref wc);
        }

        const uint wsPopup = 0x80000000;
        const uint wsExToolWindow = 0x00000080;
        _hwnd = User32Interop.CreateWindowExW(wsExToolWindow, className, "RadpaTray", wsPopup, 0, 0, 0, 0, 0, 0, 0, 0);

        var nid = CreateBaseIconData();
        nid.UFlags = PdhAndShellInterop.NifMessage | PdhAndShellInterop.NifTip | (_hIcon != 0 ? PdhAndShellInterop.NifIcon : 0u);
        nid.UCallbackMessage = TrayCallbackMsg;
        nid.HIcon = _hIcon;
        CopyFixed(_tooltip, nid.SzTip, 128);
        PdhAndShellInterop.Shell_NotifyIconW(PdhAndShellInterop.NimAdd, ref nid);

        ready.Set();

        while (_running && User32Interop.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            User32Interop.TranslateMessage(ref msg);
            User32Interop.DispatchMessageW(ref msg);
        }

        var del = CreateBaseIconData();
        PdhAndShellInterop.Shell_NotifyIconW(PdhAndShellInterop.NimDelete, ref del);
        if (_hIcon != 0)
        {
            User32Interop.DestroyIcon(_hIcon);
            _hIcon = 0;
        }
    }

    private PdhAndShellInterop.NotifyIconDataW CreateBaseIconData() =>
        new()
        {
            CbSize = (uint)sizeof(PdhAndShellInterop.NotifyIconDataW),
            HWnd = _hwnd,
            UId = 1,
        };

    private static void CopyFixed(string text, char* dest, int maxChars)
    {
        var count = Math.Min(text.Length, maxChars - 1);
        for (var i = 0; i < count; i++)
        {
            dest[i] = text[i];
        }

        dest[count] = '\0';
    }

    private void ShowContextMenu()
    {
        var menu = User32Interop.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            User32Interop.AppendMenuW(menu, User32Interop.MfString, CmdOpenDashboard, "Open Radpa / باز کردن ردپا");
            User32Interop.AppendMenuW(menu, User32Interop.MfSeparator, 0, null);
            User32Interop.AppendMenuW(menu, User32Interop.MfString, CmdExitCollector, "Exit / خروج");

            User32Interop.GetCursorPos(out var pt);
            User32Interop.SetForegroundWindow(_hwnd);
            var cmd = User32Interop.TrackPopupMenuEx(
                menu,
                User32Interop.TpmRightbutton | User32Interop.TpmReturncmd,
                pt.X,
                pt.Y,
                _hwnd,
                0);

            if (cmd == CmdOpenDashboard)
            {
                _onOpenUi();
            }
            else if (cmd == CmdExitCollector)
            {
                _onExit();
            }
        }
        finally
        {
            User32Interop.DestroyMenu(menu);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var inst = s_instance;
        if (inst is not null && msg == TrayCallbackMsg)
        {
            var mouseMsg = (uint)(lParam & 0xFFFF);
            if (mouseMsg is User32Interop.WmLbuttondblclk or User32Interop.WmLbuttonup)
            {
                inst._onOpenUi();
            }
            else if (mouseMsg == User32Interop.WmRbuttonup)
            {
                inst.ShowContextMenu();
            }

            return 0;
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
