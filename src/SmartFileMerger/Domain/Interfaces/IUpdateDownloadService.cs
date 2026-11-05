namespace SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Abstraktion für das Herunterladen und atomare Anwenden von Anwendungsupdates im laufenden Betrieb.
/// </summary>
public interface IUpdateDownloadService
{
    /// <summary>
    /// Entfernt eine eventuell vorhandene alte ausführbare Datei (.old) aus einem vorherigen Update-Vorgang.
    /// </summary>
    void CleanupOldExecutableIfExists();

    /// <summary>
    /// Lädt die neue Binärdatei von der angegebenen URL herunter und tauscht sie atomar gegen die aktuelle Datei aus.
    /// </summary>
    Task<bool> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        Action<double> progressCallback,
        CancellationToken cancellationToken = default);
}
