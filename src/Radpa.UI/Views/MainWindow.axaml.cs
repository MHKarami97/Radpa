using Avalonia.Controls;
using Avalonia.Threading;
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
        if (enableAutoRefresh)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += async (_, _) => await viewModel.RefreshAllAsync();
            _refreshTimer.Start();
            Opened += async (_, _) => await viewModel.RefreshAllAsync();
            Closed += (_, _) => _refreshTimer.Stop();
        }
    }
}
