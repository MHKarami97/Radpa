namespace Radpa.Domain.ValueObjects;

public sealed record AppAggregateStats(
    AppKey AppKey,
    string DisplayName,
    string ExecutablePath,
    TimeSpan OpenDuration,
    TimeSpan ActiveDuration,
    TimeSpan OnScreenDuration,
    double AverageCpuPercent,
    double PeakCpuPercent,
    long AverageWorkingSetBytes,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long DiskReadBytes,
    long DiskWriteBytes,
    double? AverageGpuPercent,
    double? PeakGpuPercent,
    long? PeakGpuVramBytes,
    long NetworkSentBytes,
    long NetworkReceivedBytes,
    AppHealthScore HealthScore,
    string? ProjectTag = null);

public sealed record PrivacyAuditEntry(
    AppKey AppKey,
    string DisplayName,
    string InterfaceName,
    NetworkInterfaceKind InterfaceKind,
    string RemoteIp,
    string? RemoteDomain,
    long BytesSent,
    long BytesReceived,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    bool IsFirstTimeDestination);

public sealed record PeriodComparisonEntry(
    AppKey AppKey,
    string DisplayName,
    TimeSpan BeforeActive,
    TimeSpan AfterActive,
    double BeforeCpuAvg,
    double AfterCpuAvg,
    long BeforeRamPeakBytes,
    long AfterRamPeakBytes,
    long BeforeNetBytes,
    long AfterNetBytes)
{
    public TimeSpan ActiveDelta => AfterActive - BeforeActive;
    public double CpuDelta => AfterCpuAvg - BeforeCpuAvg;
    public long RamDeltaBytes => AfterRamPeakBytes - BeforeRamPeakBytes;
    public long NetDeltaBytes => AfterNetBytes - BeforeNetBytes;
}
