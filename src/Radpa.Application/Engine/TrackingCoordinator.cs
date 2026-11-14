using System.Threading.Channels;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;

namespace Radpa.Application.Engine;

/// <summary>
/// Event-driven + low-frequency (>=2s) collector orchestrator with in-memory aggregation,
/// bounded flush queue, batched persistence (30-60s), anomaly detection, and alert rules.
/// </summary>
public sealed class TrackingCoordinator : IAsyncDisposable
{
    private const int MaxPendingNetworkBuckets = 4096;
    private const int BaselineWindowSize = 24;

    private readonly IClock _clock;
    private readonly IProcessMonitor _processMonitor;
    private readonly IForegroundMonitor _foregroundMonitor;
    private readonly IIdleTimeProvider _idleProvider;
    private readonly ISystemStateMonitor _systemStateMonitor;
    private readonly IResourceSampler _resourceSampler;
    private readonly IGpuSampler _gpuSampler;
    private readonly INetworkCollector _networkCollector;
    private readonly IUsageRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IDnsResolverCache _dnsCache;
    private readonly INotificationService _notifications;
    private readonly IAppLogger _logger;
    private readonly FocusSessionManager _focusManager;
    private readonly RulesEvaluator _rulesEvaluator = new();

    private readonly Lock _sync = new();
    private readonly Dictionary<int, ActiveProcessState> _processes = [];
    private readonly Dictionary<AppKey, AppIdentity> _knownApps = [];
    private readonly Dictionary<AppKey, SessionAccumulator> _pendingSessions = [];
    private readonly Dictionary<NetworkBucketKey, NetworkAccumulator> _pendingNetwork = [];
    private readonly List<ResourceSample> _pendingResources = [];
    private readonly Dictionary<AppKey, Queue<double>> _cpuHistory = [];
    private readonly Dictionary<AppKey, Queue<double>> _uploadHistory = [];
    private readonly Dictionary<AppKey, HourlyUploadTracker> _hourlyUploads = [];
    private readonly List<int> _pidScratch = [];
    private readonly List<ProcessResourceReading> _resourceScratch = [];
    private readonly Dictionary<int, ProcessGpuReading> _gpuScratch = [];

    private readonly Channel<TelemetryFlushBatch> _flushChannel = Channel.CreateBounded<TelemetryFlushBatch>(
        new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private AppSettings _settings;
    private GroupingResolver _groupingResolver;
    private ProjectTagMatcher _projectMatcher;
    private CancellationTokenSource? _cts;
    private Task? _samplingTask;
    private Task? _writerTask;
    private int _foregroundPid;
    private string? _foregroundTitle;
    private DateTimeOffset _lastTickUtc;
    private DateTimeOffset _lastFlushUtc;

    public TrackingCoordinator(
        IClock clock,
        IProcessMonitor processMonitor,
        IForegroundMonitor foregroundMonitor,
        IIdleTimeProvider idleProvider,
        ISystemStateMonitor systemStateMonitor,
        IResourceSampler resourceSampler,
        IGpuSampler gpuSampler,
        INetworkCollector networkCollector,
        IUsageRepository repository,
        IEncryptionService encryption,
        IDnsResolverCache dnsCache,
        INotificationService notifications,
        IAppLogger logger,
        FocusSessionManager focusManager,
        AppSettings initialSettings)
    {
        _clock = clock;
        _processMonitor = processMonitor;
        _foregroundMonitor = foregroundMonitor;
        _idleProvider = idleProvider;
        _systemStateMonitor = systemStateMonitor;
        _resourceSampler = resourceSampler;
        _gpuSampler = gpuSampler;
        _networkCollector = networkCollector;
        _repository = repository;
        _encryption = encryption;
        _dnsCache = dnsCache;
        _notifications = notifications;
        _logger = logger;
        _focusManager = focusManager;
        _settings = initialSettings;
        _groupingResolver = new GroupingResolver(initialSettings.GroupingRules);
        _projectMatcher = new ProjectTagMatcher(initialSettings.ProjectRules);
    }

    public event EventHandler<AnomalyAlert>? AnomalyDetected;
    public event EventHandler<string>? RuleAlertTriggered;
    public event EventHandler? BatchFlushed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var now = _clock.UtcNow;
        _lastTickUtc = now;
        _lastFlushUtc = now;

        _processMonitor.ProcessStarted += OnProcessStarted;
        _processMonitor.ProcessExited += OnProcessExited;
        _foregroundMonitor.ForegroundChanged += OnForegroundChanged;
        _networkCollector.TrafficBatchReceived += OnNetworkBatchReceived;

        await _processMonitor.StartAsync(_cts.Token);
        await _foregroundMonitor.StartAsync(_cts.Token);
        await _systemStateMonitor.StartAsync(_cts.Token);
        await _networkCollector.StartAsync(_cts.Token);

        lock (_sync)
        {
            foreach (var proc in _processMonitor.GetRunningProcesses())
            {
                RegisterProcessLocked(proc);
            }

            var fg = _foregroundMonitor.GetCurrentForeground();
            if (fg is not null)
            {
                _foregroundPid = fg.Value.ProcessId;
                _foregroundTitle = _settings.RecordWindowTitles ? fg.Value.WindowTitle : null;
            }
        }

        _writerTask = Task.Run(() => RunFlushWriterAsync(_cts.Token), _cts.Token);
        _samplingTask = Task.Run(() => RunSamplingLoopAsync(_cts.Token), _cts.Token);
        _logger.Info("TrackingCoordinator started.");
    }

    public void UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            _settings = settings;
            _groupingResolver = new GroupingResolver(settings.GroupingRules);
            _projectMatcher = new ProjectTagMatcher(settings.ProjectRules);

            foreach (var entry in _processes.Values)
            {
                var identity = _groupingResolver.Resolve(entry.Process.ExecutablePath, entry.Process.PackageFamilyName, entry.Process.ProductName);
                entry.Identity = identity;
                _knownApps[identity.Key] = identity;
            }
        }
    }

    public Task FlushNowAsync(CancellationToken cancellationToken)
    {
        TelemetryFlushBatch? batch;
        lock (_sync)
        {
            batch = BuildFlushBatchLocked(_clock.UtcNow);
        }

        return batch is null ? Task.CompletedTask : PersistBatchAsync(batch, cancellationToken);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _processMonitor.ProcessStarted -= OnProcessStarted;
        _processMonitor.ProcessExited -= OnProcessExited;
        _foregroundMonitor.ForegroundChanged -= OnForegroundChanged;
        _networkCollector.TrafficBatchReceived -= OnNetworkBatchReceived;

        await _cts.CancelAsync();

        if (_samplingTask is not null)
        {
            try
            {
                await _samplingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        TelemetryFlushBatch? finalBatch;
        lock (_sync)
        {
            finalBatch = BuildFlushBatchLocked(_clock.UtcNow);
        }

        if (finalBatch is not null)
        {
            await _flushChannel.Writer.WriteAsync(finalBatch, CancellationToken.None);
        }

        _flushChannel.Writer.TryComplete();
        if (_writerTask is not null)
        {
            try
            {
                await _writerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _networkCollector.StopAsync(CancellationToken.None);
        await _systemStateMonitor.StopAsync(CancellationToken.None);
        await _foregroundMonitor.StopAsync(CancellationToken.None);
        await _processMonitor.StopAsync(CancellationToken.None);
        _logger.Info("TrackingCoordinator stopped cleanly.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        _gpuSampler.Dispose();
    }

    internal void ProcessTickForTests(DateTimeOffset nowUtc) => ExecuteSampleTick(nowUtc);

    internal void IngestNetworkReadingsForTests(IReadOnlyList<NetworkTrafficReading> readings, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            IngestNetworkLocked(readings, nowUtc);
        }
    }

    private async Task RunSamplingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var intervalSec = Math.Clamp(_settings.SamplingIntervalSeconds, 2, 60);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSec));
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                ExecuteSampleTick(_clock.UtcNow);
                if (Math.Clamp(_settings.SamplingIntervalSeconds, 2, 60) != intervalSec)
                {
                    break;
                }
            }
        }
    }

    private void ExecuteSampleTick(DateTimeOffset nowUtc)
    {
        TelemetryFlushBatch? flushBatch = null;
        List<AnomalyAlert>? anomalies = null;
        List<(NetworkAlertRule Rule, AppIdentity App, long Bytes)>? ruleTriggers = null;

        lock (_sync)
        {
            var elapsedSec = Math.Max(0, (long)Math.Round((nowUtc - _lastTickUtc).TotalSeconds));
            _lastTickUtc = nowUtc;

            var idle = _idleProvider.GetIdleDuration();
            var idleThreshold = TimeSpan.FromSeconds(Math.Clamp(_settings.IdleThresholdSeconds, 15, 3600));
            var isLocked = _systemStateMonitor.IsScreenLocked;
            var isDisplayOff = _systemStateMonitor.IsDisplayOff;

            // Refresh foreground in case an elevated window missed hook notification
            var currentFg = _foregroundMonitor.GetCurrentForeground();
            if (currentFg is not null && currentFg.Value.ProcessId > 0)
            {
                _foregroundPid = currentFg.Value.ProcessId;
                _foregroundTitle = _settings.RecordWindowTitles ? currentFg.Value.WindowTitle : null;
            }

            _pidScratch.Clear();
            var seenAppsThisTick = new HashSet<AppKey>();

            foreach (var proc in _processes.Values)
            {
                _pidScratch.Add(proc.Process.ProcessId);

                if (elapsedSec > 0 && seenAppsThisTick.Add(proc.Identity.Key))
                {
                    var isFg = proc.Process.ProcessId == _foregroundPid ||
                               (_processes.TryGetValue(_foregroundPid, out var fgProc) && fgProc.Identity.Key == proc.Identity.Key);

                    var titleForTag = isFg ? _foregroundTitle : null;
                    var projectTag = _projectMatcher.Match(proc.Identity, titleForTag);

                    if (!_pendingSessions.TryGetValue(proc.Identity.Key, out var acc))
                    {
                        acc = new SessionAccumulator(proc.Identity.Key, nowUtc.AddSeconds(-elapsedSec), nowUtc, projectTag);
                        _pendingSessions[proc.Identity.Key] = acc;
                    }

                    acc.EndUtc = nowUtc;
                    acc.ProjectTag ??= projectTag;

                    SessionMerger.AccumulateTick(
                        ref acc.OpenSeconds,
                        ref acc.ActiveSeconds,
                        ref acc.OnScreenSeconds,
                        elapsedSec,
                        isOpen: true,
                        isForeground: isFg,
                        isScreenLocked: isLocked,
                        isDisplayOff: isDisplayOff,
                        idleDuration: idle,
                        idleThreshold: idleThreshold);

                    if (isFg && !isLocked && !isDisplayOff && idle < idleThreshold)
                    {
                        _focusManager.RecordForegroundActivity(proc.Identity, elapsedSec, nowUtc);
                    }
                }
            }

            _resourceScratch.Clear();
            _resourceSampler.SampleProcesses(_pidScratch, _resourceScratch);

            _gpuScratch.Clear();
            if (_gpuSampler.IsAvailable)
            {
                _gpuSampler.SampleProcesses(_gpuScratch);
            }

            // Aggregate resource samples by grouped AppKey per tick to minimize DB rows
            var appResources = new Dictionary<AppKey, ResourceSample>();
            foreach (var reading in _resourceScratch)
            {
                if (!_processes.TryGetValue(reading.ProcessId, out var state))
                {
                    continue;
                }

                _gpuScratch.TryGetValue(reading.ProcessId, out var gpu);
                var title = reading.ProcessId == _foregroundPid && _settings.RecordWindowTitles
                    ? _encryption.EncryptField(_foregroundTitle)
                    : null;

                if (appResources.TryGetValue(state.Identity.Key, out var existing))
                {
                    double? mergedGpu = existing.GpuPercent is not null || gpu.GpuPercent is not null
                        ? (existing.GpuPercent ?? 0) + (gpu.GpuPercent ?? 0)
                        : null;
                    long? mergedVram = existing.GpuVramBytes is not null || gpu.DedicatedVramBytes is not null
                        ? (existing.GpuVramBytes ?? 0) + (gpu.DedicatedVramBytes ?? 0)
                        : null;

                    appResources[state.Identity.Key] = existing with
                    {
                        CpuPercent = existing.CpuPercent + reading.CpuPercent,
                        WorkingSetBytes = existing.WorkingSetBytes + reading.WorkingSetBytes,
                        PrivateBytes = existing.PrivateBytes + reading.PrivateBytes,
                        DiskReadBytesDelta = existing.DiskReadBytesDelta + reading.DiskReadBytesDelta,
                        DiskWriteBytesDelta = existing.DiskWriteBytesDelta + reading.DiskWriteBytesDelta,
                        GpuPercent = mergedGpu,
                        GpuVramBytes = mergedVram,
                        WindowTitle = existing.WindowTitle ?? title,
                    };
                }
                else
                {
                    appResources[state.Identity.Key] = new ResourceSample(
                        state.Identity.Key,
                        nowUtc,
                        reading.CpuPercent,
                        reading.WorkingSetBytes,
                        reading.PrivateBytes,
                        reading.DiskReadBytesDelta,
                        reading.DiskWriteBytesDelta,
                        gpu.GpuPercent,
                        gpu.DedicatedVramBytes,
                        title);
                }
            }

            foreach (var sample in appResources.Values)
            {
                if (_pendingResources.Count < 8192)
                {
                    _pendingResources.Add(sample);
                }

                if (_knownApps.TryGetValue(sample.AppKey, out var appIdentity))
                {
                    var alert = EvaluateBaseline(_cpuHistory, sample.AppKey, appIdentity.DisplayName, AnomalyMetricKind.CpuPercent, sample.CpuPercent, nowUtc);
                    if (alert is not null)
                    {
                        anomalies ??= [];
                        anomalies.Add(alert);
                    }
                }
            }

            // Evaluate hourly upload rules
            foreach (var (key, tracker) in _hourlyUploads)
            {
                tracker.PruneOlderThan(nowUtc.AddHours(-1));
                if (_knownApps.TryGetValue(key, out var appIdentity))
                {
                    var fired = _rulesEvaluator.EvaluateUploadPerHourRules(_settings.AlertRules, appIdentity, tracker.TotalBytes, nowUtc);
                    foreach (var rule in fired)
                    {
                        ruleTriggers ??= [];
                        ruleTriggers.Add((rule, appIdentity, tracker.TotalBytes));
                    }
                }
            }

            var flushInterval = TimeSpan.FromSeconds(Math.Clamp(_settings.FlushIntervalSeconds, 3, 120));
            if (nowUtc - _lastFlushUtc >= flushInterval)
            {
                flushBatch = BuildFlushBatchLocked(nowUtc);
                _lastFlushUtc = nowUtc;
            }
        }

        if (anomalies is not null)
        {
            foreach (var alert in anomalies)
            {
                AnomalyDetected?.Invoke(this, alert);
                _notifications.ShowNotification(
                    $"Radpa Anomaly: {alert.DisplayName}",
                    alert.Explanation,
                    NotificationSeverity.Warning);
            }
        }

        if (ruleTriggers is not null)
        {
            foreach (var (rule, app, bytes) in ruleTriggers)
            {
                var mb = bytes / (1024.0 * 1024.0);
                var msg = $"{rule.Name}: {app.DisplayName} uploaded {mb:F1} MB in the last hour (limit {rule.MaxUploadMegabytesPerHour} MB).";
                RuleAlertTriggered?.Invoke(this, msg);
                _notifications.ShowNotification("Radpa Rule Alert", msg, NotificationSeverity.Alert);
            }
        }

        if (flushBatch is not null)
        {
            _flushChannel.Writer.TryWrite(flushBatch);
        }
    }

    private void OnProcessStarted(object? sender, ProcessInfo info)
    {
        lock (_sync)
        {
            RegisterProcessLocked(info);
        }
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        lock (_sync)
        {
            _processes.Remove(e.ProcessId);
        }
    }

    private void OnForegroundChanged(object? sender, ForegroundAppInfo info)
    {
        lock (_sync)
        {
            _foregroundPid = info.ProcessId;
            _foregroundTitle = _settings.RecordWindowTitles ? info.WindowTitle : null;

            if (!_processes.ContainsKey(info.ProcessId) && !string.IsNullOrWhiteSpace(info.ExecutablePath))
            {
                RegisterProcessLocked(new ProcessInfo(info.ProcessId, info.ExecutablePath, null, null, info.ObservedUtc));
            }
        }
    }

    private void OnNetworkBatchReceived(object? sender, IReadOnlyList<NetworkTrafficReading> readings)
    {
        lock (_sync)
        {
            IngestNetworkLocked(readings, _clock.UtcNow);
        }
    }

    private void IngestNetworkLocked(IReadOnlyList<NetworkTrafficReading> readings, DateTimeOffset nowUtc)
    {
        foreach (var item in readings)
        {
            if (!_processes.TryGetValue(item.ProcessId, out var proc))
            {
                continue;
            }

            if (_pendingNetwork.Count >= MaxPendingNetworkBuckets)
            {
                break;
            }

            var key = new NetworkBucketKey(proc.Identity.Key, item.InterfaceName, item.InterfaceKind, item.Protocol, item.RemoteIp);
            if (!_pendingNetwork.TryGetValue(key, out var acc))
            {
                acc = new NetworkAccumulator(nowUtc);
                _pendingNetwork[key] = acc;
            }

            acc.BytesSent += item.BytesSent;
            acc.BytesReceived += item.BytesReceived;
            acc.LastSeenUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(item.RemoteIp))
            {
                _dnsCache.QueueResolve(item.RemoteIp);
            }

            if (item.BytesSent > 0)
            {
                if (!_hourlyUploads.TryGetValue(proc.Identity.Key, out var tracker))
                {
                    tracker = new HourlyUploadTracker();
                    _hourlyUploads[proc.Identity.Key] = tracker;
                }

                tracker.Add(nowUtc, item.BytesSent);
            }
        }
    }

    private void RegisterProcessLocked(ProcessInfo info)
    {
        var identity = _groupingResolver.Resolve(info.ExecutablePath, info.PackageFamilyName, info.ProductName);
        _knownApps[identity.Key] = identity;
        _processes[info.ProcessId] = new ActiveProcessState(info, identity);
    }

    private TelemetryFlushBatch? BuildFlushBatchLocked(DateTimeOffset nowUtc)
    {
        if (_pendingSessions.Count == 0 && _pendingResources.Count == 0 && _pendingNetwork.Count == 0)
        {
            return null;
        }

        var apps = _knownApps.Values.ToArray();
        var sessions = new List<UsageSession>(_pendingSessions.Count);
        foreach (var s in _pendingSessions.Values)
        {
            sessions.Add(new UsageSession(0, s.AppKey, s.StartUtc, s.EndUtc, s.OpenSeconds, s.ActiveSeconds, s.OnScreenSeconds, s.ProjectTag));
        }

        _pendingSessions.Clear();

        var resources = _pendingResources.ToArray();
        _pendingResources.Clear();

        var network = new List<NetworkSample>(_pendingNetwork.Count);
        foreach (var (bucket, acc) in _pendingNetwork)
        {
            var host = _dnsCache.TryGetCachedHost(bucket.RemoteIp);
            var encIp = _encryption.EncryptField(bucket.RemoteIp) ?? bucket.RemoteIp;
            var encDomain = _encryption.EncryptField(host);

            network.Add(new NetworkSample(
                bucket.AppKey,
                acc.LastSeenUtc == default ? nowUtc : acc.LastSeenUtc,
                bucket.InterfaceName,
                bucket.InterfaceKind,
                bucket.Protocol,
                encIp,
                encDomain,
                acc.BytesSent,
                acc.BytesReceived));

            if (_knownApps.TryGetValue(bucket.AppKey, out var appIdentity))
            {
                var alert = EvaluateBaseline(_uploadHistory, bucket.AppKey, appIdentity.DisplayName, AnomalyMetricKind.UploadBytesPerMinute, acc.BytesSent, nowUtc);
                if (alert is not null)
                {
                    AnomalyDetected?.Invoke(this, alert);
                }
            }
        }

        _pendingNetwork.Clear();
        return new TelemetryFlushBatch(apps, sessions, resources, network);
    }

    private async Task RunFlushWriterAsync(CancellationToken ct)
    {
        await foreach (var batch in _flushChannel.Reader.ReadAllAsync(ct))
        {
            await PersistBatchAsync(batch, ct);
        }
    }

    private async Task PersistBatchAsync(TelemetryFlushBatch batch, CancellationToken ct)
    {
        try
        {
            await _repository.SaveBatchAsync(batch, ct);
            BatchFlushed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to flush telemetry batch.", ex);
        }
    }

    private static AnomalyAlert? EvaluateBaseline(
        Dictionary<AppKey, Queue<double>> historyMap,
        AppKey appKey,
        string displayName,
        AnomalyMetricKind metric,
        double value,
        DateTimeOffset nowUtc)
    {
        if (!historyMap.TryGetValue(appKey, out var queue))
        {
            queue = new Queue<double>(BaselineWindowSize + 1);
            historyMap[appKey] = queue;
        }

        AnomalyAlert? alert = null;
        if (queue.Count >= 6)
        {
            var snapshot = queue.ToArray();
            alert = AnomalyDetector.Evaluate(appKey, displayName, metric, value, snapshot, nowUtc);
        }

        if (queue.Count >= BaselineWindowSize)
        {
            queue.Dequeue();
        }

        queue.Enqueue(value);
        return alert;
    }

    private sealed class ActiveProcessState(ProcessInfo process, AppIdentity identity)
    {
        public ProcessInfo Process { get; } = process;
        public AppIdentity Identity { get; set; } = identity;
    }

    private sealed class SessionAccumulator(AppKey appKey, DateTimeOffset startUtc, DateTimeOffset endUtc, string? projectTag)
    {
        public AppKey AppKey { get; } = appKey;
        public DateTimeOffset StartUtc { get; } = startUtc;
        public DateTimeOffset EndUtc { get; set; } = endUtc;
        public long OpenSeconds;
        public long ActiveSeconds;
        public long OnScreenSeconds;
        public string? ProjectTag { get; set; } = projectTag;
    }

    private readonly record struct NetworkBucketKey(
        AppKey AppKey,
        string InterfaceName,
        NetworkInterfaceKind InterfaceKind,
        NetworkProtocol Protocol,
        string RemoteIp);

    private sealed class NetworkAccumulator(DateTimeOffset lastSeenUtc)
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; } = lastSeenUtc;
    }

    private sealed class HourlyUploadTracker
    {
        private readonly Queue<(DateTimeOffset TimestampUtc, long Bytes)> _events = new();
        public long TotalBytes { get; private set; }

        public void Add(DateTimeOffset timestampUtc, long bytes)
        {
            _events.Enqueue((timestampUtc, bytes));
            TotalBytes += bytes;
        }

        public void PruneOlderThan(DateTimeOffset cutoffUtc)
        {
            while (_events.TryPeek(out var head) && head.TimestampUtc < cutoffUtc)
            {
                _events.Dequeue();
                TotalBytes -= head.Bytes;
            }

            if (TotalBytes < 0)
            {
                TotalBytes = 0;
            }
        }
    }
}
