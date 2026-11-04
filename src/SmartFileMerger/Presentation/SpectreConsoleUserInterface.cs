namespace SmartFileMerger.Presentation;

using System.Globalization;
using Spectre.Console;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Infrastructure;

/// <summary>
/// Spectre.Console-basierte Implementierung der Benutzeroberfläche für das interaktive Terminal.
/// </summary>
public sealed class SpectreConsoleUserInterface : IUserInterface
{
    private const string AppTitle = "SmartFileMerger";
    private const string AppSubtitle = "[dim]Intelligenter Verzeichnis-Merger mit interaktiver Konfliktauflösung[/]\n";
    private const string ModeNormal = "Normal (Dateien verschieben)";
    private const string ModeSimulation = "Simulation (--dry-run, keine Änderungen)";
    private const double TimestampIdenticalToleranceSeconds = 2.0;

    private readonly IAnsiConsole _console;
    private readonly IFileSystemService _fileSystemService;

    public SpectreConsoleUserInterface(
        IAnsiConsole? console = null,
        IFileSystemService? fileSystemService = null)
    {
        _console = console ?? AnsiConsole.Console;
        _fileSystemService = fileSystemService ?? new WindowsFileSystemService();
    }

    public void ShowHeader()
    {
        _console.Write(new FigletText(AppTitle).Color(Color.Cyan1));
        _console.MarkupLine(AppSubtitle);
    }

    public void ShowUpdateNotice(string latestVersion, string releaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseUrl);

        var content = $"[yellow]Eine neuere Version ({Markup.Escape(latestVersion)}) ist verfügbar![/]\n" +
                      $"Download unter: [link={Markup.Escape(releaseUrl)}]{Markup.Escape(releaseUrl)}[/]";

        var panel = new Panel(content)
        {
            Header = new PanelHeader("[yellow]Update verfügbar[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        _console.Write(panel);
    }

    public (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) PromptConfiguration()
    {
        var source = PromptSourceDirectory();
        var target = PromptTargetDirectory(source);
        var isDryRun = PromptExecutionMode();

        return (source, target, isDryRun);
    }

    public bool ConfirmMerge(DirectoryRoot source, DirectoryRoot target, bool isDryRun)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Parameter[/]")
            .AddColumn("[bold]Wert[/]");

        table.AddRow("Quelle", $"[cyan]{Markup.Escape(source.FullPath)}[/]");
        table.AddRow("Ziel", $"[cyan]{Markup.Escape(target.FullPath)}[/]");
        table.AddRow("Modus", isDryRun ? "[yellow]Simulation (Dry-Run)[/]" : "[green]Normal[/]");

        _console.Write(table);

        return _console.Confirm("Möchtest du den Vorgang jetzt starten?", defaultValue: false);
    }

    public void RunWithProgress(int totalFiles, Action<Action<string, int>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        _console.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(ctx => ExecuteWithProgress(ctx, totalFiles, work));
    }

    private static void ExecuteWithProgress(
        ProgressContext ctx,
        int totalFiles,
        Action<Action<string, int>> work)
    {
        var progressTask = ctx.AddTask("[green]Verarbeite Dateien[/]", maxValue: Math.Max(1, totalFiles));
        work((description, increment) => UpdateProgressTask(progressTask, description, increment));
    }

    private static void UpdateProgressTask(ProgressTask progressTask, string description, int increment)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            progressTask.Description = $"[green]Verarbeite:[/] {Markup.Escape(description)}";
        }

        progressTask.Increment(increment);
    }

    public IConflictResolutionStrategy PromptForConflictResolution(
        FileConflict conflict,
        int currentIndex,
        int totalConflicts,
        IReadOnlyList<IConflictResolutionStrategy> availableStrategies)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(availableStrategies);

        var diffTable = BuildConflictDiffTable(conflict);
        var panelHeader = $"[yellow]Konflikt {currentIndex} von {totalConflicts}:[/] [bold]{Markup.Escape(conflict.RelativePath.Value)}[/]";

        var panel = new Panel(diffTable)
        {
            Header = new PanelHeader(panelHeader),
            Border = BoxBorder.Rounded
        };

        _console.Write(panel);

        var selectionPrompt = new SelectionPrompt<IConflictResolutionStrategy>()
            .Title("Aktion zur Konfliktauflösung wählen:")
            .UseConverter(strategy => strategy.DisplayName)
            .AddChoices(availableStrategies);

        return _console.Prompt(selectionPrompt);
    }

    public string PromptForNewFileName(string defaultFileName, string promptMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultFileName);

        var message = string.IsNullOrWhiteSpace(promptMessage) ? "Neuer Dateiname:" : promptMessage;
        var prompt = new TextPrompt<string>(Markup.Escape(message))
            .DefaultValue(defaultFileName)
            .AllowEmpty();

        var result = _console.Prompt(prompt);
        return string.IsNullOrWhiteSpace(result) ? defaultFileName : result.Trim();
    }

    public string PromptForCustomTargetFilePath(string defaultFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultFullPath);

        var prompt = new TextPrompt<string>("Neuer Zielpfad:")
            .DefaultValue(defaultFullPath)
            .AllowEmpty();

        var result = _console.Prompt(prompt);
        return string.IsNullOrWhiteSpace(result) ? defaultFullPath : CleanPath(result);
    }

    public void ShowInfo(string message)
    {
        _console.MarkupLine($"[blue][[INFO]][/] {Markup.Escape(message ?? string.Empty)}");
    }

    public void ShowWarning(string message)
    {
        _console.MarkupLine($"[yellow][[WARN]][/] {Markup.Escape(message ?? string.Empty)}");
    }

    public void ShowError(string message)
    {
        _console.MarkupLine($"[red][[ERROR]][/] {Markup.Escape(message ?? string.Empty)}");
    }

    public void DisplayMergeSummary(MergeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metrik")
            .AddColumn(new TableColumn("Wert").RightAligned());

        AddSummaryRows(table, summary);

        var panelTitle = summary.IsDryRun ? "[yellow]Zusammenfassung (Simulation)[/]" : "[green]Zusammenfassung[/]";
        var borderStyle = summary.ErrorsOccurred > 0
            ? new Style(Color.Red)
            : summary.IsDryRun
                ? new Style(Color.Yellow)
                : new Style(Color.Green);

        var panel = new Panel(table)
        {
            Header = new PanelHeader(panelTitle),
            Border = BoxBorder.Rounded,
            BorderStyle = borderStyle
        };

        _console.Write(panel);
    }

    #region Private Helpers - Prompts

    private DirectoryRoot PromptSourceDirectory()
    {
        var prompt = new TextPrompt<string>("Quellverzeichnis (Source):")
            .PromptStyle("cyan")
            .Validate(ValidateSourcePath);

        var path = _console.Prompt(prompt);
        return new DirectoryRoot(CleanPath(path));
    }

    private ValidationResult ValidateSourcePath(string? input)
    {
        var cleaned = CleanPath(input);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return ValidationResult.Error("[red]Quellpfad darf nicht leer sein.[/]");
        }

        try
        {
            var root = new DirectoryRoot(cleaned);
            if (!_fileSystemService.DirectoryExists(root.FullPath))
            {
                return ValidationResult.Error("[red]Quellverzeichnis existiert nicht.[/]");
            }
        }
        catch (Exception ex)
        {
            return ValidationResult.Error($"[red]{Markup.Escape(ex.Message)}[/]");
        }

        return ValidationResult.Success();
    }

    private DirectoryRoot PromptTargetDirectory(DirectoryRoot source)
    {
        while (true)
        {
            var targetRoot = PromptAndValidateTargetPath(source);
            if (EnsureTargetDirectoryExists(targetRoot))
            {
                return targetRoot;
            }

            _console.MarkupLine("[yellow]Bitte gib ein anderes Zielverzeichnis an.[/]");
        }
    }

    private DirectoryRoot PromptAndValidateTargetPath(DirectoryRoot source)
    {
        var prompt = new TextPrompt<string>("Zielverzeichnis (Target):")
            .PromptStyle("cyan")
            .Validate(input => ValidateTargetPath(input, source));

        var path = _console.Prompt(prompt);
        return new DirectoryRoot(CleanPath(path));
    }

    private static ValidationResult ValidateTargetPath(string? input, DirectoryRoot source)
    {
        var cleaned = CleanPath(input);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return ValidationResult.Error("[red]Zielpfad darf nicht leer sein.[/]");
        }

        try
        {
            var target = new DirectoryRoot(cleaned);
            if (string.Equals(target.FullPath, source.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Error("[red]Zielverzeichnis darf nicht mit dem Quellverzeichnis identisch sein.[/]");
            }

            if (target.IsSubdirectoryOf(source))
            {
                return ValidationResult.Error("[red]Zielverzeichnis darf kein Unterverzeichnis der Quelle sein.[/]");
            }
        }
        catch (Exception ex)
        {
            return ValidationResult.Error($"[red]{Markup.Escape(ex.Message)}[/]");
        }

        return ValidationResult.Success();
    }

    private bool EnsureTargetDirectoryExists(DirectoryRoot target)
    {
        if (_fileSystemService.DirectoryExists(target.FullPath))
        {
            return true;
        }

        var confirmed = _console.Confirm(
            $"Zielverzeichnis '[cyan]{Markup.Escape(target.FullPath)}[/]' existiert nicht. Soll es erstellt werden?",
            defaultValue: true);

        if (confirmed)
        {
            _fileSystemService.CreateDirectory(target.FullPath);
            return true;
        }

        return false;
    }

    private bool PromptExecutionMode()
    {
        var selectionPrompt = new SelectionPrompt<string>()
            .Title("Ausführungsmodus wählen:")
            .AddChoices(ModeNormal, ModeSimulation);

        var choice = _console.Prompt(selectionPrompt);
        return string.Equals(choice, ModeSimulation, StringComparison.Ordinal);
    }

    private static string CleanPath(string? input) =>
        input?.Trim().Trim('"', '\'') ?? string.Empty;

    #endregion

    #region Private Helpers - Conflict Diff Table

    private static Table BuildConflictDiffTable(FileConflict conflict)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Eigenschaft")
            .AddColumn("Quelle (A)")
            .AddColumn("Ziel (B)")
            .AddColumn("Differenz");

        AddSizeRow(table, conflict);
        AddTimestampRow(table, conflict);

        return table;
    }

    private static void AddSizeRow(Table table, FileConflict conflict)
    {
        var sourceBytes = conflict.SourceFile.SizeInBytes;
        var targetBytes = conflict.TargetFile.SizeInBytes;
        var diff = conflict.SizeDifferenceInBytes;

        string sourceCol;
        string targetCol;
        string diffCol;

        if (diff == 0)
        {
            sourceCol = FormatBytes(sourceBytes);
            targetCol = FormatBytes(targetBytes);
            diffCol = "[dim]Identisch[/]";
        }
        else if (diff > 0)
        {
            sourceCol = $"[green]{FormatBytes(sourceBytes)}[/]";
            targetCol = $"[grey]{FormatBytes(targetBytes)}[/]";
            diffCol = $"[green]+{FormatBytes(diff)} (Quelle größer)[/]";
        }
        else
        {
            sourceCol = $"[grey]{FormatBytes(sourceBytes)}[/]";
            targetCol = $"[green]{FormatBytes(targetBytes)}[/]";
            diffCol = $"[grey]-{FormatBytes(Math.Abs(diff))} (Ziel größer)[/]";
        }

        table.AddRow("Dateigröße", sourceCol, targetCol, diffCol);
    }

    private static void AddTimestampRow(Table table, FileConflict conflict)
    {
        var sourceTime = conflict.SourceFile.LastWriteAtUtc.ToLocalTime();
        var targetTime = conflict.TargetFile.LastWriteAtUtc.ToLocalTime();
        var ageDiff = conflict.AgeDifference;

        string sourceCol;
        string targetCol;
        string diffCol;

        if (Math.Abs(ageDiff.TotalSeconds) < TimestampIdenticalToleranceSeconds)
        {
            sourceCol = sourceTime.ToString("yyyy-MM-dd HH:mm:ss");
            targetCol = targetTime.ToString("yyyy-MM-dd HH:mm:ss");
            diffCol = "[dim]Identisch[/]";
        }
        else if (ageDiff > TimeSpan.Zero)
        {
            sourceCol = $"[green]{sourceTime:yyyy-MM-dd HH:mm:ss}[/]";
            targetCol = $"[grey]{targetTime:yyyy-MM-dd HH:mm:ss}[/]";
            diffCol = $"[green]Quelle neuer (+{FormatTimeSpan(ageDiff)})[/]";
        }
        else
        {
            sourceCol = $"[grey]{sourceTime:yyyy-MM-dd HH:mm:ss}[/]";
            targetCol = $"[green]{targetTime:yyyy-MM-dd HH:mm:ss}[/]";
            diffCol = $"[grey]Ziel neuer (+{FormatTimeSpan(-ageDiff)})[/]";
        }

        table.AddRow("Änderungsdatum", sourceCol, targetCol, diffCol);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0)
        {
            return "0 B";
        }

        var absBytes = Math.Abs(bytes);
        var place = Convert.ToInt32(Math.Floor(Math.Log(absBytes, 1024)));
        if (place >= suffixes.Length)
        {
            place = suffixes.Length - 1;
        }

        var num = Math.Round(absBytes / Math.Pow(1024, place), 1);
        var sign = bytes < 0 ? "-" : string.Empty;
        var formatted = num.ToString("0.#", CultureInfo.InvariantCulture);
        return $"{sign}{formatted} {suffixes[place]}";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        var abs = timeSpan.Duration();
        if (abs.TotalDays >= 1)
        {
            return $"{(int)abs.TotalDays}d {abs.Hours}h";
        }

        if (abs.TotalHours >= 1)
        {
            return $"{(int)abs.TotalHours}h {abs.Minutes}m";
        }

        if (abs.TotalMinutes >= 1)
        {
            return $"{(int)abs.TotalMinutes}m {abs.Seconds}s";
        }

        return $"{(int)abs.TotalSeconds}s";
    }

    #endregion

    #region Private Helpers - Summary

    private static void AddSummaryRows(Table table, MergeSummary summary)
    {
        var modeValue = summary.IsDryRun ? "[yellow]Simulation (--dry-run)[/]" : "[green]Normal[/]";
        table.AddRow("Modus", modeValue);
        table.AddRow("Gescannte Dateien", summary.TotalFilesScanned.ToString());
        table.AddRow("Direkt verschoben", $"[green]{summary.FilesMovedDirectly}[/]");
        table.AddRow("Identische Duplikate (übersprungen)", $"[dim]{summary.IdenticalFilesSkipped}[/]");
        table.AddRow("Konflikte gelöst", $"[green]{summary.ConflictsResolved}[/]");

        var conflictsSkippedValue = summary.ConflictsSkipped > 0 ? $"[yellow]{summary.ConflictsSkipped}[/]" : "0";
        table.AddRow("Konflikte übersprungen", conflictsSkippedValue);

        var errorsValue = summary.ErrorsOccurred > 0 ? $"[red]{summary.ErrorsOccurred}[/]" : "[green]0[/]";
        table.AddRow("Fehler aufgetreten", errorsValue);

        var durationText = summary.ElapsedDuration.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";
        table.AddRow("Dauer", durationText);
    }

    #endregion
}
