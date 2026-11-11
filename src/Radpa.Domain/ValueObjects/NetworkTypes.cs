namespace Radpa.Domain.ValueObjects;

public enum NetworkInterfaceKind
{
    Unknown = 0,
    Ethernet = 1,
    WiFi = 2,
    Vpn = 3,
    Loopback = 4,
    Cellular = 5,
    Other = 6,
}

public enum NetworkProtocol
{
    Unknown = 0,
    Tcp = 1,
    Udp = 2,
}
