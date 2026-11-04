namespace SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Abstraktion für asynchrone Versionsprüfungen gegen GitHub Releases.
/// </summary>
public interface IUpdateCheckService
{
    Task<(bool HasUpdate, string LatestVersion, string ReleaseUrl)> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default);
}
