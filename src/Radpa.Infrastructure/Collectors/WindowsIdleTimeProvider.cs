using System.Runtime.InteropServices;
using Radpa.Application.Abstractions;
using Radpa.Infrastructure.Interop;

namespace Radpa.Infrastructure.Collectors;

public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan GetIdleDuration()
    {
        var lii = new User32Interop.LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<User32Interop.LastInputInfo>(),
        };

        if (!User32Interop.GetLastInputInfo(ref lii))
        {
            return TimeSpan.Zero;
        }

        var tickNow = Kernel32Interop.GetTickCount();
        var idleMs = unchecked(tickNow - lii.DwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
