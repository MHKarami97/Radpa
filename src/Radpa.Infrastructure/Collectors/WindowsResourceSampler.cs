using System.Diagnostics;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

/// <summary>
/// Samples per-process CPU %, RAM (Working Set / Private Bytes), and Disk Read/Write deltas
/// using direct Win32 kernel calls with zero-allocation baseline tracking.
/// </summary>
public sealed unsafe class WindowsResourceSampler : IResourceSampler
{
    private readonly Dictionary<int, ProcessBaseline> _baselines = [];
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);

    public void SampleProcesses(IReadOnlyList<int> processIds, List<ProcessResourceReading> destination)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        ArgumentNullException.ThrowIfNull(destination);

        var nowTicks = Stopwatch.GetTimestamp();
        var activeSet = new HashSet<int>(processIds.Count);

        for (var i = 0; i < processIds.Count; i++)
        {
            var pid = processIds[i];
            activeSet.Add(pid);

            var handle = Kernel32Interop.OpenProcess(Kernel32Interop.ProcessQueryLimitedInformation, false, (uint)pid);
            if (handle == 0)
            {
                continue;
            }

            try
            {
                if (!Kernel32Interop.GetProcessTimes(handle, out _, out _, out var kernelTime, out var userTime))
                {
                    continue;
                }

                var cpuTicks100Ns = kernelTime.ToInt64() + userTime.ToInt64();

                var memCounters = new Kernel32Interop.ProcessMemoryCountersEx
                {
                    Cb = (uint)sizeof(Kernel32Interop.ProcessMemoryCountersEx),
                };
                long workingSet = 0;
                long privateBytes = 0;
                if (Kernel32Interop.K32GetProcessMemoryInfo(handle, out memCounters, memCounters.Cb))
                {
                    workingSet = (long)memCounters.WorkingSetSize;
                    privateBytes = (long)memCounters.PrivateUsage;
                }

                ulong readBytes = 0;
                ulong writeBytes = 0;
                if (Kernel32Interop.GetProcessIoCounters(handle, out var io))
                {
                    readBytes = io.ReadTransferCount;
                    writeBytes = io.WriteTransferCount;
                }

                double cpuPercent = 0;
                long diskReadDelta = 0;
                long diskWriteDelta = 0;

                if (_baselines.TryGetValue(pid, out var prev))
                {
                    var wallSeconds = (double)(nowTicks - prev.StopwatchTimestamp) / Stopwatch.Frequency;
                    if (wallSeconds > 0.05)
                    {
                        var cpuDeltaSeconds = (cpuTicks100Ns - prev.Cpu100Ns) / 10_000_000.0;
                        if (cpuDeltaSeconds >= 0)
                        {
                            cpuPercent = Math.Clamp((cpuDeltaSeconds / (wallSeconds * _processorCount)) * 100.0, 0.0, 100.0);
                        }
                    }

                    if (readBytes >= prev.ReadBytes)
                    {
                        diskReadDelta = (long)(readBytes - prev.ReadBytes);
                    }

                    if (writeBytes >= prev.WriteBytes)
                    {
                        diskWriteDelta = (long)(writeBytes - prev.WriteBytes);
                    }
                }

                _baselines[pid] = new ProcessBaseline(nowTicks, cpuTicks100Ns, readBytes, writeBytes);
                destination.Add(new ProcessResourceReading(pid, cpuPercent, workingSet, privateBytes, diskReadDelta, diskWriteDelta));
            }
            finally
            {
                Kernel32Interop.CloseHandle(handle);
            }
        }

        // Prune exited PIDs so dictionary does not leak
        if (_baselines.Count > activeSet.Count + 64)
        {
            var stale = new List<int>();
            foreach (var key in _baselines.Keys)
            {
                if (!activeSet.Contains(key))
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                _baselines.Remove(key);
            }
        }
    }

    private readonly record struct ProcessBaseline(
        long StopwatchTimestamp,
        long Cpu100Ns,
        ulong ReadBytes,
        ulong WriteBytes);
}
