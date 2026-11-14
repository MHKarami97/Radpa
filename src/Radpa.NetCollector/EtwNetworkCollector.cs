using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Radpa.NetCollector;

internal sealed class EtwNetworkCollector : IDisposable
{
    // Microsoft-Windows-Kernel-Network
    private static readonly Guid KernelNetworkProviderGuid = new("7dd42a49-5329-4832-8dfd-43d979153a88");
    private const ulong KeywordIpv4 = 0x10;
    private const ulong KeywordIpv6 = 0x20;

    private readonly ConcurrentDictionary<FlowKey, FlowBytes> _buckets = new();
    private TraceEventSession? _session;
    private Task? _processTask;

    public void Start()
    {
        var sessionName = $"RadpaNetCollector_{Environment.ProcessId}";
        _session = new TraceEventSession(sessionName)
        {
            StopOnDispose = true,
        };

        _session.EnableProvider(KernelNetworkProviderGuid, TraceEventLevel.Informational);
        _session.Source.Dynamic.All += OnEvent;
        _processTask = Task.Run(() => _session.Source.Process());
    }

    public byte[]? DrainFrameBytes()
    {
        if (_buckets.IsEmpty)
        {
            return null;
        }

        var snapshot = new List<(FlowKey Key, long Sent, long Recv)>();
        foreach (var kv in _buckets)
        {
            var sent = Interlocked.Exchange(ref kv.Value.Sent, 0);
            var recv = Interlocked.Exchange(ref kv.Value.Received, 0);
            if (sent == 0 && recv == 0)
            {
                _buckets.TryRemove(kv.Key, out _);
                continue;
            }

            snapshot.Add((kv.Key, sent, recv));
        }

        if (snapshot.Count == 0)
        {
            return null;
        }

        using var ms = new MemoryStream(4 + (snapshot.Count * 40));
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, snapshot.Count);
        ms.Write(lenBuf);

        Span<byte> header = stackalloc byte[22];
        foreach (var (key, sent, recv) in snapshot)
        {
            var ipBytes = Encoding.UTF8.GetBytes(key.RemoteIp);
            var ipLen = (byte)Math.Min(255, ipBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], key.Pid);
            header[4] = key.Protocol;
            header[5] = ipLen;
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(6, 8), sent);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(14, 8), recv);
            ms.Write(header);
            ms.Write(ipBytes.AsSpan(0, ipLen));
        }

        return ms.ToArray();
    }

    public void Dispose()
    {
        try
        {
            _session?.Stop();
            _processTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        finally
        {
            _session?.Dispose();
            _session = null;
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if (data.ProviderGuid != KernelNetworkProviderGuid)
        {
            return;
        }

        var id = (int)data.ID;
        var opcode = (int)data.Opcode;
        var eventName = data.EventName ?? string.Empty;

        var isSend = id is 10 or 26 or 42 or 58 ||
                     opcode is 10 or 42 ||
                     eventName.Contains("Send", StringComparison.OrdinalIgnoreCase);

        var isRecv = !isSend && (
                     id is 11 or 27 or 43 or 59 ||
                     opcode is 11 or 43 ||
                     eventName.Contains("Recv", StringComparison.OrdinalIgnoreCase) ||
                     eventName.Contains("Receiv", StringComparison.OrdinalIgnoreCase));

        if (!isSend && !isRecv)
        {
            return;
        }

        byte protocol = (id is 42 or 43 or 58 or 59 || opcode is 42 or 43 || eventName.Contains("Udp", StringComparison.OrdinalIgnoreCase))
            ? (byte)2
            : (id is 10 or 11 or 26 or 27 || opcode is 10 or 11 || eventName.Contains("Tcp", StringComparison.OrdinalIgnoreCase))
                ? (byte)1
                : (byte)0;

        if (protocol == 0)
        {
            return;
        }

        var pid = ExtractPid(data);
        var size = ExtractSize(data);
        if (pid <= 0 || size <= 0)
        {
            return;
        }

        var remoteIp = ExtractIp(data, isSend ? "daddr" : "saddr")
            ?? ExtractIp(data, isSend ? "saddr" : "daddr")
            ?? "0.0.0.0";

        var key = new FlowKey(pid, protocol, remoteIp);
        var bucket = _buckets.GetOrAdd(key, static _ => new FlowBytes());
        if (isSend)
        {
            Interlocked.Add(ref bucket.Sent, size);
        }
        else
        {
            Interlocked.Add(ref bucket.Received, size);
        }
    }

    private static int ExtractPid(TraceEvent data)
    {
        if (data.PayloadByName("PID") is int p && p > 0)
        {
            return p;
        }

        if (data.PayloadByName("PID") is uint up && up > 0)
        {
            return (int)up;
        }

        return data.ProcessID;
    }

    private static long ExtractSize(TraceEvent data) =>
        data.PayloadByName("size") switch
        {
            int i => i,
            uint u => u,
            long l => l,
            _ => 0,
        };

    private static string? ExtractIp(TraceEvent data, string fieldName)
    {
        var raw = data.PayloadByName(fieldName);
        return raw switch
        {
            IPAddress ip => ip.ToString(),
            byte[] bytes when bytes.Length is 4 or 16 => new IPAddress(bytes).ToString(),
            uint addr32 => new IPAddress(addr32).ToString(),
            int addrI32 => new IPAddress(unchecked((uint)addrI32)).ToString(),
            string s when !string.IsNullOrWhiteSpace(s) => s,
            _ => null,
        };
    }

    private readonly record struct FlowKey(int Pid, byte Protocol, string RemoteIp);

    private sealed class FlowBytes
    {
        public long Sent;
        public long Received;
    }
}
