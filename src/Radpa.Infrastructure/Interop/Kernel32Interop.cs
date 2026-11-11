using System.Runtime.InteropServices;

namespace Radpa.Infrastructure.Interop;

internal static unsafe partial class Kernel32Interop
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint Synchronize = 0x00100000;
    internal const uint Th32csSnapProcess = 0x00000002;
    internal const uint WtExecuteOnlyOnce = 0x00000008;
    internal const uint WtExecuteInWaitThread = 0x00000004;
    internal const uint Infinite = 0xFFFFFFFF;
    internal const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public long ToInt64() => ((long)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessMemoryCountersEx
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessEntry32W
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public nuint Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessId;
        public int PcPriClassBase;
        public uint DwFlags;
        public fixed char SzExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(nint hProcess, out FileTime lpCreationTime, out FileTime lpExitTime, out FileTime lpKernelTime, out FileTime lpUserTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessIoCounters(nint hProcess, out IoCounters lpIoCounters);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool K32GetProcessMemoryInfo(nint process, out ProcessMemoryCountersEx ppsmemCounters, uint cb);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32FirstW(nint hSnapshot, ref ProcessEntry32W lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32NextW(nint hSnapshot, ref ProcessEntry32W lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int GetPackageFamilyName(nint hProcess, ref uint packageFamilyNameLength, char* packageFamilyName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterWaitForSingleObject(
        out nint phNewWaitObject,
        nint hObject,
        delegate* unmanaged[Stdcall]<nint, byte, void> callback,
        nint context,
        uint dwMilliseconds,
        uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterWaitEx(nint waitHandle, nint completionEvent);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetTickCount();

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();
}
