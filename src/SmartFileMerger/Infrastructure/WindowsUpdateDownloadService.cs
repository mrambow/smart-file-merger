namespace SmartFileMerger.Infrastructure;

using System.IO;
using System.Net.Http;
using SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Lädt Anwendungsupdates von GitHub herunter und tauscht die aktuelle ausführbare Datei
/// mittels atomarem Rename-Verfahren im laufenden Windows-Betrieb aus.
/// </summary>
public sealed class WindowsUpdateDownloadService : IUpdateDownloadService, IDisposable
{
    private const string OldExtension = ".old";
    private const string NewExtension = ".new";
    private const string UserAgentHeader = "SmartFileMerger";
    private const int BufferSize = 81920;

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

    public void CleanupOldExecutableIfExists()
    {
        try
        {
            TryCleanupOldExecutable();
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
        catch
        {
            RollbackFailedUpdate(exePath, oldPath, newPath);
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private void TryCleanupOldExecutable()
    {
        var exePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        var oldPath = exePath + OldExtension;
        if (!_fileSystemService.FileExists(oldPath))
        {
            return;
        }

        StripReadOnlyAttribute(oldPath);
        _fileSystemService.DeleteFile(oldPath);
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
                StripReadOnlyAttribute(path);
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

    private static void StripReadOnlyAttribute(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
        catch
        {
            // Best effort attribute clearing
        }
    }
}
