namespace SmartFileMerger.Application.Strategies;

using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Benennt die existierende Zieldatei mit einem Backup-Pfad um und zieht die Quelldatei an den Zielort nach.
/// </summary>
public sealed class MoveOrRenameTargetStrategy : IConflictResolutionStrategy
{
    public const string StrategyKey = "backup_target";
    public const string StrategyDisplayName = "Bestehendes Ziel B sichern/umbenennen, dann Quelle A nachziehen";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    private readonly IFileSystemService _fileSystem;
    private readonly IUserInterface _ui;

    public MoveOrRenameTargetStrategy(IFileSystemService fileSystem, IUserInterface ui)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    public string Key => StrategyKey;

    public string DisplayName => StrategyDisplayName;

    public void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(sourceRoot);
        ArgumentNullException.ThrowIfNull(targetRoot);

        var defaultBackupPath = BuildDefaultBackupPath(conflict, targetRoot);
        var chosenBackupPath = _ui.PromptForCustomTargetFilePath(defaultBackupPath);
        var finalBackupPath = string.IsNullOrWhiteSpace(chosenBackupPath) ? defaultBackupPath : chosenBackupPath;

        _fileSystem.MoveFile(conflict.TargetFile.FullPath, finalBackupPath, overwrite: false);
        _fileSystem.MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: false);
    }

    private static string BuildDefaultBackupPath(FileConflict conflict, DirectoryRoot targetRoot)
    {
        var timestamp = DateTime.UtcNow.ToString(TimestampFormat);
        var backupFileName = $"{conflict.RelativePath.FileNameWithoutExtension}_backup_{timestamp}{conflict.RelativePath.Extension}";

        var relativeBackup = string.IsNullOrEmpty(conflict.RelativePath.DirectoryName)
            ? new RelativeFilePath(backupFileName)
            : new RelativeFilePath($"{conflict.RelativePath.DirectoryName}/{backupFileName}");

        return targetRoot.CombineWith(relativeBackup);
    }
}
