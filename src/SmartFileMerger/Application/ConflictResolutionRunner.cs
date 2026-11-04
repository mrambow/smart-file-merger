namespace SmartFileMerger.Application;

using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;

/// <summary>
/// Führt den interaktiven Konfliktlösungslauf für alle gesammelten Namenskollisionen durch.
/// </summary>
public class ConflictResolutionRunner
{
    private readonly IUserInterface _ui;

    public ConflictResolutionRunner(IUserInterface ui)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    /// <summary>
    /// Parameterloser Konstruktor zur Unterstützung von Test-Proxies / Mocks.
    /// </summary>
    protected ConflictResolutionRunner()
    {
        _ui = null!;
    }

    public virtual (int Resolved, int Skipped, int Errors) ResolveConflicts(
        IReadOnlyList<FileConflict> conflicts,
        DirectoryRoot source,
        DirectoryRoot target,
        IReadOnlyList<IConflictResolutionStrategy> availableStrategies,
        bool isDryRun)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(availableStrategies);

        if (conflicts.Count == 0)
        {
            return (0, 0, 0);
        }

        var resolvedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        for (var i = 0; i < conflicts.Count; i++)
        {
            var conflict = conflicts[i];
            var chosenStrategy = _ui.PromptForConflictResolution(conflict, i + 1, conflicts.Count, availableStrategies);

            if (chosenStrategy is null)
            {
                throw new InvalidOperationException("Es muss eine Konfliktlösungsstrategie ausgewählt werden.");
            }

            if (IsSkipStrategy(chosenStrategy))
            {
                skippedCount++;
                continue;
            }

            if (TryExecuteResolutionStrategy(chosenStrategy, conflict, source, target, isDryRun))
            {
                resolvedCount++;
            }
            else
            {
                errorCount++;
            }
        }

        return (resolvedCount, skippedCount, errorCount);
    }

    private bool TryExecuteResolutionStrategy(
        IConflictResolutionStrategy strategy,
        FileConflict conflict,
        DirectoryRoot source,
        DirectoryRoot target,
        bool isDryRun)
    {
        if (isDryRun)
        {
            return true;
        }

        try
        {
            strategy.Execute(conflict, source, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _ui.ShowWarning($"Fehler beim Ausführen der Konfliktlösung für '{conflict.RelativePath.Value}': {ex.Message}");
            return false;
        }
    }

    private static bool IsSkipStrategy(IConflictResolutionStrategy strategy)
    {
        return strategy is SkipConflictStrategy ||
               string.Equals(strategy.Key, SkipConflictStrategy.StrategyKey, StringComparison.OrdinalIgnoreCase);
    }
}
