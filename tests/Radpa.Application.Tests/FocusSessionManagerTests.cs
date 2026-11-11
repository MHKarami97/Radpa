using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Engine;
using Radpa.Domain.Entities;
using Radpa.Domain.ValueObjects;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class FocusSessionManagerTests
{
    [Fact]
    public void StartTracksFocusedAndDistractingAppsAndCompletesAtPlannedEnd()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var now = t0;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);

        var notifications = Substitute.For<INotificationService>();
        var manager = new FocusSessionManager(clock, notifications);

        FocusSession? completed = null;
        manager.SessionCompleted += (_, s) => completed = s;

        var ide = new AppIdentity(new AppKey("exe:code"), "Visual Studio Code", @"C:\VSCode\Code.exe");
        var youtube = new AppIdentity(new AppKey("exe:chrome"), "Chrome YouTube", @"C:\Chrome\chrome.exe");

        manager.Start(TimeSpan.FromMinutes(25), ["chrome"]);
        manager.IsActive.Should().BeTrue();

        now = t0.AddMinutes(10);
        manager.RecordForegroundActivity(ide, 600, now);

        now = t0.AddMinutes(15);
        manager.RecordForegroundActivity(youtube, 300, now);

        var mid = manager.GetCurrentSnapshot();
        mid.Should().NotBeNull();
        mid!.FocusedSeconds.Should().Be(600);
        mid.DistractedSeconds.Should().Be(300);

        now = t0.AddMinutes(25);
        manager.RecordForegroundActivity(ide, 600, now);

        manager.IsActive.Should().BeFalse();
        completed.Should().NotBeNull();
        completed!.FocusedSeconds.Should().Be(1200);
        completed.DistractedSeconds.Should().Be(300);
        completed.DistractionBreakdownSeconds["Chrome YouTube"].Should().Be(300);
        notifications.Received(1).ShowNotification(Arg.Any<string>(), Arg.Any<string>(), NotificationSeverity.Info);
    }

    [Fact]
    public void StopReturnsSummaryAndZeroDurationThrows()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        var manager = new FocusSessionManager(clock, Substitute.For<INotificationService>());

        var act = () => manager.Start(TimeSpan.Zero, []);
        act.Should().Throw<ArgumentOutOfRangeException>();

        manager.Stop().Should().BeNull();
        manager.Start(TimeSpan.FromMinutes(10), []);
        manager.Stop().Should().NotBeNull();
        manager.IsActive.Should().BeFalse();
    }
}
