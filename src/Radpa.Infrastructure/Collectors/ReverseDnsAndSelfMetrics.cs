using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Radpa.Application.Abstractions;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Bounded non-blocking reverse-DNS cache. Resolves PTR records asynchronously in the background
/// without blocking packet ingestion or UI threads.
/// </summary>
public sealed class ReverseDnsCache : IDnsResolverCache, IDisposable
{
    private const int MaxCacheEntries = 4096;

    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public ReverseDnsCache()
    {
        _worker = Task.Run(() => ProcessQueueAsync(_cts.Token));
    }

    public string? TryGetCachedHost(string ipAddress) =>
        _cache.TryGetValue(ipAddress, out var host) ? host : null;

    public void QueueResolve(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || _cache.ContainsKey(ipAddress))
        {
            return;
        }

        if (_inFlight.TryAdd(ipAddress, 0))
        {
            _queue.Writer.TryWrite(ipAddress);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var ip in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                if (_cache.Count >= MaxCacheEntries)
                {
                    _cache.Clear();
                }

                if (IPAddress.TryParse(ip, out var addr) && !IPAddress.IsLoopback(addr))
                {
                    using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lookupCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var entry = await Dns.GetHostEntryAsync(addr.ToString(), lookupCts.Token);
                    _cache[ip] = string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == ip
                        ? null
                        : entry.HostName;
                }
                else
                {
                    _cache[ip] = null;
                }
            }
            catch
            {
                _cache[ip] = null;
            }
            finally
            {
                _inFlight.TryRemove(ip, out _);
            }
        }
    }
}

public sealed class SelfMetricsProvider : ISelfMetricsProvider
{
    private readonly Process _current = Process.GetCurrentProcess();
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);
    private TimeSpan _lastTotalCpu;
    private long _lastTimestamp;

    public SelfMetricsProvider()
    {
        _lastTotalCpu = _current.TotalProcessorTime;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public SelfMetricsSnapshot Capture()
    {
        _current.Refresh();
        var nowTs = Stopwatch.GetTimestamp();
        var curCpu = _current.TotalProcessorTime;
        var elapsedSec = (double)(nowTs - _lastTimestamp) / Stopwatch.Frequency;
        var cpuPct = 0.0;
        if (elapsedSec > 0.1)
        {
            var deltaCpu = (curCpu - _lastTotalCpu).TotalSeconds;
            cpuPct = Math.Clamp((deltaCpu / (elapsedSec * _cpuCount)) * 100.0, 0.0, 100.0);
        }

        _lastTotalCpu = curCpu;
        _lastTimestamp = nowTs;
        return new SelfMetricsSnapshot(Math.Round(cpuPct, 2), _current.WorkingSet64, _current.PrivateMemorySize64);
    }
}
