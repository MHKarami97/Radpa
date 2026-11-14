using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Application.Updates;
using Radpa.Domain.Services;
using Xunit;

namespace Radpa.Application.Tests;

public sealed class UpdateCoordinatorTests
{
    [Fact]
    public async Task CheckIfDueSkipsInsideIntervalWhenDisabledOrTooRecent()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(t0);

        var checker = Substitute.For<IUpdateChecker>();
        var store = Substitute.For<ISettingsStore>();
        var notify = Substitute.For<INotificationService>();
        var current = new SemanticVersion(1, 0, 0);

        var sut = new UpdateCoordinator(checker, clock, store, notify);

        var disabledSettings = new AppSettings { UpdateCheck = new UpdateCheckOptions { Enabled = false } };
        var ct = TestContext.Current.CancellationToken;
        var (resDisabled, _) = await sut.CheckIfDueAsync(disabledSettings, current, force: false, ct);
        resDisabled.Status.Should().Be(UpdateCheckStatus.Disabled);

        var recentSettings = new AppSettings
        {
            UpdateCheck = new UpdateCheckOptions { Enabled = true, IntervalHours = 24 },
            LastUpdateCheckUtc = t0.AddHours(-2),
            LastUpdateETag = "etag-1",
        };
        var (resRecent, _) = await sut.CheckIfDueAsync(recentSettings, current, force: false, ct);
        resRecent.Status.Should().Be(UpdateCheckStatus.NotModified);

        await checker.DidNotReceiveWithAnyArgs().CheckAsync(default, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckIfDueForcesUpdateCheckNotifiesAndHonorsSkippedVersion()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var current = new SemanticVersion(1, 0, 0);
        var next = new SemanticVersion(1, 1, 0);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(t0);

        var checker = Substitute.For<IUpdateChecker>();
        checker.CheckAsync(current, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                current,
                next,
                "https://github.com/MHKarami97/Radpa/releases/tag/v1.1.0",
                "Notes",
                "etag-2",
                null));

        var store = Substitute.For<ISettingsStore>();
        var notify = Substitute.For<INotificationService>();
        var sut = new UpdateCoordinator(checker, clock, store, notify);

        var settings = new AppSettings
        {
            UpdateCheck = new UpdateCheckOptions { Enabled = true, IntervalHours = 24 },
            LastUpdateETag = "etag-1",
            LastUpdateCheckUtc = t0.AddHours(-25),
        };

        var (res, updated) = await sut.CheckIfDueAsync(settings, current, force: false, TestContext.Current.CancellationToken);
        res.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        updated.LastUpdateETag.Should().Be("etag-2");
        updated.LastUpdateCheckUtc.Should().Be(t0);
        notify.Received(1).ShowNotification(Arg.Any<string>(), Arg.Any<string>(), NotificationSeverity.Info);

        // Skipped version must suppress notification even when forced
        notify.ClearReceivedCalls();
        var skippedSettings = updated with { SkippedUpdateVersion = "1.1.0" };
        await sut.CheckIfDueAsync(skippedSettings, current, force: true, TestContext.Current.CancellationToken);
        notify.DidNotReceive().ShowNotification(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationSeverity>());
    }
}
