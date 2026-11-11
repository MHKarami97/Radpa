using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Radpa.Application.Abstractions;
using Radpa.Application.Options;
using Radpa.Domain.Services;

namespace Radpa.Infrastructure.Updates;

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}

[JsonSerializable(typeof(GitHubReleaseDto))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly UpdateCheckOptions _options;
    private readonly IClock _clock;
    private DateTimeOffset? _backoffUntilUtc;

    public GitHubUpdateChecker(HttpClient http, UpdateCheckOptions options, IClock clock)
    {
        _http = http;
        _options = options;
        _clock = clock;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        SemanticVersion currentVersion,
        string? lastETag,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Repository))
        {
            return new UpdateCheckResult(UpdateCheckStatus.Disabled, currentVersion, null, null, null, lastETag, null);
        }

        var now = _clock.UtcNow;
        if (_backoffUntilUtc is not null && now < _backoffUntilUtc.Value)
        {
            return new UpdateCheckResult(UpdateCheckStatus.RateLimited, currentVersion, null, null, null, lastETag, _backoffUntilUtc);
        }

        var url = $"https://api.github.com/repos/{_options.Repository.Trim()}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"Radpa/{currentVersion}");

        if (!string.IsNullOrWhiteSpace(lastETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", lastETag);
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var etag = response.Headers.ETag?.Tag ?? lastETag;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NotModified, currentVersion, null, null, null, etag, null);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ParseRetryAfter(response, now);
                _backoffUntilUtc = retryAfter;
                return new UpdateCheckResult(
                    UpdateCheckStatus.RateLimited,
                    currentVersion,
                    null,
                    null,
                    null,
                    etag,
                    retryAfter,
                    $"HTTP {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.OfflineOrFailed,
                    currentVersion,
                    null,
                    null,
                    null,
                    etag,
                    null,
                    $"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dto = await JsonSerializer.DeserializeAsync(stream, GitHubReleaseJsonContext.Default.GitHubReleaseDto, cancellationToken);
            if (dto is null || dto.Draft || dto.Prerelease || !SemanticVersion.TryParse(dto.TagName, out var latest))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, currentVersion, null, null, null, etag, null);
            }

            var status = latest > currentVersion ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
            return new UpdateCheckResult(status, currentVersion, latest, dto.HtmlUrl, dto.Body, etag, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _backoffUntilUtc = now.AddMinutes(15);
            return new UpdateCheckResult(
                UpdateCheckStatus.OfflineOrFailed,
                currentVersion,
                null,
                null,
                null,
                lastETag,
                _backoffUntilUtc,
                ex.Message);
        }
    }

    private static DateTimeOffset ParseRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return now.Add(delta);
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            foreach (var v in values)
            {
                if (long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(epoch);
                }
            }
        }

        return now.AddHours(1);
    }
}
