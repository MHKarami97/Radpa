using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Radpa.Domain.ValueObjects;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Maps remote destination IP addresses to the active Windows network adapter (Ethernet, Wi-Fi, VPN, Loopback)
/// via iphlpapi GetBestInterfaceEx and NetworkInterface classification.
/// </summary>
public sealed class InterfaceClassifier
{
    private readonly ConcurrentDictionary<uint, (string Name, NetworkInterfaceKind Kind)> _indexCache = new();
    private readonly ConcurrentDictionary<IPAddress, uint> _routeCache = new();
    private long _lastRefreshTicks;

    public (string Name, NetworkInterfaceKind Kind) ResolveInterface(string remoteIp)
    {
        if (!IPAddress.TryParse(remoteIp, out var ip))
        {
            return ("Unknown", NetworkInterfaceKind.Unknown);
        }

        if (IPAddress.IsLoopback(ip))
        {
            return ("Loopback", NetworkInterfaceKind.Loopback);
        }

        RefreshAdaptersIfStale();

        var ifIndex = _routeCache.GetOrAdd(ip, static addr => QueryBestInterfaceIndex(addr));
        if (ifIndex != 0 && _indexCache.TryGetValue(ifIndex, out var cached))
        {
            return cached;
        }

        return ("Unknown", NetworkInterfaceKind.Unknown);
    }

    private void RefreshAdaptersIfStale()
    {
        var now = Environment.TickCount64;
        if (!_indexCache.IsEmpty && now - Volatile.Read(ref _lastRefreshTicks) < 15_000)
        {
            return;
        }

        Volatile.Write(ref _lastRefreshTicks, now);
        if (_routeCache.Count > 2048)
        {
            _routeCache.Clear();
        }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = ni.GetIPProperties();
                var kind = ClassifyInterface(ni);
                var label = string.IsNullOrWhiteSpace(ni.Name) ? ni.Description : ni.Name;

                if (ni.Supports(NetworkInterfaceComponent.IPv4))
                {
                    try
                    {
                        var v4 = props.GetIPv4Properties();
                        if (v4 is not null)
                        {
                            _indexCache[(uint)v4.Index] = (label, kind);
                        }
                    }
                    catch (NetworkInformationException)
                    {
                    }
                }

                if (ni.Supports(NetworkInterfaceComponent.IPv6))
                {
                    try
                    {
                        var v6 = props.GetIPv6Properties();
                        if (v6 is not null)
                        {
                            _indexCache[(uint)v6.Index] = (label, kind);
                        }
                    }
                    catch (NetworkInformationException)
                    {
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
    }

    internal static NetworkInterfaceKind ClassifyInterface(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        {
            return NetworkInterfaceKind.Loopback;
        }

        if (ni.NetworkInterfaceType is NetworkInterfaceType.Wireless80211)
        {
            return NetworkInterfaceKind.WiFi;
        }

        if (ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel ||
            ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
            ni.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
            ni.Description.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
            ni.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
            ni.Description.Contains("Cisco", StringComparison.OrdinalIgnoreCase) ||
            ni.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase))
        {
            return NetworkInterfaceKind.Vpn;
        }

        if (ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)
        {
            return NetworkInterfaceKind.Ethernet;
        }

        if (ni.NetworkInterfaceType is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2)
        {
            return NetworkInterfaceKind.Cellular;
        }

        return NetworkInterfaceKind.Other;
    }

    private static unsafe uint QueryBestInterfaceIndex(IPAddress destinationIp)
    {
        Span<byte> sockaddr = stackalloc byte[28];
        sockaddr.Clear();

        if (destinationIp.AddressFamily == AddressFamily.InterNetwork)
        {
            sockaddr[0] = 2; // AF_INET
            _ = destinationIp.TryWriteBytes(sockaddr.Slice(4, 4), out _);
        }
        else if (destinationIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            sockaddr[0] = 23; // AF_INET6
            _ = destinationIp.TryWriteBytes(sockaddr.Slice(8, 16), out _);
        }
        else
        {
            return 0;
        }

        fixed (byte* ptr = sockaddr)
        {
            return PdhAndShellInterop.GetBestInterfaceEx(ptr, out var ifIndex) == 0 ? ifIndex : 0;
        }
    }
}
