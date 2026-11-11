using FluentAssertions;
using Radpa.Domain.Entities;
using Radpa.Domain.Services;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Domain.Tests;

public sealed class SessionMergerTests
{
    [Fact]
    public void AccumulateTickWhenForegroundVisibleAndActiveIncrementsAllThreeCounters()
    {
        long open = 0;
        long active = 0;
        long onScreen = 0;

        SessionMerger.AccumulateTick(
            ref open,
            ref active,
            ref onScreen,
            elapsedSeconds: 5,
            isOpen: true,
            isForeground: true,
            isScreenLocked: false,
            isDisplayOff: false,
            idleDuration: TimeSpan.FromSeconds(10),
            idleThreshold: TimeSpan.FromSeconds(120));

        open.Should().Be(5);
        onScreen.Should().Be(5);
        active.Should().Be(5);
    }

    [Fact]
    public void AccumulateTickWhenForegroundVisibleButIdleExcludesActiveTimeOnly()
    {
        long open = 10;
        long active = 10;
        long onScreen = 10;

        SessionMerger.AccumulateTick(
            ref open,
            ref active,
            ref onScreen,
            elapsedSeconds: 4,
            isOpen: true,
            isForeground: true,
            isScreenLocked: false,
            isDisplayOff: false,
            idleDuration: TimeSpan.FromSeconds(120),
            idleThreshold: TimeSpan.FromSeconds(120));

        open.Should().Be(14);
        onScreen.Should().Be(14);
        active.Should().Be(10);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AccumulateTickWhenScreenLockedOrDisplayOffExcludesOnScreenAndActive(bool locked, bool displayOff)
    {
        long open = 0;
        long active = 0;
        long onScreen = 0;

        SessionMerger.AccumulateTick(
            ref open,
            ref active,
            ref onScreen,
            elapsedSeconds: 6,
            isOpen: true,
            isForeground: true,
            isScreenLocked: locked,
            isDisplayOff: displayOff,
            idleDuration: TimeSpan.Zero,
            idleThreshold: TimeSpan.FromMinutes(2));

        open.Should().Be(6);
        onScreen.Should().Be(0);
        active.Should().Be(0);
    }

    [Fact]
    public void AccumulateTickWhenBackgroundIncrementsOpenOnly()
    {
        long open = 0;
        long active = 0;
        long onScreen = 0;

        SessionMerger.AccumulateTick(
            ref open,
            ref active,
            ref onScreen,
            elapsedSeconds: 3,
            isOpen: true,
            isForeground: false,
            isScreenLocked: false,
            isDisplayOff: false,
            idleDuration: TimeSpan.Zero,
            idleThreshold: TimeSpan.FromMinutes(2));

        open.Should().Be(3);
        onScreen.Should().Be(0);
        active.Should().Be(0);
    }

    [Fact]
    public void AccumulateTickWhenClosedOrZeroElapsedDoesNothing()
    {
        long open = 7;
        long active = 5;
        long onScreen = 6;

        SessionMerger.AccumulateTick(ref open, ref active, ref onScreen, 5, false, true, false, false, TimeSpan.Zero, TimeSpan.FromMinutes(2));
        SessionMerger.AccumulateTick(ref open, ref active, ref onScreen, 0, true, true, false, false, TimeSpan.Zero, TimeSpan.FromMinutes(2));

        open.Should().Be(7);
        active.Should().Be(5);
        onScreen.Should().Be(6);
    }

    [Fact]
    public void MergeAdjacentCombinesContiguousSessionsAndPreservesProjectAndAppBoundaries()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var chrome = new AppKey("group:Google Chrome");
        var code = new AppKey("group:Visual Studio Code");

        var input = new[]
        {
            new UsageSession(2, chrome, t0.AddSeconds(25), t0.AddSeconds(50), 25, 20, 25, "Web"),
            new UsageSession(1, chrome, t0, t0.AddSeconds(20), 20, 15, 20, "Web"),
            new UsageSession(3, chrome, t0.AddSeconds(120), t0.AddSeconds(150), 30, 10, 20, "Web"),
            new UsageSession(4, chrome, t0.AddSeconds(152), t0.AddSeconds(160), 8, 8, 8, "Docs"),
            new UsageSession(5, code, t0, t0.AddSeconds(40), 40, 40, 40),
        };

        var merged = SessionMerger.MergeAdjacent(input, TimeSpan.FromSeconds(10));

        merged.Should().HaveCount(4);

        var firstChrome = merged[0];
        firstChrome.AppKey.Should().Be(chrome);
        firstChrome.StartUtc.Should().Be(t0);
        firstChrome.EndUtc.Should().Be(t0.AddSeconds(50));
        firstChrome.OpenSeconds.Should().Be(45);
        firstChrome.ActiveSeconds.Should().Be(35);
        firstChrome.OnScreenSeconds.Should().Be(45);
        firstChrome.ProjectTag.Should().Be("Web");

        merged[1].StartUtc.Should().Be(t0.AddSeconds(120));
        merged[2].ProjectTag.Should().Be("Docs");
        merged[3].AppKey.Should().Be(code);
    }

    [Fact]
    public void MergeAdjacentOnEmptyReturnsEmpty()
    {
        SessionMerger.MergeAdjacent([], TimeSpan.FromSeconds(5)).Should().BeEmpty();
    }
}
