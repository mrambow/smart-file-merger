namespace SmartFileMerger.Infrastructure;

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Lädt Anwendungsupdates von GitHub herunter und tauscht die aktuelle ausführbare Datei
/// mittels atomarem Rename-Verfahren im laufenden Windows-Betrieb aus.
/// </summary>
public sealed class WindowsUpdateDownloadService : IUpdateDownloadService, IDisposable
{
    private const string OldExtension = ".old";
    private const string NewExtension = ".new";
    private const string ChecksumExtension = ".sha256";
    private const string UserAgentHeader = "SmartFileMerger";
    private const int BufferSize = 81920;

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "raw.githubusercontent.com"
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly IFileSystemService _fileSystemService;
    private readonly Func<string?> _executablePathAccessor;

    public WindowsUpdateDownloadService(
        HttpClient? httpClient = null,
        IFileSystemService? fileSystemService = null,
        Func<string?>? executablePathAccessor = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgentHeader);
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }

        _fileSystemService = fileSystemService ?? new WindowsFileSystemService();
        _executablePathAccessor = executablePathAccessor ?? (() => Environment.ProcessPath);
    }

    public bool IsValidDownloadUrl(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host);
    }

    public void CleanupOldExecutableIfExists()
    {
        try
        {
            TryCleanupStaleUpdateFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Silently ignore if locked by decaying process
        }
        catch
        {
            // Silently ignore unexpected errors during startup cleanup
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        Action<double> progressCallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progressCallback);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return false;
        }

        ValidateDownloadUrl(downloadUrl);

        var exePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath) || !_fileSystemService.FileExists(exePath))
        {
            return false;
        }

        var oldPath = exePath + OldExtension;
        var newPath = exePath + NewExtension;

        try
        {
            await DownloadToFileAsync(downloadUrl, newPath, progressCallback, cancellationToken);
            SwapExecutables(exePath, oldPath, newPath);
            return true;
        }
        catch (InvalidOperationException)
        {
            RollbackFailedUpdate(exePath, oldPath, newPath);
            throw;
        }
        catch
        {
            RollbackFailedUpdate(exePath, oldPath, newPath);
            return false;
        }
    }

    private static void ValidateDownloadUrl(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Invalid download URL format.", nameof(downloadUrl));
        }

        ValidateUriSecurity(uri);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void ValidateUriSecurity(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Update downloads must use HTTPS.");
        }

        if (!AllowedHosts.Contains(uri.Host))
        {
            throw new InvalidOperationException($"Update download host '{uri.Host}' is not permitted.");
        }
    }

    private void TryCleanupStaleUpdateFiles()
    {
        var exePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        CleanupFileIfExists(exePath + OldExtension);
        CleanupFileIfExists(exePath + NewExtension);
    }

    private void CleanupFileIfExists(string filePath)
    {
        if (!_fileSystemService.FileExists(filePath))
        {
            return;
        }

        _fileSystemService.StripReadOnlyAttribute(filePath);
        _fileSystemService.DeleteFile(filePath);
    }

    private async Task DownloadToFileAsync(
        string downloadUrl,
        string destinationFilePath,
        Action<double> progressCallback,
        CancellationToken cancellationToken)
    {
        _fileSystemService.EnsureDirectoryExistsForFile(destinationFilePath);
        TryDeleteFile(destinationFilePath);

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        ApplyUserAgentIfMissing(request);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseUri = response.RequestMessage?.RequestUri;
        if (responseUri is not null)
        {
            ValidateUriSecurity(responseUri);
        }

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var fileStream = new FileStream(
            destinationFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true))
        {
            await StreamWithProgressAsync(contentStream, fileStream, totalBytes, progressCallback, cancellationToken);
        }

        VerifyDownloadedFile(destinationFilePath, totalBytes);
        await VerifyChecksumIfExistsAsync(downloadUrl, destinationFilePath, cancellationToken);
    }

    private async Task VerifyChecksumIfExistsAsync(
        string downloadUrl,
        string destinationFilePath,
        CancellationToken cancellationToken)
    {
        var sha256Url = downloadUrl + ChecksumExtension;
        string? sha256Content = null;

        try
        {
            using var shaRequest = new HttpRequestMessage(HttpMethod.Get, sha256Url);
            ApplyUserAgentIfMissing(shaRequest);
            using var shaResponse = await _httpClient.SendAsync(shaRequest, cancellationToken);
            if (!shaResponse.IsSuccessStatusCode)
            {
                return;
            }

            var shaUri = shaResponse.RequestMessage?.RequestUri;
            if (shaUri is not null)
            {
                ValidateUriSecurity(shaUri);
            }

            sha256Content = await shaResponse.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return;
        }

        ValidateChecksum(sha256Content, destinationFilePath);
    }

    private static void ValidateChecksum(string? sha256Content, string destinationFilePath)
    {
        if (string.IsNullOrWhiteSpace(sha256Content))
        {
            return;
        }

        var expectedHash = ExtractHash(sha256Content);
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return;
        }

        var actualHash = ComputeSha256Hex(destinationFilePath);
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum verification failed. Expected SHA256: '{expectedHash}', but got: '{actualHash}'.");
        }
    }

    private static string ExtractHash(string content)
    {
        var trimmed = content.Trim();
        var parts = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }

    private void VerifyDownloadedFile(string destinationFilePath, long? expectedBytes)
    {
        if (!_fileSystemService.FileExists(destinationFilePath))
        {
            throw new InvalidDataException("Downloaded update file does not exist.");
        }

        var metadata = _fileSystemService.GetMetadata(destinationFilePath);
        if (metadata.SizeInBytes <= 0)
        {
            throw new InvalidDataException("Downloaded update file is empty.");
        }

        if (expectedBytes.HasValue && expectedBytes.Value > 0 && metadata.SizeInBytes != expectedBytes.Value)
        {
            throw new InvalidDataException(
                $"Downloaded file size ({metadata.SizeInBytes} bytes) does not match expected Content-Length ({expectedBytes.Value} bytes).");
        }
    }

    private static async Task StreamWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        Action<double> progressCallback,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;
            NotifyProgress(totalBytesRead, totalBytes, progressCallback);
        }

        await destination.FlushAsync(cancellationToken);
        progressCallback(1.0);
    }

    private static void NotifyProgress(long totalBytesRead, long? totalBytes, Action<double> progressCallback)
    {
        if (totalBytes.HasValue && totalBytes.Value > 0)
        {
            var fraction = Math.Clamp((double)totalBytesRead / totalBytes.Value, 0.0, 1.0);
            progressCallback(fraction);
        }
    }

    private void SwapExecutables(string exePath, string oldPath, string newPath)
    {
        _fileSystemService.MoveFile(exePath, oldPath, overwrite: true);
        _fileSystemService.MoveFile(newPath, exePath, overwrite: false);
    }

    private void RollbackFailedUpdate(string exePath, string oldPath, string newPath)
    {
        TryDeleteFile(newPath);
        TryRestoreOldExecutable(exePath, oldPath);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_fileSystemService.FileExists(path))
            {
                _fileSystemService.StripReadOnlyAttribute(path);
                _fileSystemService.DeleteFile(path);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private void TryRestoreOldExecutable(string exePath, string oldPath)
    {
        try
        {
            if (_fileSystemService.FileExists(oldPath) && !_fileSystemService.FileExists(exePath))
            {
                _fileSystemService.MoveFile(oldPath, exePath, overwrite: true);
            }
        }
        catch
        {
            // Best effort rollback
        }
    }

    private string? ResolveExecutablePath()
    {
        var path = _executablePathAccessor();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var clean = path.Trim().Trim('\"', '\'');
            return Path.GetFullPath(clean);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyUserAgentIfMissing(HttpRequestMessage request)
    {
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any() && !request.Headers.UserAgent.Any())
        {
            request.Headers.UserAgent.ParseAdd(UserAgentHeader);
        }
    }
}
