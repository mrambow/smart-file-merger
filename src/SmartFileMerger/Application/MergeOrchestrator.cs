namespace SmartFileMerger.Application;

using System.Diagnostics;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Orchestriert den gesamten Merge-Workflow: Validierung, Scan & Partitionierung,
/// Konfliktbehandlung und Verzeichnisbereinigung.
/// </summary>
public class MergeOrchestrator
{
    private const int SingleProgressStep = 1;

    private readonly IFileSystemService _fileSystem;
    private readonly IUserInterface _ui;
    private readonly ConflictResolutionRunner _conflictRunner;
    private readonly IReadOnlyList<IConflictResolutionStrategy> _strategies;

    public MergeOrchestrator(
        IFileSystemService fileSystem,
        IUserInterface ui,
        ConflictResolutionRunner conflictRunner,
        IEnumerable<IConflictResolutionStrategy> strategies)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _conflictRunner = conflictRunner ?? throw new ArgumentNullException(nameof(conflictRunner));
        ArgumentNullException.ThrowIfNull(strategies);

        _strategies = strategies.ToList();
    }

    public async Task<MergeSummary> ExecuteAsync(
        DirectoryRoot source,
        DirectoryRoot target,
        bool isDryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateDirectories(source, target);
        EnsureTargetDirectory(target, isDryRun);

        var stopwatch = Stopwatch.StartNew();

        var scanResult = ScanAndPartitionFiles(source, target, isDryRun, cancellationToken);
        var (conflictsResolved, conflictsSkipped, resolutionErrors) = ResolveConflicts(scanResult.ConflictQueue, source, target, isDryRun);
        scanResult.ErrorsOccurred += resolutionErrors;

        CleanupEmptyDirectories(source, isDryRun, scanResult);

        stopwatch.Stop();
        await Task.CompletedTask;

        return CreateSummary(scanResult, conflictsResolved, conflictsSkipped, stopwatch.Elapsed, isDryRun);
    }

    private void ValidateDirectories(DirectoryRoot source, DirectoryRoot target)
    {
        if (!_fileSystem.DirectoryExists(source.FullPath))
        {
            throw new DirectoryNotFoundException($"Das Quellverzeichnis wurde nicht gefunden: {source.FullPath}");
        }

        if (string.Equals(source.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Quell- und Zielverzeichnis dürfen nicht identisch sein.");
        }

        if (target.IsSubdirectoryOf(source))
        {
            throw new ArgumentException("Zielverzeichnis darf kein Unterordner des Quellverzeichnisses sein.");
        }
    }

    private void EnsureTargetDirectory(DirectoryRoot target, bool isDryRun)
    {
        if (isDryRun || _fileSystem.DirectoryExists(target.FullPath))
        {
            return;
        }

        _fileSystem.CreateDirectory(target.FullPath);
    }

    private ScanPartitionResult ScanAndPartitionFiles(
        DirectoryRoot source,
        DirectoryRoot target,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        var relativePaths = _fileSystem.GetAllRelativeFilePaths(source);
        var result = new ScanPartitionResult(relativePaths.Count);

        if (relativePaths.Count == 0)
        {
            return result;
        }

        _ui.RunWithProgress(relativePaths.Count, reportProgress =>
        {
            foreach (var relativePath in relativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reportProgress(relativePath.Value, SingleProgressStep);
                ProcessFile(source, target, relativePath, isDryRun, result);
            }
        });

        return result;
    }

    private void ProcessFile(
        DirectoryRoot source,
        DirectoryRoot target,
        RelativeFilePath relativePath,
        bool isDryRun,
        ScanPartitionResult result)
    {
        var sourceFullPath = source.CombineWith(relativePath);
        var targetFullPath = target.CombineWith(relativePath);

        try
        {
            if (!_fileSystem.FileExists(targetFullPath))
            {
                DirectMoveFile(sourceFullPath, targetFullPath, isDryRun);
                result.FilesMovedDirectly++;
                return;
            }

            HandleExistingTargetFile(sourceFullPath, targetFullPath, relativePath, isDryRun, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.ErrorsOccurred++;
            _ui.ShowWarning($"Fehler bei Datei '{relativePath.Value}': {ex.Message}");
        }
    }

    private void DirectMoveFile(string sourceFullPath, string targetFullPath, bool isDryRun)
    {
        if (isDryRun)
        {
            return;
        }

        _fileSystem.EnsureDirectoryExistsForFile(targetFullPath);
        _fileSystem.MoveFile(sourceFullPath, targetFullPath, overwrite: false);
    }

    private void HandleExistingTargetFile(
        string sourceFullPath,
        string targetFullPath,
        RelativeFilePath relativePath,
        bool isDryRun,
        ScanPartitionResult result)
    {
        var sourceMetadata = _fileSystem.GetMetadata(sourceFullPath);
        var targetMetadata = _fileSystem.GetMetadata(targetFullPath);
        var conflict = new FileConflict(relativePath, sourceMetadata, targetMetadata);

        if (conflict.IsIdenticalSizeAndTimestamp)
        {
            DeleteDuplicateSourceFile(sourceFullPath, isDryRun);
            result.IdenticalFilesSkipped++;
            return;
        }

        result.ConflictQueue.Add(conflict);
    }

    private void DeleteDuplicateSourceFile(string sourceFullPath, bool isDryRun)
    {
        if (isDryRun)
        {
            return;
        }

        _fileSystem.DeleteFile(sourceFullPath);
    }

    private (int Resolved, int Skipped, int Errors) ResolveConflicts(
        IReadOnlyList<FileConflict> conflicts,
        DirectoryRoot source,
        DirectoryRoot target,
        bool isDryRun)
    {
        if (conflicts.Count == 0)
        {
            return (0, 0, 0);
        }

        return _conflictRunner.ResolveConflicts(conflicts, source, target, _strategies, isDryRun);
    }

    private void CleanupEmptyDirectories(DirectoryRoot source, bool isDryRun, ScanPartitionResult result)
    {
        if (isDryRun)
        {
            return;
        }

        try
        {
            _fileSystem.RemoveEmptyDirectories(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.ErrorsOccurred++;
            _ui.ShowWarning($"Fehler beim Bereinigen leerer Verzeichnisse: {ex.Message}");
        }
    }

    private static MergeSummary CreateSummary(
        ScanPartitionResult scanResult,
        int conflictsResolved,
        int conflictsSkipped,
        TimeSpan elapsedDuration,
        bool isDryRun)
    {
        return new MergeSummary(
            scanResult.TotalFilesScanned,
            scanResult.FilesMovedDirectly,
            scanResult.IdenticalFilesSkipped,
            conflictsResolved,
            conflictsSkipped,
            scanResult.ErrorsOccurred,
            elapsedDuration,
            isDryRun);
    }

    private sealed class ScanPartitionResult
    {
        public int TotalFilesScanned { get; }
        public int FilesMovedDirectly { get; set; }
        public int IdenticalFilesSkipped { get; set; }
        public int ErrorsOccurred { get; set; }
        public List<FileConflict> ConflictQueue { get; } = [];

        public ScanPartitionResult(int totalFilesScanned)
        {
            TotalFilesScanned = totalFilesScanned;
        }
    }
}
