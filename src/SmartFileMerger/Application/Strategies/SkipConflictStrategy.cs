namespace SmartFileMerger.Application.Strategies;

using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Überspringt den Konflikt und belässt die Quelldatei unverändert im Quellverzeichnis.
/// </summary>
public sealed class SkipConflictStrategy : IConflictResolutionStrategy
{
    public const string StrategyKey = "skip";
    public const string StrategyDisplayName = "Überspringen (Datei in A belassen)";

    public string Key => StrategyKey;

    public string DisplayName => StrategyDisplayName;

    public void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot)
    {
        // No-op: Konflikt wird übersprungen, Datei verbleibt in Quelle A.
    }
}
