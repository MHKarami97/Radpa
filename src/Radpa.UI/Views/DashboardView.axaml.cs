using System.ComponentModel;
using Avalonia.Controls;
using Radpa.UI.ViewModels;

namespace Radpa.UI.Views;

public sealed partial class DashboardView : UserControl
{
    private DashboardViewModel? _boundVm;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundVm is not null)
        {
            _boundVm.Loc.PropertyChanged -= OnLocalizationChanged;
        }

        _boundVm = DataContext as DashboardViewModel;
        if (_boundVm is not null)
        {
            _boundVm.Loc.PropertyChanged += OnLocalizationChanged;
            ApplyColumnHeaders(_boundVm);
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundVm is not null)
        {
            ApplyColumnHeaders(_boundVm);
        }
    }

    private void ApplyColumnHeaders(DashboardViewModel vm)
    {
        var grid = this.FindControl<DataGrid>("AppsDataGrid");
        if (grid is null || grid.Columns.Count < 11)
        {
            return;
        }

        grid.Columns[0].Header = vm.Loc.ColApp;
        grid.Columns[1].Header = vm.Loc.ColActive;
        grid.Columns[2].Header = vm.Loc.ColOnScreen;
        grid.Columns[3].Header = vm.Loc.ColOpen;
        grid.Columns[4].Header = vm.Loc.ColCpu;
        grid.Columns[5].Header = vm.Loc.ColRam;
        grid.Columns[6].Header = vm.Loc.ColDisk;
        grid.Columns[7].Header = vm.Loc.ColGpu;
        grid.Columns[8].Header = vm.Loc.ColSent;
        grid.Columns[9].Header = vm.Loc.ColReceived;
        grid.Columns[10].Header = vm.Loc.ColHealth;
    }
}
