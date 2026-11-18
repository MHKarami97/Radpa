using Avalonia.Controls;
using Avalonia.Threading;
using Radpa.Infrastructure.Platform;
using Radpa.UI.ViewModels;

namespace Radpa.UI.Views;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer? _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, bool enableAutoRefresh = true)
        : this()
    {
        DataContext = viewModel;
        WindowState = WindowState.Maximized;
        viewModel.SelectedTabIndex = 0;
        Opened += (_, _) =>
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? 0;
            if (hwnd != 0)
            {
                Win32TrayHost.ApplyWindowIcon(hwnd);
            }
        };
        if (enableAutoRefresh)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += async (_, _) => await viewModel.RefreshAllAsync();
            _refreshTimer.Start();
            Opened += async (_, _) => await viewModel.RefreshAllAsync();
            Closing += (sender, e) =>
            {
                if (DataContext is MainWindowViewModel vm && vm.Settings.MinimizeToTray)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            Closed += (_, _) =>
            {
                _refreshTimer?.Stop();
                if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            };
        }
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = global::Avalonia.Controls.WindowState.Maximized;
        Activate();
        if (DataContext is MainWindowViewModel vm)
        {
            _ = vm.RefreshAllAsync();
        }
    }
}
