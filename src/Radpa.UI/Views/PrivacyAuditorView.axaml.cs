using System.ComponentModel;
using Avalonia.Controls;
using Radpa.UI.ViewModels;

namespace Radpa.UI.Views;

public sealed partial class PrivacyAuditorView : UserControl
{
    private PrivacyAuditorViewModel? _boundVm;

    public PrivacyAuditorView()
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

        _boundVm = DataContext as PrivacyAuditorViewModel;
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

    private void ApplyHeaders(PrivacyAuditorViewModel vm)
    {
        var grid = this.FindControl<DataGrid>("AuditDataGrid");
        if (grid is null || grid.Columns.Count < 8)
        {
            return;
        }

        grid.Columns[0].Header = vm.Loc.ColApp;
        grid.Columns[1].Header = vm.Loc.ColInterface;
        grid.Columns[2].Header = vm.Loc.ColRemoteIp;
        grid.Columns[3].Header = vm.Loc.ColDomain;
        grid.Columns[4].Header = vm.Loc.ColSent;
        grid.Columns[5].Header = vm.Loc.ColReceived;
        grid.Columns[6].Header = vm.Loc.ColFirstSeen;
        grid.Columns[7].Header = vm.Loc.BadgeNewDestination;
    }
}
