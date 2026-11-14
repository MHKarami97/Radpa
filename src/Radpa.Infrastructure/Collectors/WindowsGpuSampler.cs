using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Queries Windows WDDM "GPU Engine" and "GPU Process Memory" counters via PDH English paths.
/// Reports IsAvailable=false cleanly on systems/VMs without WDDM GPU counters (never fabricates data).
/// </summary>
public sealed unsafe class WindowsGpuSampler : IGpuSampler
{
    private readonly IAppLogger _logger;
    private nint _queryHandle;
    private nint _engineCounter;
    private nint _memoryCounter;
    private bool _hasPrimed;
    private bool _disposed;

    public WindowsGpuSampler(IAppLogger logger)
    {
        _logger = logger;
        InitializePdh();
    }

    public bool IsAvailable => _queryHandle != 0 && (_engineCounter != 0 || _memoryCounter != 0);

    public void SampleProcesses(Dictionary<int, ProcessGpuReading> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!IsAvailable)
        {
            return;
        }

        var status = PdhAndShellInterop.PdhCollectQueryData(_queryHandle);
        if (status != 0)
        {
            return;
        }

        if (!_hasPrimed)
        {
            _hasPrimed = true;
            return;
        }

        var gpuSums = new Dictionary<int, double>();
        var vramSums = new Dictionary<int, long>();

        if (_engineCounter != 0)
        {
            ReadCounterItems(_engineCounter, PdhAndShellInterop.PdhFmtDouble, gpuSums, static (ref double acc, PdhAndShellInterop.PdhFmtCounterValue val) =>
            {
                acc += val.DoubleValue;
            });
        }

        if (_memoryCounter != 0)
        {
            ReadCounterItems(_memoryCounter, PdhAndShellInterop.PdhFmtLarge, vramSums, static (ref long acc, PdhAndShellInterop.PdhFmtCounterValue val) =>
            {
                acc += val.LargeValue;
            });
        }

        foreach (var (pid, pct) in gpuSums)
        {
            long? vram = vramSums.TryGetValue(pid, out var mem) ? mem : null;
            destination[pid] = new ProcessGpuReading(pid, Math.Clamp(pct, 0.0, 100.0), vram);
        }

        foreach (var (pid, mem) in vramSums)
        {
            if (!destination.ContainsKey(pid))
            {
                destination[pid] = new ProcessGpuReading(pid, null, mem);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_queryHandle != 0)
        {
            if (PdhAndShellInterop.PdhCloseQuery(_queryHandle) != 0)
            {
            }
            _queryHandle = 0;
        }
    }

    private void InitializePdh()
    {
        try
        {
            if (PdhAndShellInterop.PdhOpenQueryW(null, 0, out _queryHandle) != 0 || _queryHandle == 0)
            {
                _queryHandle = 0;
                return;
            }

            if (PdhAndShellInterop.PdhAddEnglishCounterW(_queryHandle, @"\GPU Engine(*)\Utilization Percentage", 0, out _engineCounter) != 0)
            {
                _engineCounter = 0;
            }

            if (PdhAndShellInterop.PdhAddEnglishCounterW(_queryHandle, @"\GPU Process Memory(*)\Local Usage", 0, out _memoryCounter) != 0)
            {
                _memoryCounter = 0;
            }

            if (_engineCounter == 0 && _memoryCounter == 0)
            {
                if (PdhAndShellInterop.PdhCloseQuery(_queryHandle) != 0)
                {
                }
                _queryHandle = 0;
                _logger.Info("PDH GPU counters not exposed by graphics driver; GPU metrics will show N/A.");
            }
            else
            {
                if (PdhAndShellInterop.PdhCollectQueryData(_queryHandle) != 0)
                {
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to initialize PDH GPU counters: {ex.Message}");
            if (_queryHandle != 0)
            {
                if (PdhAndShellInterop.PdhCloseQuery(_queryHandle) != 0)
                {
                }
                _queryHandle = 0;
            }
        }
    }

    private delegate void AccumulatorAction<T>(ref T accumulator, PdhAndShellInterop.PdhFmtCounterValue value);

    private static void ReadCounterItems<T>(
        nint counterHandle,
        uint format,
        Dictionary<int, T> output,
        AccumulatorAction<T> accumulate)
        where T : struct
    {
        uint bufferSize = 0;
        var status = PdhAndShellInterop.PdhGetFormattedCounterArrayW(counterHandle, format, ref bufferSize, out var itemCount, null);
        if (status != PdhAndShellInterop.PdhMoreData || bufferSize == 0)
        {
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent((int)bufferSize);
        try
        {
            fixed (byte* ptr = rented)
            {
                var size = (uint)rented.Length;
                status = PdhAndShellInterop.PdhGetFormattedCounterArrayW(counterHandle, format, ref size, out itemCount, ptr);
                if (status != 0 || itemCount == 0)
                {
                    return;
                }

                var items = (PdhAndShellInterop.PdhFmtCounterValueItemW*)ptr;
                for (uint i = 0; i < itemCount; i++)
                {
                    var item = items[i];
                    if (item.FmtValue.CStatus is not (PdhAndShellInterop.PdhCstatusValidData or PdhAndShellInterop.PdhCstatusNewData))
                    {
                        continue;
                    }

                    if (item.SzName == 0)
                    {
                        continue;
                    }

                    var instanceSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.SzName);
                    if (!TryParsePidFromInstance(instanceSpan, out var pid))
                    {
                        continue;
                    }

                    ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(output, pid, out _);
                    accumulate(ref slot, item.FmtValue);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal static bool TryParsePidFromInstance(ReadOnlySpan<char> instanceName, out int pid)
    {
        pid = 0;
        if (!instanceName.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = instanceName[4..];
        var nextUnderscore = rest.IndexOf('_');
        var digits = nextUnderscore >= 0 ? rest[..nextUnderscore] : rest;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }
}
