using System.ComponentModel;
using Avalonia.Controls;
using Radpa.UI.ViewModels;

namespace Radpa.UI.Views;

public sealed partial class InsightsView : UserControl
{
    private InsightsViewModel? _boundVm;

    public InsightsView()
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

        _boundVm = DataContext as InsightsViewModel;
        if (_boundVm is not null)
        {
            _boundVm.Loc.PropertyChanged += OnLocalizationChanged;
            ApplyHeaders(_boundVm);
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundVm is not null)
        {
            ApplyHeaders(_boundVm);
        }
    }

    private void ApplyHeaders(InsightsViewModel vm)
    {
        var healthGrid = this.FindControl<DataGrid>("HealthGrid");
        if (healthGrid is not null && healthGrid.Columns.Count >= 6)
        {
            healthGrid.Columns[0].Header = vm.Loc.ColApp;
            healthGrid.Columns[1].Header = vm.Loc.ColHealthTotal;
            healthGrid.Columns[2].Header = vm.Loc.ColHealthResource;
            healthGrid.Columns[3].Header = vm.Loc.ColHealthNetwork;
            healthGrid.Columns[4].Header = vm.Loc.ColHealthStartup;
            healthGrid.Columns[5].Header = vm.Loc.ColHealthGrade;
        }

        var compareGrid = this.FindControl<DataGrid>("CompareGrid");
        if (compareGrid is not null && compareGrid.Columns.Count >= 6)
        {
            compareGrid.Columns[0].Header = vm.Loc.ColApp;
            compareGrid.Columns[1].Header = vm.Loc.ColCompareBefore;
            compareGrid.Columns[2].Header = vm.Loc.ColCompareAfter;
            compareGrid.Columns[3].Header = vm.Loc.ColCompareDeltaActive;
            compareGrid.Columns[4].Header = vm.Loc.ColCompareDeltaCpu;
            compareGrid.Columns[5].Header = vm.Loc.ColCompareDeltaNet;
        }
    }
}
