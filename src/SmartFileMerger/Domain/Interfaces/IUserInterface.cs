namespace SmartFileMerger.Domain.Interfaces;

using SmartFileMerger.Domain.Models;

/// <summary>
/// Abstraktion für alle Benutzerinteraktionen und Terminalausgaben.
/// </summary>
public interface IUserInterface
{
    void ShowHeader();

    void ShowUpdateNotice(string latestVersion, string releaseUrl);

    /// <summary>
    /// Fragt den Benutzer, ob SmartFileMerger installiert werden soll (Desktop- und Startmenü-Verknüpfung).
    /// </summary>
    bool PromptForInstallation();

    /// <summary>
    /// Fragt den Benutzer, ob ein verfügbares Update automatisch heruntergeladen und installiert werden soll.
    /// </summary>
    bool PromptForUpdateInstallation(string latestVersion);

    /// <summary>
    /// Führt die übergebene Download-Aktion mit einer Fortschrittsanzeige aus.
    /// Der übergebene Progress-Wert ist ein Bruch zwischen 0.0 und 1.0.
    /// </summary>
    Task RunWithDownloadProgressAsync(string version, Func<Action<double>, Task> downloadWork);

    (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) PromptConfiguration();

    bool ConfirmMerge(DirectoryRoot source, DirectoryRoot target, bool isDryRun);

    void RunWithProgress(int totalFiles, Action<Action<string, int>> work);

    void DisplayMergeSummary(MergeSummary summary);

    IConflictResolutionStrategy PromptForConflictResolution(
        FileConflict conflict,
        int currentIndex,
        int totalConflicts,
        IReadOnlyList<IConflictResolutionStrategy> availableStrategies);

    string PromptForNewFileName(string defaultFileName, string promptMessage);

    string PromptForCustomTargetFilePath(string defaultFullPath);

    void ShowInfo(string message);

    void ShowWarning(string message);

    void ShowError(string message);
}
