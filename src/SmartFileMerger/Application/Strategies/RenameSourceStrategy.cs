namespace SmartFileMerger.Application.Strategies;

using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Benennt die Quelldatei um und verschiebt sie an den neuen Zielort, während die existierende Zieldatei unberührt bleibt.
/// </summary>
public sealed class RenameSourceStrategy : IConflictResolutionStrategy
{
    public const string StrategyKey = "rename_source";
    public const string StrategyDisplayName = "Quelle A umbenennen und nach B schieben";
    private const string DefaultPromptMessage = "Neuer Dateiname für Quelldatei:";

    private readonly IFileSystemService _fileSystem;
    private readonly IUserInterface _ui;

    public RenameSourceStrategy(IFileSystemService fileSystem, IUserInterface ui)
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

        var suggestedFileName = BuildSuggestedFileName(conflict);
        var chosenFileName = _ui.PromptForNewFileName(suggestedFileName, DefaultPromptMessage);
        var finalFileName = string.IsNullOrWhiteSpace(chosenFileName) ? suggestedFileName : chosenFileName;

        var newTargetPath = BuildNewTargetPath(conflict, targetRoot, finalFileName);
        _fileSystem.MoveFile(conflict.SourceFile.FullPath, newTargetPath, overwrite: false);
    }

    private static string BuildSuggestedFileName(FileConflict conflict)
    {
        return $"{conflict.RelativePath.FileNameWithoutExtension}_from_source{conflict.RelativePath.Extension}";
    }

    private static string BuildNewTargetPath(FileConflict conflict, DirectoryRoot targetRoot, string fileName)
    {
        var relativeNewPath = string.IsNullOrEmpty(conflict.RelativePath.DirectoryName)
            ? new RelativeFilePath(fileName)
            : new RelativeFilePath($"{conflict.RelativePath.DirectoryName}/{fileName}");

        return targetRoot.CombineWith(relativeNewPath);
    }
}
