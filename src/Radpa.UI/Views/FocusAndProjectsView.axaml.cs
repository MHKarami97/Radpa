using System.ComponentModel;
using Avalonia.Controls;
using Radpa.UI.ViewModels;

namespace Radpa.UI.Views;

public sealed partial class FocusAndProjectsView : UserControl
{
    private FocusAndProjectsViewModel? _boundVm;

    public FocusAndProjectsView()
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

        _boundVm = DataContext as FocusAndProjectsViewModel;
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

    private void ApplyHeaders(FocusAndProjectsViewModel vm)
    {
        var grid = this.FindControl<DataGrid>("ProjectGrid");
        if (grid is null || grid.Columns.Count < 3)
        {
            return;
        }

        grid.Columns[0].Header = vm.Loc.ColProjectName;
        grid.Columns[1].Header = vm.Loc.ColProjectActive;
        grid.Columns[2].Header = vm.Loc.ColProjectOnScreen;
    }
}
