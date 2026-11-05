namespace SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Abstraktion für die Anwendungsinstallation im Benutzerprofil und das Anlegen von Verknüpfungen.
/// </summary>
public interface IAppInstallationService
{
    /// <summary>
    /// Prüft, ob der aktuelle Prozess aus dem regulären Installationsverzeichnis gestartet wurde.
    /// </summary>
    bool IsRunningFromInstalledLocation();

    /// <summary>
    /// Gibt den vollständigen Pfad zum Installationsverzeichnis im Benutzerprofil zurück.
    /// </summary>
    string GetInstalledDirectoryPath();

    /// <summary>
    /// Gibt den vollständigen Pfad zur ausführbaren Datei im Installationsverzeichnis zurück.
    /// </summary>
    string GetInstalledExecutablePath();

    /// <summary>
    /// Installiert die ausführbare Datei in das Benutzerprogrammverzeichnis und erstellt Desktop- sowie Startmenü-Verknüpfungen.
    /// </summary>
    void InstallToUserProgramsAndCreateShortcuts();
}
