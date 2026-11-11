using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using Radpa.Application.Abstractions;
using Radpa.Domain.ValueObjects;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Receives aggregated ETW network samples from the elevated Radpa.NetCollector.exe helper
/// over an ACL-secured Named Pipe (Fallback B from Gate B).
/// </summary>
public sealed class PipeNetworkCollector : INetworkCollector
{
    private readonly IElevationService _elevationService;
    private readonly InterfaceClassifier _classifier;
    private readonly IAppLogger _logger;
    private readonly string _pipeName = $"radpa-net-{Environment.UserName}-{Environment.ProcessId}";

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private volatile bool _active;

    public PipeNetworkCollector(
        IElevationService elevationService,
        InterfaceClassifier classifier,
        IAppLogger logger)
    {
        _elevationService = elevationService;
        _classifier = classifier;
        _logger = logger;
    }

    public string Name => "PipeNetworkCollector";
    public bool IsActive => _active;
    public string? LastError { get; private set; }

    public event EventHandler<IReadOnlyList<NetworkTrafficReading>>? TrafficBatchReceived;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<bool> EnableElevatedAsync(CancellationToken cancellationToken)
    {
        if (_active)
        {
            return true;
        }

        LastError = null;
        if (!_elevationService.IsHelperAvailable)
        {
            LastError = "Radpa.NetCollector.exe was not found next to the application.";
            return false;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenTask = Task.Run(() => RunPipeClientLoopAsync(_cts.Token), _cts.Token);

        var started = await _elevationService.StartNetworkHelperElevatedAsync(_pipeName, cancellationToken);
        if (!started)
        {
            LastError = "Administrator permission was declined or helper failed to start.";
            await DisableAsync(CancellationToken.None);
            return false;
        }

        _active = true;
        return true;
    }

    public async Task DisableAsync(CancellationToken cancellationToken)
    {
        _active = false;
        _elevationService.StopNetworkHelper();
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_listenTask is not null)
            {
                try
                {
                    await _listenTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _cts.Dispose();
            _cts = null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisableAsync(cancellationToken);

    public ValueTask DisposeAsync() => new(DisableAsync(CancellationToken.None));

    private async Task RunPipeClientLoopAsync(CancellationToken ct)
    {
        // Give the UAC-elevated helper a brief window to initialize its named pipe server
        for (var attempt = 0; attempt < 20 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500, ct);
                _logger.Info($"Connected to elevated network collector pipe '{_pipeName}'.");
                await ReadFramesAsync(pipe, ct);
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.Warning($"Network pipe reconnect attempt {attempt + 1}: {ex.Message}");
                await Task.Delay(300, ct);
            }
        }
    }

    private async Task ReadFramesAsync(Stream pipe, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(pipe, lenBuf, ct))
            {
                break;
            }

            var count = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (count <= 0 || count > 4096)
            {
                continue;
            }

            var readings = new List<NetworkTrafficReading>(count);
            var headerBuf = new byte[22];
            for (var i = 0; i < count; i++)
            {
                if (!await ReadExactAsync(pipe, headerBuf, ct))
                {
                    return;
                }

                var pid = BinaryPrimitives.ReadInt32LittleEndian(headerBuf.AsSpan(0, 4));
                var proto = (NetworkProtocol)headerBuf[4];
                var ipLen = headerBuf[5];
                var sent = BinaryPrimitives.ReadInt64LittleEndian(headerBuf.AsSpan(6, 8));
                var recv = BinaryPrimitives.ReadInt64LittleEndian(headerBuf.AsSpan(14, 8));

                var ipBytes = new byte[ipLen];
                if (!await ReadExactAsync(pipe, ipBytes, ct))
                {
                    return;
                }

                var remoteIp = Encoding.UTF8.GetString(ipBytes);
                var (ifaceName, ifaceKind) = _classifier.ResolveInterface(remoteIp);
                readings.Add(new NetworkTrafficReading(pid, ifaceName, ifaceKind, proto, remoteIp, sent, recv));
            }

            if (readings.Count > 0)
            {
                TrafficBatchReceived?.Invoke(this, readings);
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
