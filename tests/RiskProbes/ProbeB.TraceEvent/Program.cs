using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace ProbeB.TraceEvent;

internal static class Program
{
    private static readonly Guid KernelNetworkProviderGuid = new("7dd42a49-5329-4832-8dfd-43d979153a88");

    public static int Main(string[] args)
    {
        var isElevated = IsAdministrator();
        Console.WriteLine($"ProbeB starting. Elevated={isElevated}, Is64Bit={Environment.Is64BitProcess}");

        if (!isElevated)
        {
            Console.WriteLine("Not elevated: verifying TraceEventSession throws UnauthorizedAccessException gracefully...");
            try
            {
                using var unprivileged = new TraceEventSession("RadpaProbeBUnelevated");
                unprivileged.EnableProvider(KernelNetworkProviderGuid);
                Console.WriteLine("Unexpectedly succeeded without elevation.");
                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"EXPECTED_UNELEVATED: {ex.GetType().Name}: {ex.Message}");
                return 0;
            }
        }

        long tcpEvents = 0;
        long udpEvents = 0;
        long bytesTotal = 0;

        using var session = new TraceEventSession("RadpaProbeBNetworkSession");
        session.EnableProvider(KernelNetworkProviderGuid);

        session.Source.Dynamic.All += data =>
        {
            if (data.ProviderGuid != KernelNetworkProviderGuid)
            {
                return;
            }

            var name = data.EventName;
            if (name.Contains("Tcp", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref tcpEvents);
            }
            else if (name.Contains("Udp", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref udpEvents);
            }

            if (data.PayloadByName("size") is int size)
            {
                Interlocked.Add(ref bytesTotal, size);
            }
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(args.Contains("--quick") ? 2 : 4));
        var processingTask = Task.Run(() => session.Source.Process());

        // Generate minimal local UDP/TCP traffic to self so events fire if elevated
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            udp.Send([1, 2, 3, 4], 4, new IPEndPoint(IPAddress.Loopback, 59999));
        }
        catch (System.Net.Sockets.SocketException)
        {
        }

        cts.Token.WaitHandle.WaitOne();
        session.Stop();
        processingTask.Wait(TimeSpan.FromSeconds(2));

        Console.WriteLine($"PROBE_B_ELEVATED tcp={tcpEvents} udp={udpEvents} bytes={bytesTotal}");
        return 0;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
