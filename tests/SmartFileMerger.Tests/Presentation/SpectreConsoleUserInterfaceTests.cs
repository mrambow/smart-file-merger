namespace SmartFileMerger.Tests.Presentation;

using FluentAssertions;
using NSubstitute;
using Spectre.Console.Testing;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Presentation;
using Xunit;

public class SpectreConsoleUserInterfaceTests
{
    private readonly TestConsole _console;
    private readonly IFileSystemService _fileSystem;
    private readonly SpectreConsoleUserInterface _sut;

    public SpectreConsoleUserInterfaceTests()
    {
        _console = new TestConsole();
        _console.Profile.Width = 120;
        _console.Profile.Capabilities.Interactive = true;
        _fileSystem = Substitute.For<IFileSystemService>();
        _sut = new SpectreConsoleUserInterface(_console, _fileSystem);
    }

    #region ShowHeader Tests

    [Fact]
    public void ShowHeader_WhenCalled_RendersAppTitleAndSubtitle()
    {
        // Act
        _sut.ShowHeader();

        // Assert
        _console.Output.Should().NotBeNullOrWhiteSpace();
        _console.Output.Should().Contain("Intelligenter Verzeichnis-Merger");
    }

    #endregion

    #region ShowUpdateNotice Tests

    [Fact]
    public void ShowUpdateNotice_WhenCalled_RendersPanelWithVersionAndUrl()
    {
        // Arrange
        const string version = "2.1.0";
        const string url = "https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0";

        // Act
        _sut.ShowUpdateNotice(version, url);

        // Assert
        _console.Output.Should().Contain("2.1.0");
        _console.Output.Should().Contain(url);
        _console.Output.Should().Contain("Update verfügbar");
    }

    [Theory]
    [InlineData(null, "https://github.com")]
    [InlineData("", "https://github.com")]
    [InlineData("   ", "https://github.com")]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0", "")]
    [InlineData("1.0.0", "   ")]
    public void ShowUpdateNotice_WhenInvalidArguments_ThrowsArgumentException(string? version, string? url)
    {
        // Act
        var act = () => _sut.ShowUpdateNotice(version!, url!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region PromptForInstallation Tests

    [Fact]
    public void PromptForInstallation_WhenUserConfirms_OutputsPromptAndReturnsTrue()
    {
        // Arrange
        _console.Input.PushTextWithEnter("y");

        // Act
        var result = _sut.PromptForInstallation();

        // Assert
        result.Should().BeTrue();
        _console.Output.Should().Contain("Möchtest du SmartFileMerger installieren");
        _console.Output.Should().Contain("Desktop- und Startmenü-Verknüpfung anlegen");
    }

    [Fact]
    public void PromptForInstallation_WhenUserDeclines_OutputsPromptAndReturnsFalse()
    {
        // Arrange
        _console.Input.PushTextWithEnter("n");

        // Act
        var result = _sut.PromptForInstallation();

        // Assert
        result.Should().BeFalse();
        _console.Output.Should().Contain("Möchtest du SmartFileMerger installieren");
    }

    #endregion

    #region PromptForUpdateInstallation Tests

    [Fact]
    public void PromptForUpdateInstallation_WhenUserConfirms_OutputsEscapedVersionAndReturnsTrue()
    {
        // Arrange
        const string version = "2.2.0";
        _console.Input.PushTextWithEnter("y");

        // Act
        var result = _sut.PromptForUpdateInstallation(version);

        // Assert
        result.Should().BeTrue();
        _console.Output.Should().Contain("Ein neues Update");
        _console.Output.Should().Contain("2.2.0");
        _console.Output.Should().Contain("Jetzt automatisch herunterladen und installieren?");
    }

    [Fact]
    public void PromptForUpdateInstallation_WhenUserDeclines_ReturnsFalse()
    {
        // Arrange
        const string version = "2.2.0";
        _console.Input.PushTextWithEnter("n");

        // Act
        var result = _sut.PromptForUpdateInstallation(version);

        // Assert
        result.Should().BeFalse();
        _console.Output.Should().Contain("2.2.0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PromptForUpdateInstallation_WhenVersionNullOrWhiteSpace_ThrowsArgumentException(string? invalidVersion)
    {
        // Act
        var act = () => _sut.PromptForUpdateInstallation(invalidVersion!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RunWithDownloadProgress Tests

    [Fact]
    public void RunWithDownloadProgress_WhenExecutedWithRatio_CallsWorkAndReportsProgress()
    {
        // Arrange
        const string version = "2.2.0";
        var progressValues = new List<double>();

        // Act
        _sut.RunWithDownloadProgress(version, async reportProgress =>
        {
            reportProgress(0.25);
            progressValues.Add(0.25);

            reportProgress(0.75);
            progressValues.Add(0.75);

            await Task.CompletedTask;
        });

        // Assert
        progressValues.Should().ContainInOrder(0.25, 0.75);
        _console.Output.Should().Contain("2.2.0");
    }

    [Fact]
    public void RunWithDownloadProgress_WhenExecutedWithPercentage_CallsWorkAndReportsProgress()
    {
        // Arrange
        const string version = "3.0.0";
        var progressValues = new List<double>();

        // Act
        _sut.RunWithDownloadProgress(version, async reportProgress =>
        {
            reportProgress(50.0);
            progressValues.Add(50.0);

            reportProgress(100.0);
            progressValues.Add(100.0);

            await Task.CompletedTask;
        });

        // Assert
        progressValues.Should().ContainInOrder(50.0, 100.0);
        _console.Output.Should().Contain("3.0.0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RunWithDownloadProgress_WhenVersionNullOrWhiteSpace_ThrowsArgumentException(string? invalidVersion)
    {
        // Act
        var act = () => _sut.RunWithDownloadProgress(invalidVersion!, _ => Task.CompletedTask);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RunWithDownloadProgress_WhenDownloadWorkNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.RunWithDownloadProgress("2.2.0", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ShowInfo / ShowWarning / ShowError Tests

    [Fact]
    public void ShowInfo_WhenCalled_EscapesMarkupAndOutputsBlueInfo()
    {
        // Arrange
        const string message = "Vorgang [Test] erfolgreich!";

        // Act
        _sut.ShowInfo(message);

        // Assert
        _console.Output.Should().Contain("[INFO]");
        _console.Output.Should().Contain("Vorgang [Test] erfolgreich!");
    }

    [Fact]
    public void ShowWarning_WhenCalled_EscapesMarkupAndOutputsYellowWarn()
    {
        // Arrange
        const string message = "Achtung vor [Sonderzeichen]!";

        // Act
        _sut.ShowWarning(message);

        // Assert
        _console.Output.Should().Contain("[WARN]");
        _console.Output.Should().Contain("Achtung vor [Sonderzeichen]!");
    }

    [Fact]
    public void ShowError_WhenCalled_EscapesMarkupAndOutputsRedError()
    {
        // Arrange
        const string message = "Fehler bei [Datei 1] aufgetreten.";

        // Act
        _sut.ShowError(message);

        // Assert
        _console.Output.Should().Contain("[ERROR]");
        _console.Output.Should().Contain("Fehler bei [Datei 1] aufgetreten.");
    }

    #endregion

    #region ConfirmMerge Tests

    [Fact]
    public void ConfirmMerge_WhenUserConfirms_RendersSummaryTableAndReturnsTrue()
    {
        // Arrange
        var source = new DirectoryRoot(@"C:\Source");
        var target = new DirectoryRoot(@"C:\Target");
        _console.Input.PushTextWithEnter("y");

        // Act
        var result = _sut.ConfirmMerge(source, target, isDryRun: false);

        // Assert
        result.Should().BeTrue();
        _console.Output.Should().Contain(@"C:\Source");
        _console.Output.Should().Contain(@"C:\Target");
        _console.Output.Should().Contain("Normal");
        _console.Output.Should().Contain("Möchtest du den Vorgang jetzt starten?");
    }

    [Fact]
    public void ConfirmMerge_WhenUserDeclines_RendersSimulationModeAndReturnsFalse()
    {
        // Arrange
        var source = new DirectoryRoot(@"C:\Source");
        var target = new DirectoryRoot(@"C:\Target");
        _console.Input.PushTextWithEnter("n");

        // Act
        var result = _sut.ConfirmMerge(source, target, isDryRun: true);

        // Assert
        result.Should().BeFalse();
        _console.Output.Should().Contain("Simulation (Dry-Run)");
    }

    #endregion

    #region RunWithProgress Tests

    [Fact]
    public void RunWithProgress_WhenExecuted_CallsWorkAndReportsProgress()
    {
        // Arrange
        var processedItems = new List<string>();

        // Act
        _sut.RunWithProgress(2, report =>
        {
            report("file1.txt", 1);
            processedItems.Add("file1.txt");

            report("file2.txt", 1);
            processedItems.Add("file2.txt");
        });

        // Assert
        processedItems.Should().ContainInOrder("file1.txt", "file2.txt");
    }

    #endregion

    #region PromptForNewFileName Tests

    [Fact]
    public void PromptForNewFileName_WhenUserInputProvided_ReturnsTrimmedInput()
    {
        // Arrange
        _console.Input.PushTextWithEnter("  new_document.pdf  ");

        // Act
        var result = _sut.PromptForNewFileName("default.pdf", "Neuer Name:");

        // Assert
        result.Should().Be("new_document.pdf");
    }

    [Fact]
    public void PromptForNewFileName_WhenUserInputBlank_ReturnsDefaultFileName()
    {
        // Arrange
        _console.Input.PushTextWithEnter("");

        // Act
        var result = _sut.PromptForNewFileName("fallback.pdf", "Neuer Name:");

        // Assert
        result.Should().Be("fallback.pdf");
    }

    #endregion

    #region PromptForCustomTargetFilePath Tests

    [Fact]
    public void PromptForCustomTargetFilePath_WhenUserInputWithQuotes_ReturnsStrippedPath()
    {
        // Arrange
        _console.Input.PushTextWithEnter("\"C:\\NewTarget\\custom.txt\"");

        // Act
        var result = _sut.PromptForCustomTargetFilePath(@"C:\Target\default.txt");

        // Assert
        result.Should().Be(@"C:\NewTarget\custom.txt");
    }

    [Fact]
    public void PromptForCustomTargetFilePath_WhenUserInputEmpty_ReturnsDefaultFullPath()
    {
        // Arrange
        _console.Input.PushTextWithEnter("");

        // Act
        var result = _sut.PromptForCustomTargetFilePath(@"C:\Target\default.txt");

        // Assert
        result.Should().Be(@"C:\Target\default.txt");
    }

    #endregion

    #region DisplayMergeSummary Tests

    [Fact]
    public void DisplayMergeSummary_WhenNormalExecution_RendersAllMetricsAndNormalBadge()
    {
        // Arrange
        var summary = new MergeSummary(
            TotalFilesScanned: 100,
            FilesMovedDirectly: 80,
            IdenticalFilesSkipped: 10,
            ConflictsResolved: 8,
            ConflictsSkipped: 2,
            ErrorsOccurred: 0,
            ElapsedDuration: TimeSpan.FromSeconds(3.45),
            IsDryRun: false);

        // Act
        _sut.DisplayMergeSummary(summary);

        // Assert
        _console.Output.Should().Contain("Normal");
        _console.Output.Should().Contain("100");
        _console.Output.Should().Contain("80");
        _console.Output.Should().Contain("10");
        _console.Output.Should().Contain("8");
        _console.Output.Should().Contain("2");
        _console.Output.Should().Contain("3.45 s");
    }

    [Fact]
    public void DisplayMergeSummary_WhenSimulationWithErrors_RendersSimulationBadgeAndErrorCount()
    {
        // Arrange
        var summary = new MergeSummary(
            TotalFilesScanned: 50,
            FilesMovedDirectly: 30,
            IdenticalFilesSkipped: 5,
            ConflictsResolved: 5,
            ConflictsSkipped: 0,
            ErrorsOccurred: 3,
            ElapsedDuration: TimeSpan.FromSeconds(1.20),
            IsDryRun: true);

        // Act
        _sut.DisplayMergeSummary(summary);

        // Assert
        _console.Output.Should().Contain("Simulation (--dry-run)");
        _console.Output.Should().Contain("3");
    }

    #endregion

    #region PromptForConflictResolution Tests

    [Fact]
    public void PromptForConflictResolution_WhenStrategySelected_DisplaysDiffTableAndReturnsStrategy()
    {
        // Arrange
        var strategy1 = Substitute.For<IConflictResolutionStrategy>();
        strategy1.DisplayName.Returns("Strategie 1: Überschreiben");
        var strategy2 = Substitute.For<IConflictResolutionStrategy>();
        strategy2.DisplayName.Returns("Strategie 2: Überspringen");

        var relativePath = new RelativeFilePath("documents/report.docx");
        var sourceFile = new FileMetadata(@"C:\Source\documents\report.docx", 2048, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        var targetFile = new FileMetadata(@"C:\Target\documents\report.docx", 1024, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddHours(-3));
        var conflict = new FileConflict(relativePath, sourceFile, targetFile);

        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var result = _sut.PromptForConflictResolution(conflict, 1, 1, new[] { strategy1, strategy2 });

        // Assert
        result.Should().Be(strategy1);
        _console.Output.Should().Contain(conflict.RelativePath.Value);
        _console.Output.Should().Contain("Konflikt 1 von 1");
        _console.Output.Should().Contain("Dateigröße");
        _console.Output.Should().Contain("Änderungsdatum");
        _console.Output.Should().Contain("Quelle größer");
    }

    [Fact]
    public void PromptForConflictResolution_WhenTargetIsNewerAndLarger_DisplaysTargetHighlighted()
    {
        // Arrange
        var strategy = Substitute.For<IConflictResolutionStrategy>();
        strategy.DisplayName.Returns("Skip");

        var relativePath = new RelativeFilePath("photo.jpg");
        var sourceFile = new FileMetadata(@"C:\Source\photo.jpg", 1000, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2));
        var targetFile = new FileMetadata(@"C:\Target\photo.jpg", 5000, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1));
        var conflict = new FileConflict(relativePath, sourceFile, targetFile);

        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var result = _sut.PromptForConflictResolution(conflict, 1, 5, new[] { strategy });

        // Assert
        result.Should().Be(strategy);
        _console.Output.Should().Contain("Ziel größer");
        _console.Output.Should().Contain("Ziel neuer");
    }

    [Fact]
    public void PromptForConflictResolution_WhenIdenticalMetadata_DisplaysIdenticalStatus()
    {
        // Arrange
        var strategy = Substitute.For<IConflictResolutionStrategy>();
        strategy.DisplayName.Returns("Skip");

        var timestamp = DateTime.UtcNow;
        var relativePath = new RelativeFilePath("data.csv");
        var sourceFile = new FileMetadata(@"C:\Source\data.csv", 4096, timestamp.AddHours(-1), timestamp);
        var targetFile = new FileMetadata(@"C:\Target\data.csv", 4096, timestamp.AddHours(-2), timestamp);
        var conflict = new FileConflict(relativePath, sourceFile, targetFile);

        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var result = _sut.PromptForConflictResolution(conflict, 2, 3, new[] { strategy });

        // Assert
        result.Should().Be(strategy);
        _console.Output.Should().Contain("Identisch");
    }

    #endregion

    #region PromptConfiguration Tests

    [Fact]
    public void PromptConfiguration_WhenValidInputsProvided_ReturnsConfiguredTuple()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Source").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Target").Returns(true);

        _console.Input.PushTextWithEnter("\"C:\\Source\"");
        _console.Input.PushTextWithEnter("\"C:\\Target\"");
        _console.Input.PushKey(ConsoleKey.Enter); // Normal mode

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\Source");
        target.FullPath.Should().Be(@"C:\Target");
        isDryRun.Should().BeFalse();
    }

    [Fact]
    public void PromptConfiguration_WhenTargetDoesNotExistAndUserConfirms_CreatesDirectoryAndReturnsDryRun()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Source").Returns(true);
        _fileSystem.DirectoryExists(@"C:\NewTarget").Returns(false);

        _console.Input.PushTextWithEnter(@"C:\Source");
        _console.Input.PushTextWithEnter(@"C:\NewTarget");
        _console.Input.PushTextWithEnter("y"); // Confirm create directory
        _console.Input.PushKey(ConsoleKey.DownArrow); // Select Simulation
        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\Source");
        target.FullPath.Should().Be(@"C:\NewTarget");
        isDryRun.Should().BeTrue();
        _fileSystem.Received(1).CreateDirectory(@"C:\NewTarget");
    }

    [Fact]
    public void PromptConfiguration_WhenTargetIsSubdirectoryOfSource_RejectsUntilValidTargetProvided()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Source").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Source\SubFolder").Returns(true);
        _fileSystem.DirectoryExists(@"C:\ValidTarget").Returns(true);

        _console.Input.PushTextWithEnter(@"C:\Source");
        _console.Input.PushTextWithEnter(@"C:\Source\SubFolder"); // Invalid: subfolder of source
        _console.Input.PushTextWithEnter(@"C:\ValidTarget");
        _console.Input.PushKey(ConsoleKey.Enter); // Normal mode

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\Source");
        target.FullPath.Should().Be(@"C:\ValidTarget");
        isDryRun.Should().BeFalse();
        _console.Output.Should().Contain("Zielverzeichnis darf kein Unterverzeichnis der Quelle sein");
    }

    [Fact]
    public void PromptConfiguration_WhenSourceDoesNotExist_RetriesUntilExistingDirectoryEntered()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\MissingSource").Returns(false);
        _fileSystem.DirectoryExists(@"C:\ExistingSource").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Target").Returns(true);

        _console.Input.PushTextWithEnter(@"C:\MissingSource");
        _console.Input.PushTextWithEnter(@"C:\ExistingSource");
        _console.Input.PushTextWithEnter(@"C:\Target");
        _console.Input.PushKey(ConsoleKey.Enter); // Normal mode

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\ExistingSource");
        target.FullPath.Should().Be(@"C:\Target");
        isDryRun.Should().BeFalse();
        _console.Output.Should().Contain("Quellverzeichnis existiert nicht");
    }

    [Fact]
    public void PromptConfiguration_WhenTargetIsIdenticalToSource_RetriesUntilDifferentTargetProvided()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Source").Returns(true);
        _fileSystem.DirectoryExists(@"C:\DifferentTarget").Returns(true);

        _console.Input.PushTextWithEnter(@"C:\Source");
        _console.Input.PushTextWithEnter(@"C:\Source"); // Same as source
        _console.Input.PushTextWithEnter(@"C:\DifferentTarget");
        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\Source");
        target.FullPath.Should().Be(@"C:\DifferentTarget");
        _console.Output.Should().Contain("Zielverzeichnis darf nicht mit dem Quellverzeichnis identisch sein");
    }

    [Fact]
    public void PromptConfiguration_WhenTargetCreationDeclined_PromptsForDifferentTarget()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Source").Returns(true);
        _fileSystem.DirectoryExists(@"C:\NonExistentTarget").Returns(false);
        _fileSystem.DirectoryExists(@"C:\ExistingTarget").Returns(true);

        _console.Input.PushTextWithEnter(@"C:\Source");
        _console.Input.PushTextWithEnter(@"C:\NonExistentTarget");
        _console.Input.PushTextWithEnter("n"); // Decline create
        _console.Input.PushTextWithEnter(@"C:\ExistingTarget");
        _console.Input.PushKey(ConsoleKey.Enter);

        // Act
        var (source, target, isDryRun) = _sut.PromptConfiguration();

        // Assert
        source.FullPath.Should().Be(@"C:\Source");
        target.FullPath.Should().Be(@"C:\ExistingTarget");
        _fileSystem.DidNotReceive().CreateDirectory(@"C:\NonExistentTarget");
        _console.Output.Should().Contain("Bitte gib ein anderes Zielverzeichnis an");
    }

    #endregion

    #region Guard Clause Tests

    [Fact]
    public void ConfirmMerge_WhenSourceOrTargetNull_ThrowsArgumentNullException()
    {
        // Arrange
        var valid = new DirectoryRoot(@"C:\Valid");

        // Act
        var actSource = () => _sut.ConfirmMerge(null!, valid, false);
        var actTarget = () => _sut.ConfirmMerge(valid, null!, false);

        // Assert
        actSource.Should().Throw<ArgumentNullException>();
        actTarget.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RunWithProgress_WhenWorkNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.RunWithProgress(10, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PromptForConflictResolution_WhenArgumentsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var conflict = new FileConflict(
            new RelativeFilePath("test.txt"),
            new FileMetadata(@"C:\A\test.txt", 10, DateTime.UtcNow, DateTime.UtcNow),
            new FileMetadata(@"C:\B\test.txt", 20, DateTime.UtcNow, DateTime.UtcNow));

        // Act
        var actConflict = () => _sut.PromptForConflictResolution(null!, 1, 1, Array.Empty<IConflictResolutionStrategy>());
        var actStrategies = () => _sut.PromptForConflictResolution(conflict, 1, 1, null!);

        // Assert
        actConflict.Should().Throw<ArgumentNullException>();
        actStrategies.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DisplayMergeSummary_WhenSummaryNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.DisplayMergeSummary(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
