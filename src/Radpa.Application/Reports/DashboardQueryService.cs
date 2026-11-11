using Radpa.Application.Abstractions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;

namespace Radpa.Application.Reports;

public sealed record NetworkInterfaceBreakdown(
    string InterfaceName,
    NetworkInterfaceKind InterfaceKind,
    long BytesSent,
    long BytesReceived);

public sealed record TimelineBucket(
    DateTimeOffset BucketStartUtc,
    long ActiveSeconds,
    long SentBytes,
    long ReceivedBytes);

public sealed record ProjectSummary(
    string ProjectName,
    TimeSpan ActiveDuration,
    TimeSpan OnScreenDuration);

public sealed record DashboardSnapshot(
    IReadOnlyList<AppAggregateStats> Apps,
    IReadOnlyList<NetworkInterfaceBreakdown> NetworkByInterface,
    IReadOnlyList<TimelineBucket> Timeline,
    IReadOnlyList<PrivacyAuditEntry> PrivacyAudit,
    IReadOnlyList<ProjectSummary> Projects,
    TimeSpan TotalActive,
    long TotalNetworkSent,
    long TotalNetworkReceived);

public sealed class DashboardQueryService
{
    private readonly IUsageRepository _repository;
    private readonly IEncryptionService _encryption;

    public DashboardQueryService(IUsageRepository repository, IEncryptionService encryption)
    {
        _repository = repository;
        _encryption = encryption;
    }

    public async Task<DashboardSnapshot> QueryAsync(TimeRange range, CancellationToken cancellationToken)
    {
        var apps = (await _repository.GetAllAppsAsync(cancellationToken)).ToDictionary(a => a.Key);
        var sessions = await _repository.QuerySessionsAsync(range, cancellationToken);
        var resources = await _repository.QueryResourceSamplesAsync(range, cancellationToken);
        var network = await _repository.QueryNetworkSamplesAsync(range, cancellationToken);
        var allNetworkForFirstSeen = await _repository.QueryNetworkSamplesAsync(
            new TimeRange(DateTimeOffset.UnixEpoch, range.EndUtc),
            cancellationToken);

        var sessionStats = new Dictionary<AppKey, (long Open, long Active, long OnScreen, string? Project)>();
        foreach (var s in sessions)
        {
            var cur = sessionStats.GetValueOrDefault(s.AppKey);
            sessionStats[s.AppKey] = (
                cur.Open + s.OpenSeconds,
                cur.Active + s.ActiveSeconds,
                cur.OnScreen + s.OnScreenSeconds,
                cur.Project ?? s.ProjectTag);
        }

        var resStats = new Dictionary<AppKey, ResourceAccumulator>();
        foreach (var r in resources)
        {
            if (!resStats.TryGetValue(r.AppKey, out var acc))
            {
                acc = new ResourceAccumulator();
                resStats[r.AppKey] = acc;
            }

            acc.Count++;
            acc.CpuSum += r.CpuPercent;
            acc.CpuPeak = Math.Max(acc.CpuPeak, r.CpuPercent);
            acc.WsSum += r.WorkingSetBytes;
            acc.WsPeak = Math.Max(acc.WsPeak, r.WorkingSetBytes);
            acc.PrivPeak = Math.Max(acc.PrivPeak, r.PrivateBytes);
            acc.DiskRead += r.DiskReadBytesDelta;
            acc.DiskWrite += r.DiskWriteBytesDelta;

            if (r.GpuPercent is not null)
            {
                acc.GpuCount++;
                acc.GpuSum += r.GpuPercent.Value;
                acc.GpuPeak = Math.Max(acc.GpuPeak ?? 0, r.GpuPercent.Value);
            }

            if (r.GpuVramBytes is not null)
            {
                acc.VramPeak = Math.Max(acc.VramPeak ?? 0, r.GpuVramBytes.Value);
            }
        }

        var netAppStats = new Dictionary<AppKey, (long Sent, long Recv)>();
        var ifaceStats = new Dictionary<(string Name, NetworkInterfaceKind Kind), (long Sent, long Recv)>();
        var endpointStats = new Dictionary<(AppKey Key, string Iface, NetworkInterfaceKind Kind, string Ip), EndpointAccumulator>();

        var firstSeenLookup = new Dictionary<(AppKey Key, string Ip), DateTimeOffset>();
        foreach (var n in allNetworkForFirstSeen)
        {
            var ip = _encryption.DecryptField(n.RemoteIp) ?? n.RemoteIp;
            var k = (n.AppKey, ip);
            if (!firstSeenLookup.TryGetValue(k, out var existingFirst) || n.TimestampUtc < existingFirst)
            {
                firstSeenLookup[k] = n.TimestampUtc;
            }
        }

        foreach (var n in network)
        {
            var cur = netAppStats.GetValueOrDefault(n.AppKey);
            netAppStats[n.AppKey] = (cur.Sent + n.BytesSent, cur.Recv + n.BytesReceived);

            var ifaceKey = (n.InterfaceName, n.InterfaceKind);
            var ic = ifaceStats.GetValueOrDefault(ifaceKey);
            ifaceStats[ifaceKey] = (ic.Sent + n.BytesSent, ic.Recv + n.BytesReceived);

            var ip = _encryption.DecryptField(n.RemoteIp) ?? n.RemoteIp;
            var domain = _encryption.DecryptField(n.RemoteDomain);
            var epKey = (n.AppKey, n.InterfaceName, n.InterfaceKind, ip);
            if (!endpointStats.TryGetValue(epKey, out var ep))
            {
                ep = new EndpointAccumulator(domain, n.TimestampUtc);
                endpointStats[epKey] = ep;
            }

            ep.Sent += n.BytesSent;
            ep.Recv += n.BytesReceived;
            if (n.TimestampUtc > ep.LastSeenUtc)
            {
                ep.LastSeenUtc = n.TimestampUtc;
            }

            ep.Domain ??= domain;
        }

        var allKeys = new HashSet<AppKey>(sessionStats.Keys);
        allKeys.UnionWith(resStats.Keys);
        allKeys.UnionWith(netAppStats.Keys);

        var appAggregates = new List<AppAggregateStats>(allKeys.Count);
        foreach (var key in allKeys)
        {
            var identity = apps.GetValueOrDefault(key) ?? new AppIdentity(key, key.Value, key.Value);
            var sess = sessionStats.GetValueOrDefault(key);
            resStats.TryGetValue(key, out var res);
            var net = netAppStats.GetValueOrDefault(key);

            var open = TimeSpan.FromSeconds(sess.Open);
            var active = TimeSpan.FromSeconds(sess.Active);
            var onScreen = TimeSpan.FromSeconds(sess.OnScreen);
            var avgCpu = res is { Count: > 0 } ? res.CpuSum / res.Count : 0;
            var avgWs = res is { Count: > 0 } ? res.WsSum / res.Count : 0;
            var peakWs = res?.WsPeak ?? 0;
            var diskRead = res?.DiskRead ?? 0;
            var diskWrite = res?.DiskWrite ?? 0;
            double? avgGpu = res is { GpuCount: > 0 } ? res.GpuSum / res.GpuCount : null;

            var health = HealthScoreCalculator.Calculate(
                avgCpu,
                peakWs,
                diskRead + diskWrite,
                open,
                active,
                net.Sent,
                net.Recv);

            appAggregates.Add(new AppAggregateStats(
                key,
                identity.DisplayName,
                identity.ExecutablePath,
                open,
                active,
                onScreen,
                Math.Round(avgCpu, 1),
                Math.Round(res?.CpuPeak ?? 0, 1),
                avgWs,
                peakWs,
                res?.PrivPeak ?? 0,
                diskRead,
                diskWrite,
                avgGpu is null ? null : Math.Round(avgGpu.Value, 1),
                res?.GpuPeak is null ? null : Math.Round(res.GpuPeak.Value, 1),
                res?.VramPeak,
                net.Sent,
                net.Recv,
                health,
                sess.Project));
        }

        var privacyEntries = endpointStats.Select(kv =>
        {
            var name = apps.TryGetValue(kv.Key.Key, out var app) ? app.DisplayName : kv.Key.Key.Value;
            var firstSeen = firstSeenLookup.TryGetValue((kv.Key.Key, kv.Key.Ip), out var fs) ? fs : kv.Value.FirstSeenUtc;
            var isFirstTime = firstSeen >= range.StartUtc;
            return new PrivacyAuditEntry(
                kv.Key.Key,
                name,
                kv.Key.Iface,
                kv.Key.Kind,
                kv.Key.Ip,
                kv.Value.Domain,
                kv.Value.Sent,
                kv.Value.Recv,
                firstSeen,
                kv.Value.LastSeenUtc,
                isFirstTime);
        })
        .OrderByDescending(e => e.BytesSent + e.BytesReceived)
        .ToList();

        var timeline = BuildTimeline(range, sessions, network);
        var projectSummaries = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.ProjectTag))
            .GroupBy(s => s.ProjectTag!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProjectSummary(
                g.Key,
                TimeSpan.FromSeconds(g.Sum(x => x.ActiveSeconds)),
                TimeSpan.FromSeconds(g.Sum(x => x.OnScreenSeconds))))
            .OrderByDescending(p => p.ActiveDuration)
            .ToList();

        var ifaceBreakdown = ifaceStats
            .Select(kv => new NetworkInterfaceBreakdown(kv.Key.Name, kv.Key.Kind, kv.Value.Sent, kv.Value.Recv))
            .OrderByDescending(x => x.BytesSent + x.BytesReceived)
            .ToList();

        return new DashboardSnapshot(
            appAggregates.OrderByDescending(a => a.ActiveDuration).ThenByDescending(a => a.OpenDuration).ToList(),
            ifaceBreakdown,
            timeline,
            privacyEntries,
            projectSummaries,
            TimeSpan.FromSeconds(appAggregates.Sum(a => (long)a.ActiveDuration.TotalSeconds)),
            appAggregates.Sum(a => a.NetworkSentBytes),
            appAggregates.Sum(a => a.NetworkReceivedBytes));
    }

    public async Task<IReadOnlyList<PeriodComparisonEntry>> ComparePeriodsAsync(
        TimeRange before,
        TimeRange after,
        CancellationToken cancellationToken)
    {
        var snapBefore = await QueryAsync(before, cancellationToken);
        var snapAfter = await QueryAsync(after, cancellationToken);

        var beforeMap = snapBefore.Apps.ToDictionary(a => a.AppKey);
        var afterMap = snapAfter.Apps.ToDictionary(a => a.AppKey);
        var keys = new HashSet<AppKey>(beforeMap.Keys);
        keys.UnionWith(afterMap.Keys);

        var result = new List<PeriodComparisonEntry>(keys.Count);
        foreach (var k in keys)
        {
            beforeMap.TryGetValue(k, out var b);
            afterMap.TryGetValue(k, out var a);
            result.Add(new PeriodComparisonEntry(
                k,
                a?.DisplayName ?? b?.DisplayName ?? k.Value,
                b?.ActiveDuration ?? TimeSpan.Zero,
                a?.ActiveDuration ?? TimeSpan.Zero,
                b?.AverageCpuPercent ?? 0,
                a?.AverageCpuPercent ?? 0,
                b?.PeakWorkingSetBytes ?? 0,
                a?.PeakWorkingSetBytes ?? 0,
                (b?.NetworkSentBytes ?? 0) + (b?.NetworkReceivedBytes ?? 0),
                (a?.NetworkSentBytes ?? 0) + (a?.NetworkReceivedBytes ?? 0)));
        }

        return result.OrderByDescending(c => Math.Abs(c.ActiveDelta.TotalSeconds)).ToList();
    }

    private static List<TimelineBucket> BuildTimeline(
        TimeRange range,
        IReadOnlyList<UsageSession> sessions,
        IReadOnlyList<NetworkSample> network)
    {
        var bucketSpan = range.Duration.TotalHours <= 36 ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var buckets = new SortedDictionary<DateTimeOffset, (long Active, long Sent, long Recv)>();

        foreach (var s in sessions)
        {
            var bucketStart = AlignToBucket(s.StartUtc, bucketSpan);
            var cur = buckets.GetValueOrDefault(bucketStart);
            buckets[bucketStart] = (cur.Active + s.ActiveSeconds, cur.Sent, cur.Recv);
        }

        foreach (var n in network)
        {
            var bucketStart = AlignToBucket(n.TimestampUtc, bucketSpan);
            var cur = buckets.GetValueOrDefault(bucketStart);
            buckets[bucketStart] = (cur.Active, cur.Sent + n.BytesSent, cur.Recv + n.BytesReceived);
        }

        return buckets
            .Select(kv => new TimelineBucket(kv.Key, kv.Value.Active, kv.Value.Sent, kv.Value.Recv))
            .ToList();
    }

    private static DateTimeOffset AlignToBucket(DateTimeOffset ts, TimeSpan span)
    {
        var utc = ts.ToUniversalTime();
        return span >= TimeSpan.FromDays(1)
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private sealed class ResourceAccumulator
    {
        public int Count;
        public double CpuSum;
        public double CpuPeak;
        public long WsSum;
        public long WsPeak;
        public long PrivPeak;
        public long DiskRead;
        public long DiskWrite;
        public int GpuCount;
        public double GpuSum;
        public double? GpuPeak;
        public long? VramPeak;
    }

    private sealed class EndpointAccumulator(string? domain, DateTimeOffset firstSeenUtc)
    {
        public string? Domain { get; set; } = domain;
        public DateTimeOffset FirstSeenUtc { get; } = firstSeenUtc;
        public DateTimeOffset LastSeenUtc { get; set; } = firstSeenUtc;
        public long Sent { get; set; }
        public long Recv { get; set; }
    }
}
