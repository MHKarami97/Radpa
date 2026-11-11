using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Radpa.Application.Reports;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Radpa.UI.Localization;

namespace Radpa.UI.ViewModels;

public sealed record PrivacyAuditRowViewModel(
    string AppName,
    string InterfaceLabel,
    string RemoteIp,
    string DomainDisplay,
    string SentText,
    string ReceivedText,
    string FirstSeenText,
    bool IsFirstTime,
    string NewBadgeText);

public sealed partial class PrivacyAuditorViewModel : ObservableObject
{
    private IReadOnlyList<PrivacyAuditEntry> _source = [];
    private bool _usePersianDigits = true;

    public PrivacyAuditorViewModel(Localizer loc)
    {
        Loc = loc;
    }

    public Localizer Loc { get; }

    [ObservableProperty]
    public partial bool FirstTimeOnly { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    public ObservableCollection<PrivacyAuditRowViewModel> Entries { get; } = [];

    partial void OnFirstTimeOnlyChanged(bool value) => RebuildRows();
    partial void OnFilterTextChanged(string value) => RebuildRows();

    public void UpdateFromSnapshot(DashboardSnapshot snapshot, bool usePersianDigits)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _source = snapshot.PrivacyAudit;
        _usePersianDigits = usePersianDigits;
        RebuildRows();
    }

    private void RebuildRows()
    {
        var fa = Loc.IsPersian;
        var q = FilterText.Trim();
        Entries.Clear();

        foreach (var item in _source)
        {
            if (FirstTimeOnly && !item.IsFirstTimeDestination)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(q) &&
                !item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !item.RemoteIp.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !(item.RemoteDomain?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            var firstSeen = item.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (_usePersianDigits)
            {
                firstSeen = NumberFormatter.ToPersianDigits(firstSeen);
            }

            Entries.Add(new PrivacyAuditRowViewModel(
                item.DisplayName,
                $"{item.InterfaceName} ({item.InterfaceKind})",
                item.RemoteIp,
                string.IsNullOrWhiteSpace(item.RemoteDomain) ? "-" : item.RemoteDomain,
                NumberFormatter.FormatBytes(item.BytesSent, _usePersianDigits, fa),
                NumberFormatter.FormatBytes(item.BytesReceived, _usePersianDigits, fa),
                firstSeen,
                item.IsFirstTimeDestination,
                item.IsFirstTimeDestination ? Loc.BadgeNewDestination : string.Empty));
        }
    }
}
