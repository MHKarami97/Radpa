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
        ColApp.Header = vm.Loc.ColApp;
        ColActive.Header = vm.Loc.ColActive;
        ColOnScreen.Header = vm.Loc.ColOnScreen;
        ColOpen.Header = vm.Loc.ColOpen;
        ColCpu.Header = vm.Loc.ColCpu;
        ColRam.Header = vm.Loc.ColRam;
        ColDisk.Header = vm.Loc.ColDisk;
        ColGpu.Header = vm.Loc.ColGpu;
        ColSent.Header = vm.Loc.ColSent;
        ColReceived.Header = vm.Loc.ColReceived;
        ColHealth.Header = vm.Loc.ColHealth;
    }
}
