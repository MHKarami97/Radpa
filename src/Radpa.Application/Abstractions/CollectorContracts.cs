using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;

namespace Radpa.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset LocalNow { get; }
}

public interface ICollector : IAsyncDisposable
{
    string Name { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public readonly record struct ProcessInfo(
    int ProcessId,
    string ExecutablePath,
    string? PackageFamilyName,
    string? ProductName,
    DateTimeOffset StartTimeUtc);

public sealed class ProcessExitedEventArgs : EventArgs
{
    public ProcessExitedEventArgs(int processId, DateTimeOffset exitedUtc)
    {
        ProcessId = processId;
        ExitedUtc = exitedUtc;
    }

    public int ProcessId { get; }
    public DateTimeOffset ExitedUtc { get; }
}

public interface IProcessMonitor : ICollector
{
    event EventHandler<ProcessInfo>? ProcessStarted;
    event EventHandler<ProcessExitedEventArgs>? ProcessExited;
    IReadOnlyList<ProcessInfo> GetRunningProcesses();
}

public readonly record struct ForegroundAppInfo(
    int ProcessId,
    string? ExecutablePath,
    string? WindowTitle,
    DateTimeOffset ObservedUtc);

public interface IForegroundMonitor : ICollector
{
    event EventHandler<ForegroundAppInfo>? ForegroundChanged;
    ForegroundAppInfo? GetCurrentForeground();
}

public interface IIdleTimeProvider
{
    TimeSpan GetIdleDuration();
}

public interface ISystemStateMonitor : ICollector
{
    bool IsScreenLocked { get; }
    bool IsDisplayOff { get; }
    event EventHandler? StateChanged;
}

public readonly record struct ProcessResourceReading(
    int ProcessId,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    long DiskReadBytesDelta,
    long DiskWriteBytesDelta);

public interface IResourceSampler
{
    void SampleProcesses(IReadOnlyList<int> processIds, List<ProcessResourceReading> destination);
}

public readonly record struct ProcessGpuReading(
    int ProcessId,
    double? GpuPercent,
    long? DedicatedVramBytes);

public interface IGpuSampler : IDisposable
{
    bool IsAvailable { get; }
    void SampleProcesses(Dictionary<int, ProcessGpuReading> destination);
}

public readonly record struct NetworkTrafficReading(
    int ProcessId,
    string InterfaceName,
    NetworkInterfaceKind InterfaceKind,
    NetworkProtocol Protocol,
    string RemoteIp,
    long BytesSent,
    long BytesReceived);

public interface INetworkCollector : ICollector
{
    bool IsActive { get; }
    string? LastError { get; }
    event EventHandler<IReadOnlyList<NetworkTrafficReading>>? TrafficBatchReceived;
    Task<bool> EnableElevatedAsync(CancellationToken cancellationToken);
    Task DisableAsync(CancellationToken cancellationToken);
}
