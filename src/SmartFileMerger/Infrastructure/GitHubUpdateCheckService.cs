namespace SmartFileMerger.Infrastructure;

using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Prüft asynchron gegen die GitHub REST-API, ob eine neuere Version der Anwendung verfügbar ist.
/// </summary>
public sealed class GitHubUpdateCheckService : IUpdateCheckService, IDisposable
{
    private const string DefaultReleasesUrl = "https://api.github.com/repos/mrambow/smart-file-merger/releases/latest";
    private const string UserAgentHeader = "SmartFileMerger";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1.5);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GitHubUpdateCheckService(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = DefaultTimeout };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentHeader);
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }
    }

    public async Task<(bool HasUpdate, string LatestVersion, string ReleaseUrl)> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return (false, string.Empty, string.Empty);
        }

        try
        {
            var release = await FetchLatestReleaseAsync(cancellationToken);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return (false, string.Empty, string.Empty);
            }

            return EvaluateVersionComparison(currentVersion, release);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, string.Empty, string.Empty);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<GitHubReleaseDto?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DefaultReleasesUrl);
        ApplyUserAgentIfMissing(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(contentStream, cancellationToken: cancellationToken);
    }

    private void ApplyUserAgentIfMissing(HttpRequestMessage request)
    {
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any() && !request.Headers.UserAgent.Any())
        {
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);
        }
    }

    private static (bool HasUpdate, string LatestVersion, string ReleaseUrl) EvaluateVersionComparison(
        string currentVersion,
        GitHubReleaseDto release)
    {
        if (!TryParseSemanticVersion(currentVersion, out var current) ||
            !TryParseSemanticVersion(release.TagName, out var latest))
        {
            return (false, string.Empty, string.Empty);
        }

        if (latest > current)
        {
            return (true, release.TagName ?? string.Empty, release.HtmlUrl ?? string.Empty);
        }

        return (false, string.Empty, string.Empty);
    }

    private static bool TryParseSemanticVersion(string? rawVersion, [NotNullWhen(true)] out Version? parsedVersion)
    {
        parsedVersion = null;

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var trimmed = rawVersion.Trim().TrimStart('v', 'V');
        var coreVersion = ExtractCoreVersion(trimmed);

        return Version.TryParse(coreVersion, out parsedVersion);
    }

    private static string ExtractCoreVersion(string raw)
    {
        var withoutPrerelease = raw.Split('-')[0];
        return withoutPrerelease.Split('+')[0];
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
