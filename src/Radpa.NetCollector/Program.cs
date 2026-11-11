using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Radpa.NetCollector;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pipeName = GetArg(args, "--pipe");
        var allowedSidStr = GetArg(args, "--sid");
        var parentPidStr = GetArg(args, "--parent");

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.Error.WriteLine("Missing --pipe argument.");
            return 1;
        }

        var pipeSecurity = new PipeSecurity();
        if (!string.IsNullOrWhiteSpace(allowedSidStr))
        {
            var userSid = new SecurityIdentifier(allowedSidStr);
            pipeSecurity.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }
        else
        {
            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid is not null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(currentSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            }
        }

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        using var cts = new CancellationTokenSource();
        if (int.TryParse(parentPidStr, NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid) && parentPid > 0)
        {
            _ = WatchParentAsync(parentPid, cts);
        }

        try
        {
            using var collector = new EtwNetworkCollector();
            collector.Start();

            await using var server = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                65536,
                pipeSecurity);

            await server.WaitForConnectionAsync(cts.Token);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!cts.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token))
            {
                var frame = collector.DrainFrameBytes();
                if (frame is not null)
                {
                    await server.WriteAsync(frame, cts.Token);
                    await server.FlushAsync(cts.Token);
                }
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (IOException)
        {
            // Parent disconnected pipe
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Elevation required: {ex.Message}");
            return 5;
        }
    }

    private static async Task WatchParentAsync(int parentPid, CancellationTokenSource cts)
    {
        try
        {
            using var proc = Process.GetProcessById(parentPid);
            await proc.WaitForExitAsync(cts.Token);
        }
        catch
        {
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
