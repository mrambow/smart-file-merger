namespace SmartFileMerger.Tests.Infrastructure;

using System.Net;
using System.Text;
using FluentAssertions;
using SmartFileMerger.Infrastructure;
using Xunit;

public sealed class GitHubUpdateCheckServiceTests
{
    private static HttpClient CreateMockClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var messageHandler = new MockHttpMessageHandler(handler);
        return new HttpClient(messageHandler);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerReleaseExists_ReturnsHasUpdateTrueWithDetails()
    {
        var jsonResponse = """
        {
            "tag_name": "v2.1.0",
            "html_url": "https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0"
        }
        """;

        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeTrue();
        latestVersion.Should().Be("v2.1.0");
        releaseUrl.Should().Be("https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenCurrentVersionIsEqualToLatest_ReturnsHasUpdateFalse()
    {
        var jsonResponse = """
        {
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/mrambow/smart-file-merger/releases/tag/v1.0.0"
        }
        """;

        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenCurrentVersionIsNewerThanLatest_ReturnsHasUpdateFalse()
    {
        var jsonResponse = """
        {
            "tag_name": "v0.9.0",
            "html_url": "https://github.com/mrambow/smart-file-merger/releases/tag/v0.9.0"
        }
        """;

        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenReleaseHasNoVPrefix_ParsesAndComparesCorrectly()
    {
        var jsonResponse = """
        {
            "tag_name": "2.0.0",
            "html_url": "https://github.com/mrambow/smart-file-merger/releases/tag/2.0.0"
        }
        """;

        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("v1.5.0");

        hasUpdate.Should().BeTrue();
        latestVersion.Should().Be("2.0.0");
        releaseUrl.Should().Be("https://github.com/mrambow/smart-file-merger/releases/tag/2.0.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenHttpErrorOccurs_ReturnsHasUpdateFalseGracefully()
    {
        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenHttpRequestExceptionThrown_ReturnsHasUpdateFalseGracefully()
    {
        using var client = CreateMockClient(_ => throw new HttpRequestException("Network failure"));

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenTimeoutOccurs_ReturnsHasUpdateFalseGracefully()
    {
        using var client = CreateMockClient(_ => throw new TaskCanceledException("Request timed out"));

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenMalformedJsonReturned_ReturnsHasUpdateFalseGracefully()
    {
        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!DOCTYPE html><html>Not json</html>", Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("1.0.0");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenVersionCannotBeParsed_ReturnsHasUpdateFalse()
    {
        var jsonResponse = """
        {
            "tag_name": "release_candidate_foo",
            "html_url": "https://github.com/mrambow/smart-file-merger/releases"
        }
        """;

        using var client = CreateMockClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        using var sut = new GitHubUpdateCheckService(client);

        var (hasUpdate, latestVersion, releaseUrl) = await sut.CheckForUpdateAsync("invalid-version");

        hasUpdate.Should().BeFalse();
        latestVersion.Should().BeEmpty();
        releaseUrl.Should().BeEmpty();
    }

    [Fact]
    public void DefaultConstructor_InitializesInstanceCleanly()
    {
        using var sut = new GitHubUpdateCheckService();

        sut.Should().NotBeNull();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
