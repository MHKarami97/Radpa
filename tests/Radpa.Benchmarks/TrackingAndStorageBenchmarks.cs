using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Application.Options;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Radpa.Infrastructure.Persistence;

namespace Radpa.Benchmarks;

[MemoryDiagnoser]
public sealed class TrackingAndStorageBenchmarks : IDisposable
{
    private string _tempDir = string.Empty;
    private SqliteUsageRepository _repository = null!;
    private TelemetryFlushBatch _sampleBatch = null!;
    private TrackingCoordinator _coordinator = null!;
    private DateTimeOffset _tickTime = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [GlobalSetup]
    public async Task Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"radpa-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _repository = new SqliteUsageRepository(Path.Combine(_tempDir, "bench.db"));
        await _repository.InitializeAsync(CancellationToken.None);

        var apps = new List<AppIdentity>(50);
        var sessions = new List<UsageSession>(50);
        var resources = new List<ResourceSample>(100);
        var net = new List<NetworkSample>(50);

        for (var i = 0; i < 50; i++)
        {
            var key = new AppKey($"exe:app{i}");
            apps.Add(new AppIdentity(key, $"App {i}", $@"C:\Apps\app{i}.exe"));
            sessions.Add(new UsageSession(0, key, _tickTime, _tickTime.AddSeconds(30), 30, i == 0 ? 30 : 0, i == 0 ? 30 : 0));
            resources.Add(new ResourceSample(key, _tickTime, 1.5, 50_000_000, 40_000_000, 1024, 512, null, null));
            resources.Add(new ResourceSample(key, _tickTime.AddSeconds(2), 1.8, 51_000_000, 40_500_000, 512, 256, null, null));
            net.Add(new NetworkSample(key, _tickTime, "Wi-Fi", NetworkInterfaceKind.WiFi, NetworkProtocol.Tcp, "1.1.1.1", "one.one.one.one", 1024, 4096));
        }

        _sampleBatch = new TelemetryFlushBatch(apps, sessions, resources, net);

        var clock = new FixedBenchClock(_tickTime);
        var procMon = new BenchProcessMonitor(100, _tickTime);
        var fgMon = new BenchForegroundMonitor(_tickTime);
        var idle = new BenchIdleProvider();
        var sys = new BenchSysState();
        var sampler = new BenchResourceSampler(100);
        var gpu = new BenchGpuSampler();
        var netCol = new BenchNetworkCollector();
        var enc = new BenchEncryption();
        var dns = new BenchDns();
        var notif = new BenchNotifier();
        var logger = new BenchLogger();
        var focus = new FocusSessionManager(clock, notif);

        _coordinator = new TrackingCoordinator(
            clock, procMon, fgMon, idle, sys, sampler, gpu, netCol,
            _repository, enc, dns, notif, logger, focus,
            new AppSettings { SamplingIntervalSeconds = 60, FlushIntervalSeconds = 120 });

        await _coordinator.StartAsync(CancellationToken.None);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
            _coordinator = null!;
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    public void Dispose() => Cleanup().GetAwaiter().GetResult();

    [Benchmark]
    public void CoordinatorTick100Processes()
    {
        _tickTime = _tickTime.AddSeconds(2);
        _coordinator.ProcessTickForTests(_tickTime);
    }

    [Benchmark]
    public Task SqliteSaveBatch200Samples() =>
        _repository.SaveBatchAsync(_sampleBatch, CancellationToken.None);

    private sealed class FixedBenchClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public DateTimeOffset LocalNow { get; } = now.ToLocalTime();
    }

    private sealed class BenchProcessMonitor : IProcessMonitor
    {
        private readonly ProcessInfo[] _procs;

        public BenchProcessMonitor(int count, DateTimeOffset start)
        {
            _procs = new ProcessInfo[count];
            for (var i = 0; i < count; i++)
            {
                var exe = i < 15 ? @"C:\Program Files\Google\Chrome\Application\chrome.exe" : $@"C:\Apps\app{i}.exe";
                _procs[i] = new ProcessInfo(1000 + i, exe, null, null, start);
            }
        }

        public string Name => "Bench";
        public event EventHandler<ProcessInfo>? ProcessStarted { add { } remove { } }
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public IReadOnlyList<ProcessInfo> GetRunningProcesses() => _procs;
    }

    private sealed class BenchForegroundMonitor(DateTimeOffset now) : IForegroundMonitor
    {
        public string Name => "BenchFg";
        public event EventHandler<ForegroundAppInfo>? ForegroundChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ForegroundAppInfo? GetCurrentForeground() => new ForegroundAppInfo(1000, null, null, now);
    }

    private sealed class BenchIdleProvider : IIdleTimeProvider
    {
        public TimeSpan GetIdleDuration() => TimeSpan.Zero;
    }

    private sealed class BenchSysState : ISystemStateMonitor
    {
        public string Name => "BenchSys";
        public bool IsScreenLocked => false;
        public bool IsDisplayOff => false;
        public event EventHandler? StateChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BenchResourceSampler(int count) : IResourceSampler
    {
        public void SampleProcesses(IReadOnlyList<int> processIds, List<ProcessResourceReading> destination)
        {
            for (var i = 0; i < count; i++)
            {
                destination.Add(new ProcessResourceReading(1000 + i, 0.5, 40_000_000, 30_000_000, 0, 0));
            }
        }
    }

    private sealed class BenchGpuSampler : IGpuSampler
    {
        public bool IsAvailable => false;
        public void SampleProcesses(Dictionary<int, ProcessGpuReading> destination) { }
        public void Dispose() { }
    }

    private sealed class BenchNetworkCollector : INetworkCollector
    {
        public string Name => "BenchNet";
        public bool IsActive => false;
        public string? LastError => null;
        public event EventHandler<IReadOnlyList<NetworkTrafficReading>>? TrafficBatchReceived { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> EnableElevatedAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task DisableAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BenchEncryption : IEncryptionService
    {
        public bool IsEnabled => false;
        public void Configure(bool enabled, byte[]? protectedKeyBlob) { }
        public byte[] GenerateProtectedKeyBlob() => [];
        public string? EncryptField(string? plaintext) => plaintext;
        public string? DecryptField(string? ciphertext) => ciphertext;
    }

    private sealed class BenchDns : IDnsResolverCache
    {
        public string? TryGetCachedHost(string ipAddress) => null;
        public void QueueResolve(string ipAddress) { }
    }

    private sealed class BenchNotifier : INotificationService
    {
        public void ShowNotification(string title, string message, NotificationSeverity severity = NotificationSeverity.Info) { }
    }

    private sealed class BenchLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void LogError(string message, Exception? exception = null) { }
    }
}
