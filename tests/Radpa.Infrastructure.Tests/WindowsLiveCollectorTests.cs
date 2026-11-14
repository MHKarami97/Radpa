using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Domain.ValueObjects;
using Radpa.Infrastructure.Collectors;
using Radpa.Infrastructure.Platform;
using Xunit;

namespace Radpa.Infrastructure.Tests;

[Trait("Category", "WindowsLive")]
public sealed class WindowsLiveCollectorTests
{
    [Fact]
    public void WindowsResourceAndProcessMonitorsCaptureCurrentProcessCleanly()
    {
        var pid = Environment.ProcessId;
        var sampler = new WindowsResourceSampler();
        var readings = new List<ProcessResourceReading>();

        // First call primes baseline, second measures non-negative delta
        sampler.SampleProcesses([pid], readings);
        readings.Should().ContainSingle(r => r.ProcessId == pid && r.WorkingSetBytes > 0);

        Thread.Sleep(60);
        readings.Clear();
        sampler.SampleProcesses([pid], readings);
        readings.Should().ContainSingle(r => r.ProcessId == pid && r.CpuPercent >= 0 && r.WorkingSetBytes > 0);
    }

    [Fact]
    public async Task WindowsProcessMonitorEnumeratesRunningWindowsProcesses()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = Substitute.For<IAppLogger>();
        await using var monitor = new WindowsProcessMonitor(new SystemClock(), logger);
        await monitor.StartAsync(ct);

        var procs = monitor.GetRunningProcesses();
        procs.Should().NotBeEmpty();
        procs.Should().Contain(p => p.ProcessId == Environment.ProcessId);

        await monitor.StopAsync(ct);
    }

    [Fact]
    public void WindowsGpuSamplerAndIdleProviderHandleSystemCapabilitiesWithoutThrowing()
    {
        var idle = new WindowsIdleTimeProvider().GetIdleDuration();
        idle.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

        using var gpu = new WindowsGpuSampler(Substitute.For<IAppLogger>());
        var dst = new Dictionary<int, ProcessGpuReading>();
        gpu.SampleProcesses(dst);

        // Must never fake values if unavailable, and must not throw either way
        if (!gpu.IsAvailable)
        {
            dst.Should().BeEmpty();
        }
    }

    [Fact]
    public void InterfaceClassifierRecognizesLoopbackAndSelfMetricsCapturesCurrentProcess()
    {
        var classifier = new InterfaceClassifier();
        var loopback = classifier.ResolveInterface("127.0.0.1");
        loopback.Kind.Should().Be(NetworkInterfaceKind.Loopback);

        var invalid = classifier.ResolveInterface("not-an-ip");
        invalid.Kind.Should().Be(NetworkInterfaceKind.Unknown);

        var selfMetrics = new SelfMetricsProvider().Capture();
        selfMetrics.WorkingSetBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WindowsElevationServiceResolvesHelperInEnvironment()
    {
        var logger = Substitute.For<IAppLogger>();
        var service = new WindowsElevationService(logger);
        service.IsHelperAvailable.Should().BeTrue();
        WindowsElevationService.HelperExecutablePath.Should().NotBeNullOrWhiteSpace();
        File.Exists(WindowsElevationService.HelperExecutablePath).Should().BeTrue();
    }
}
