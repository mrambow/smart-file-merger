namespace SmartFileMerger.Domain.Interfaces;

using SmartFileMerger.Domain.Models;

/// <summary>
/// Abstraktion für alle Benutzerinteraktionen und Terminalausgaben.
/// </summary>
public interface IUserInterface
{
    void ShowHeader();

    void ShowUpdateNotice(string latestVersion, string releaseUrl);

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
