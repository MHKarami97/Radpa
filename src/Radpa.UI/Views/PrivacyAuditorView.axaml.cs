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
        ColApp.Header = vm.Loc.ColApp;
        ColIface.Header = vm.Loc.ColInterface;
        ColIp.Header = vm.Loc.ColRemoteIp;
        ColDomain.Header = vm.Loc.ColDomain;
        ColSent.Header = vm.Loc.ColSent;
        ColRecv.Header = vm.Loc.ColReceived;
        ColFirstSeen.Header = vm.Loc.ColFirstSeen;
        ColBadge.Header = vm.Loc.BadgeNewDestination;
    }
}
