namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Zusammenfassung des gesamten Merge-Laufs.
/// </summary>
public sealed record MergeSummary(
    int TotalFilesScanned,
    int FilesMovedDirectly,
    int IdenticalFilesSkipped,
    int ConflictsResolved,
    int ConflictsSkipped,
    int ErrorsOccurred,
    TimeSpan ElapsedDuration,
    bool IsDryRun);
