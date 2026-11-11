using System.Runtime.InteropServices;

namespace Radpa.Infrastructure.Interop;

internal static unsafe partial class PdhAndShellInterop
{
    internal const uint PdhFmtDouble = 0x00000200;
    internal const uint PdhFmtLarge = 0x00000400;
    internal const uint PdhMoreData = 0x800007D2;
    internal const uint PdhCstatusValidData = 0x00000000;
    internal const uint PdhCstatusNewData = 0x00000001;

    internal const uint NimAdd = 0x00000000;
    internal const uint NimModify = 0x00000001;
    internal const uint NimDelete = 0x00000002;
    internal const uint NifMessage = 0x00000001;
    internal const uint NifIcon = 0x00000002;
    internal const uint NifTip = 0x00000004;
    internal const uint NifInfo = 0x00000010;
    internal const uint NiifInfo = 0x00000001;
    internal const uint NiifWarning = 0x00000002;
    internal const uint NiifError = 0x00000003;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct PdhFmtCounterValue
    {
        [FieldOffset(0)]
        public uint CStatus;
        [FieldOffset(8)]
        public double DoubleValue;
        [FieldOffset(8)]
        public long LargeValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValueItemW
    {
        public nint SzName;
        public PdhFmtCounterValue FmtValue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconDataW
    {
        public uint CbSize;
        public nint HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        public fixed char SzTip[128];
        public uint DwState;
        public uint DwStateMask;
        public fixed char SzInfo[256];
        public uint UTimeoutOrVersion;
        public fixed char SzInfoTitle[64];
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhOpenQueryW(string? szDataSource, nuint dwUserData, out nint phQuery);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhAddEnglishCounterW(nint hQuery, string szFullCounterPath, nuint dwUserData, out nint phCounter);

    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCollectQueryData(nint hQuery);

    [LibraryImport("pdh.dll")]
    internal static partial uint PdhGetFormattedCounterArrayW(nint hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, void* itemBuffer);

    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCloseQuery(nint hQuery);

    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconDataW lpData);

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetBestInterfaceEx(byte* pDestAddr, out uint pdwBestIfIndex);
}
