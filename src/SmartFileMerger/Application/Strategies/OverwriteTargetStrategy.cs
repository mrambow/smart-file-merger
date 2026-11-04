namespace SmartFileMerger.Application.Strategies;

using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Überschreibt die Zieldatei mit der Quelldatei.
/// </summary>
public sealed class OverwriteTargetStrategy : IConflictResolutionStrategy
{
    public const string StrategyKey = "overwrite";
    public const string StrategyDisplayName = "Ziel in B überschreiben (Quelle A gewinnt)";

    private readonly IFileSystemService _fileSystem;

    public OverwriteTargetStrategy(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public string Key => StrategyKey;

    public string DisplayName => StrategyDisplayName;

    public void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(sourceRoot);
        ArgumentNullException.ThrowIfNull(targetRoot);

        _fileSystem.MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: true);
    }
}
