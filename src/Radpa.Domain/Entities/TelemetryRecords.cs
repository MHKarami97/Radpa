using Radpa.Domain.ValueObjects;

namespace Radpa.Domain.Entities;

/// <summary>
/// Tracks open, active (foreground + non-idle), and on-screen (visible foreground + display on + unlocked)
/// intervals for a single application instance or grouped session window.
/// </summary>
public sealed record UsageSession(
    long Id,
    AppKey AppKey,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    long OpenSeconds,
    long ActiveSeconds,
    long OnScreenSeconds,
    string? ProjectTag = null);

/// <summary>
/// Per-sample resource snapshot. Uses a struct to minimize GC allocations in hot paths.
/// GPU metrics are nullable because WDDM counters may be unavailable on some drivers/VMs.
/// </summary>
public readonly record struct ResourceSample(
    AppKey AppKey,
    DateTimeOffset TimestampUtc,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    long DiskReadBytesDelta,
    long DiskWriteBytesDelta,
    double? GpuPercent,
    long? GpuVramBytes,
    string? WindowTitle = null);

/// <summary>
/// Aggregated per-interval network footprint sample with separate sent/received byte counts.
/// </summary>
public readonly record struct NetworkSample(
    AppKey AppKey,
    DateTimeOffset TimestampUtc,
    string InterfaceName,
    NetworkInterfaceKind InterfaceKind,
    NetworkProtocol Protocol,
    string RemoteIp,
    string? RemoteDomain,
    long BytesSent,
    long BytesReceived);
