namespace SmartFileMerger.Tests.Infrastructure;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using NSubstitute;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Infrastructure;
using Xunit;

public sealed class WindowsUpdateDownloadServiceTests : IDisposable
{
    private const string ValidGitHubDownloadUrl = "https://github.com/mrambow/smart-file-merger/releases/download/v1.1.0/SmartFileMerger.exe";

    private readonly string _testRoot;
    private readonly string _exePath;
    private readonly string _oldPath;
    private readonly string _newPath;

    public WindowsUpdateDownloadServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "SmartFileMergerUpdateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);

        _exePath = Path.Combine(_testRoot, "SmartFileMerger.exe");
        _oldPath = Path.Combine(_testRoot, "SmartFileMerger.exe.old");
        _newPath = Path.Combine(_testRoot, "SmartFileMerger.exe.new");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_testRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenOldExecutableExists_DeletesOldFile()
    {
        File.WriteAllText(_exePath, "current binary");
        File.WriteAllText(_oldPath, "stale binary");

        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        sut.CleanupOldExecutableIfExists();

        File.Exists(_exePath).Should().BeTrue();
        File.Exists(_oldPath).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenStaleNewFileExists_DeletesNewFile()
    {
        File.WriteAllText(_exePath, "current binary");
        File.WriteAllText(_newPath, "stale partial download");

        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        sut.CleanupOldExecutableIfExists();

        File.Exists(_exePath).Should().BeTrue();
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenOldExecutableIsReadOnly_DeletesOldFileSuccessfully()
    {
        File.WriteAllText(_exePath, "current binary");
        File.WriteAllText(_oldPath, "stale binary");
        File.SetAttributes(_oldPath, FileAttributes.ReadOnly);

        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        sut.CleanupOldExecutableIfExists();

        File.Exists(_oldPath).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenOldExecutableIsLocked_DoesNotThrowAndHandlesLockSilently()
    {
        File.WriteAllText(_exePath, "current binary");
        File.WriteAllText(_oldPath, "locked binary");

        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        using (new FileStream(_oldPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var act = () => sut.CleanupOldExecutableIfExists();
            act.Should().NotThrow();
            File.Exists(_oldPath).Should().BeTrue();
        }

        sut.CleanupOldExecutableIfExists();
        File.Exists(_oldPath).Should().BeFalse();
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenOldExecutableDoesNotExist_DoesNotThrow()
    {
        File.WriteAllText(_exePath, "current binary");

        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        var act = () => sut.CleanupOldExecutableIfExists();

        act.Should().NotThrow();
        File.Exists(_exePath).Should().BeTrue();
    }

    [Fact]
    public void CleanupOldExecutableIfExists_WhenExecutablePathIsNull_DoesNotThrow()
    {
        var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => null);

        var act = () => sut.CleanupOldExecutableIfExists();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenDownloadSucceeds_UpdatesExecutableAndBacksUpToOldAndReportsProgress()
    {
        File.WriteAllText(_exePath, "v1.0.0 binary content");

        const string newPayload = "v1.1.0 new binary payload content";
        var payloadBytes = Encoding.UTF8.GetBytes(newPayload);

        var httpClient = CreateHttpClientWithResponder(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith(".sha256"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payloadBytes)
            };
        });

        var progressValues = new List<double>();
        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            progress => progressValues.Add(progress));

        success.Should().BeTrue();
        File.ReadAllText(_exePath).Should().Be(newPayload);
        File.Exists(_oldPath).Should().BeTrue();
        File.ReadAllText(_oldPath).Should().Be("v1.0.0 binary content");
        File.Exists(_newPath).Should().BeFalse();

        progressValues.Should().NotBeEmpty();
        progressValues.Last().Should().Be(1.0);
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenServerReturnsHttpError_LeavesOriginalIntactAndCleansUpNewFile()
    {
        File.WriteAllText(_exePath, "v1.0.0 untouched content");

        var httpClient = CreateHttpClientWithResponder(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            _ => { });

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("v1.0.0 untouched content");
        File.Exists(_oldPath).Should().BeFalse();
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenDownloadCanceled_LeavesOriginalIntactAndCleansUpNewFile()
    {
        File.WriteAllText(_exePath, "v1.0.0 untouched content");

        var httpClient = CreateHttpClientWithResponder(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("partial payload"))
            };
            return response;
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            _ => { },
            cts.Token);

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("v1.0.0 untouched content");
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenSwapToNewFileFails_RollsBackOldFileToOriginalExecutable()
    {
        File.WriteAllText(_exePath, "original stable binary");

        var payloadBytes = Encoding.UTF8.GetBytes("new binary payload");
        var httpClient = CreateHttpClientWithResponder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payloadBytes)
        });

        var realFileSystem = new WindowsFileSystemService();
        var fileSystemMock = Substitute.For<IFileSystemService>();

        fileSystemMock.FileExists(Arg.Any<string>())
            .Returns(call => realFileSystem.FileExists(call.Arg<string>()));
        fileSystemMock.GetMetadata(Arg.Any<string>())
            .Returns(call => realFileSystem.GetMetadata(call.Arg<string>()));
        fileSystemMock.When(x => x.EnsureDirectoryExistsForFile(Arg.Any<string>()))
            .Do(call => realFileSystem.EnsureDirectoryExistsForFile(call.Arg<string>()));
        fileSystemMock.When(x => x.DeleteFile(Arg.Any<string>()))
            .Do(call => realFileSystem.DeleteFile(call.Arg<string>()));

        fileSystemMock.When(x => x.MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(call =>
            {
                var src = call.ArgAt<string>(0);
                var dst = call.ArgAt<string>(1);
                var overwrite = call.ArgAt<bool>(2);

                if (src == _newPath && dst == _exePath)
                {
                    throw new IOException("Simulated file lock during new executable rename");
                }

                realFileSystem.MoveFile(src, dst, overwrite);
            });

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            fileSystemService: fileSystemMock,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            _ => { });

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("original stable binary");
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenUrlIsNotHttps_ThrowsInvalidOperationException()
    {
        File.WriteAllText(_exePath, "current binary");
        using var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        var act = async () => await sut.DownloadAndApplyUpdateAsync(
            "http://github.com/mrambow/smart-file-merger/releases/download/v1.0.0/SmartFileMerger.exe",
            _ => { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HTTPS*");
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenHostIsNotPermitted_ThrowsInvalidOperationException()
    {
        File.WriteAllText(_exePath, "current binary");
        using var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        var act = async () => await sut.DownloadAndApplyUpdateAsync(
            "https://evil.com/downloads/SmartFileMerger.exe",
            _ => { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not permitted*");
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenDownloadedFileIsEmpty_ReturnsFalseAndRollsBack()
    {
        File.WriteAllText(_exePath, "current binary");

        var httpClient = CreateHttpClientWithResponder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        });

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            _ => { });

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("current binary");
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenCurrentExecutableDoesNotExist_ReturnsFalse()
    {
        var nonExistentExe = Path.Combine(_testRoot, "NonExistent.exe");
        var httpClient = CreateHttpClientWithResponder(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => nonExistentExe);

        var success = await sut.DownloadAndApplyUpdateAsync(
            ValidGitHubDownloadUrl,
            _ => { });

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenDownloadUrlIsWhitespace_ReturnsFalse()
    {
        File.WriteAllText(_exePath, "current binary");
        using var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync("   ", _ => { });

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenProgressCallbackIsNull_ThrowsArgumentNullException()
    {
        File.WriteAllText(_exePath, "current binary");
        using var sut = new WindowsUpdateDownloadService(executablePathAccessor: () => _exePath);

        var act = async () => await sut.DownloadAndApplyUpdateAsync(ValidGitHubDownloadUrl, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("https://github.com/mrambow/smart-file-merger/releases/download/v1.0.0/SmartFileMerger.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123", true)]
    [InlineData("https://raw.githubusercontent.com/mrambow/smart-file-merger/main/README.md", true)]
    [InlineData("http://github.com/mrambow/smart-file-merger/releases/download/v1.0.0/SmartFileMerger.exe", false)]
    [InlineData("https://evil.com/malicious.exe", false)]
    [InlineData("not-a-url", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValidDownloadUrl_ValidatesAllowedHostsAndHttps(string? url, bool expectedResult)
    {
        using var sut = new WindowsUpdateDownloadService();

        var result = sut.IsValidDownloadUrl(url!);

        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenContentLengthMismatchesDownloadedBytes_ReturnsFalseAndRollsBack()
    {
        File.WriteAllText(_exePath, "current binary");

        var httpClient = CreateHttpClientWithResponder(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("actual content 18b"))
            };
            response.Content.Headers.ContentLength = 9999;
            return response;
        });

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(ValidGitHubDownloadUrl, _ => { });

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("current binary");
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenSha256ChecksumMatches_Succeeds()
    {
        File.WriteAllText(_exePath, "current binary");

        const string newPayload = "new verified payload";
        var payloadBytes = Encoding.UTF8.GetBytes(newPayload);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var expectedHash = Convert.ToHexString(sha256.ComputeHash(payloadBytes));

        var httpClient = CreateHttpClientWithResponder(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith(".sha256"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{expectedHash}  SmartFileMerger.exe\n")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payloadBytes)
            };
        });

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(ValidGitHubDownloadUrl, _ => { });

        success.Should().BeTrue();
        File.ReadAllText(_exePath).Should().Be(newPayload);
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_WhenSha256ChecksumMismatches_ReturnsFalseAndRollsBack()
    {
        File.WriteAllText(_exePath, "current binary");

        const string newPayload = "tampered payload";
        var payloadBytes = Encoding.UTF8.GetBytes(newPayload);

        var httpClient = CreateHttpClientWithResponder(req =>
        {
            if (req.RequestUri!.AbsoluteUri.EndsWith(".sha256"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("0000000000000000000000000000000000000000000000000000000000000000  SmartFileMerger.exe\n")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payloadBytes)
            };
        });

        using var sut = new WindowsUpdateDownloadService(
            httpClient: httpClient,
            executablePathAccessor: () => _exePath);

        var success = await sut.DownloadAndApplyUpdateAsync(ValidGitHubDownloadUrl, _ => { });

        success.Should().BeFalse();
        File.ReadAllText(_exePath).Should().Be("current binary");
        File.Exists(_newPath).Should().BeFalse();
    }

    [Fact]
    public void Dispose_WhenOwnsHttpClient_DisposesSuccessfully()
    {
        var sut = new WindowsUpdateDownloadService();

        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_WhenExternalHttpClientProvided_DoesNotDisposeExternalClient()
    {
        var httpClient = CreateHttpClientWithResponder(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new WindowsUpdateDownloadService(httpClient: httpClient);

        sut.Dispose();

        var act = async () => await httpClient.GetAsync("https://github.com");
        await act.Should().NotThrowAsync();
    }

    private static HttpClient CreateHttpClientWithResponder(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new TestMockHttpMessageHandler(responder);
        return new HttpClient(handler);
    }

    private sealed class TestMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public TestMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responder(request));
        }
    }
}
