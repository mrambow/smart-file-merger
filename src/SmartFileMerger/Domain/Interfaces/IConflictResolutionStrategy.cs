namespace SmartFileMerger.Domain.Interfaces;

using SmartFileMerger.Domain.Models;

/// <summary>
/// Strategie zur Auflösung einer Namenskollision zwischen Quelle und Ziel.
/// </summary>
public interface IConflictResolutionStrategy
{
    string Key { get; }

    string DisplayName { get; }

    void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot);
}
