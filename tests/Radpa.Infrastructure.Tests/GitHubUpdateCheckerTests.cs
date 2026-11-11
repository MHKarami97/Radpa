using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using NSubstitute;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Domain.Services;
using Radpa.Infrastructure.Updates;
using Xunit;

namespace Radpa.Infrastructure.Tests;

public sealed class GitHubUpdateCheckerTests
{
    [Fact]
    public async Task CheckAsyncHandlesUpdateAvailableNotModifiedAndRateLimitBackoff()
    {
        var t0 = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var now = t0;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);

        var handler = new FakeHttpHandler();
        using var http = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(http, new UpdateCheckOptions { Enabled = true, Repository = "radpa-app/radpa" }, clock);
        var current = new SemanticVersion(1, 0, 0);

        // 1. 200 OK with newer release and ETag
        handler.NextResponse = _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v1.2.0","html_url":"https://github.com/radpa-app/radpa/releases/tag/v1.2.0","body":"Changes","draft":false,"prerelease":false}""", Encoding.UTF8, "application/json"),
            };
            resp.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return resp;
        };

        var res1 = await checker.CheckAsync(current, null, CancellationToken.None);
        res1.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        res1.LatestVersion.Should().Be(new SemanticVersion(1, 2, 0));
        res1.ETag.Should().Be("\"abc\"");
        handler.LastRequest!.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
        handler.LastRequest!.Headers.GetValues("X-GitHub-Api-Version").Should().Contain("2022-11-28");

        // 2. 304 Not Modified sends If-None-Match
        handler.NextResponse = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        var res2 = await checker.CheckAsync(current, "\"abc\"", CancellationToken.None);
        res2.Status.Should().Be(UpdateCheckStatus.NotModified);
        handler.LastRequest!.Headers.IfNoneMatch.ToString().Should().Contain("\"abc\"");

        // 3. 429 triggers backoff window
        handler.NextResponse = _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return resp;
        };

        var callsBefore = handler.CallCount;
        var res3 = await checker.CheckAsync(current, "\"abc\"", CancellationToken.None);
        res3.Status.Should().Be(UpdateCheckStatus.RateLimited);
        res3.RetryAfterUtc.Should().BeCloseTo(t0.AddMinutes(10), TimeSpan.FromSeconds(1));

        // 4. Subsequent check within backoff does not hit network
        var res4 = await checker.CheckAsync(current, "\"abc\"", CancellationToken.None);
        res4.Status.Should().Be(UpdateCheckStatus.RateLimited);
        handler.CallCount.Should().Be(callsBefore + 1);
    }

    [Fact]
    public async Task CheckAsyncHandlesOfflineExceptionGracefully()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        var handler = new FakeHttpHandler
        {
            NextResponse = _ => throw new HttpRequestException("Network unreachable"),
        };
        using var http = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(http, new UpdateCheckOptions { Enabled = true, Repository = "radpa-app/radpa" }, clock);

        var res = await checker.CheckAsync(new SemanticVersion(1, 0, 0), null, CancellationToken.None);
        res.Status.Should().Be(UpdateCheckStatus.OfflineOrFailed);
        res.ErrorMessage.Should().Contain("Network unreachable");
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? NextResponse { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(NextResponse!(request));
        }
    }
}
