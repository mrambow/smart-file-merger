namespace SmartFileMerger.Tests.Application;

using FluentAssertions;
using NSubstitute;
using SmartFileMerger.Application;
using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using Xunit;

public sealed class MergeOrchestratorTests
{
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly IUserInterface _ui = Substitute.For<IUserInterface>();
    private readonly ConflictResolutionRunner _conflictRunner;
    private readonly List<IConflictResolutionStrategy> _strategies;
    private readonly MergeOrchestrator _orchestrator;

    private readonly DirectoryRoot _source = new(@"C:\Source");
    private readonly DirectoryRoot _target = new(@"C:\Target");

    public MergeOrchestratorTests()
    {
        _conflictRunner = new ConflictResolutionRunner(_ui);
        _strategies =
        [
            new OverwriteTargetStrategy(_fileSystem),
            new MoveOrRenameTargetStrategy(_fileSystem, _ui),
            new RenameSourceStrategy(_fileSystem, _ui),
            new SkipConflictStrategy()
        ];

        _orchestrator = new MergeOrchestrator(_fileSystem, _ui, _conflictRunner, _strategies);

        // Default: Source & Target directories exist
        _fileSystem.DirectoryExists(_source.FullPath).Returns(true);
        _fileSystem.DirectoryExists(_target.FullPath).Returns(true);

        // Default: Execute progress callback synchronously
        _ui.When(x => x.RunWithProgress(Arg.Any<int>(), Arg.Any<Action<Action<string, int>>>()))
           .Do(callInfo =>
           {
               var work = callInfo.Arg<Action<Action<string, int>>>();
               work((_, _) => { });
           });
    }

    #region Constructor Guard Tests

    [Fact]
    public void Constructor_WhenAnyDependencyIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var actFsNull = () => new MergeOrchestrator(null!, _ui, _conflictRunner, _strategies);
        var actUiNull = () => new MergeOrchestrator(_fileSystem, null!, _conflictRunner, _strategies);
        var actRunnerNull = () => new MergeOrchestrator(_fileSystem, _ui, null!, _strategies);
        var actStrategiesNull = () => new MergeOrchestrator(_fileSystem, _ui, _conflictRunner, null!);

        actFsNull.Should().Throw<ArgumentNullException>();
        actUiNull.Should().Throw<ArgumentNullException>();
        actRunnerNull.Should().Throw<ArgumentNullException>();
        actStrategiesNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConflictResolutionRunner_Constructor_WhenUiIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ConflictResolutionRunner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Directory Validation Tests

    [Fact]
    public async Task ExecuteAsync_WhenSourceDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        _fileSystem.DirectoryExists(_source.FullPath).Returns(false);

        // Act
        var act = () => _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage($"*{_source.FullPath}*");
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceAndTargetAreIdentical_ThrowsArgumentException()
    {
        // Arrange
        var sameTarget = new DirectoryRoot(_source.FullPath);

        // Act
        var act = () => _orchestrator.ExecuteAsync(_source, sameTarget, isDryRun: false);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*identisch*");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetIsSubdirectoryOfSource_ThrowsArgumentException()
    {
        // Arrange
        var nestedTarget = new DirectoryRoot(@"C:\Source\SubFolder\Target");

        // Act
        var act = () => _orchestrator.ExecuteAsync(_source, nestedTarget, isDryRun: false);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unterordner*");
    }

    [Fact]
    public async Task ExecuteAsync_WhenArgumentsAreNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var actSourceNull = () => _orchestrator.ExecuteAsync(null!, _target, isDryRun: false);
        var actTargetNull = () => _orchestrator.ExecuteAsync(_source, null!, isDryRun: false);

        await actSourceNull.Should().ThrowAsync<ArgumentNullException>();
        await actTargetNull.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetDirectoryDoesNotExistAndNotDryRun_CreatesTargetDirectory()
    {
        // Arrange
        _fileSystem.DirectoryExists(_target.FullPath).Returns(false);
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([]);

        // Act
        await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.Received(1).CreateDirectory(_target.FullPath);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetDirectoryDoesNotExistAndIsDryRun_DoesNotCreateTargetDirectory()
    {
        // Arrange
        _fileSystem.DirectoryExists(_target.FullPath).Returns(false);
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([]);

        // Act
        await _orchestrator.ExecuteAsync(_source, _target, isDryRun: true);

        // Assert
        _fileSystem.DidNotReceive().CreateDirectory(Arg.Any<string>());
    }

    #endregion

    #region Direct Move Tests

    [Fact]
    public async Task ExecuteAsync_WhenFileHasNoConflict_MovesDirectly()
    {
        // Arrange
        var relPath = new RelativeFilePath("documents/report.pdf");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath]);
        _fileSystem.FileExists(@"C:\Target\documents\report.pdf").Returns(false);

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.Received(1).EnsureDirectoryExistsForFile(@"C:\Target\documents\report.pdf");
        _fileSystem.Received(1).MoveFile(@"C:\Source\documents\report.pdf", @"C:\Target\documents\report.pdf", overwrite: false);

        summary.TotalFilesScanned.Should().Be(1);
        summary.FilesMovedDirectly.Should().Be(1);
        summary.IdenticalFilesSkipped.Should().Be(0);
        summary.ConflictsResolved.Should().Be(0);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
        summary.IsDryRun.Should().BeFalse();
    }

    #endregion

    #region Identical File Auto-Detection Tests

    [Fact]
    public async Task ExecuteAsync_WhenFileIsIdenticalDuplicate_DeletesSourceFileAndIncrementsSkipped()
    {
        // Arrange
        var relPath = new RelativeFilePath("photo.jpg");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath]);
        _fileSystem.FileExists(@"C:\Target\photo.jpg").Returns(true);

        var timestamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        _fileSystem.GetMetadata(@"C:\Source\photo.jpg")
            .Returns(new FileMetadata(@"C:\Source\photo.jpg", 1024, timestamp, timestamp));
        _fileSystem.GetMetadata(@"C:\Target\photo.jpg")
            .Returns(new FileMetadata(@"C:\Target\photo.jpg", 1024, timestamp, timestamp));

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.Received(1).DeleteFile(@"C:\Source\photo.jpg");
        _fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());

        summary.TotalFilesScanned.Should().Be(1);
        summary.FilesMovedDirectly.Should().Be(0);
        summary.IdenticalFilesSkipped.Should().Be(1);
        summary.ConflictsResolved.Should().Be(0);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
    }

    #endregion

    #region Non-Identical Conflict Queuing & Resolution Tests

    [Fact]
    public async Task ExecuteAsync_WhenNonIdenticalConflictAndUserResolves_IncrementsConflictsResolved()
    {
        // Arrange
        var relPath = new RelativeFilePath("data.csv");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath]);
        _fileSystem.FileExists(@"C:\Target\data.csv").Returns(true);

        var timestamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        _fileSystem.GetMetadata(@"C:\Source\data.csv")
            .Returns(new FileMetadata(@"C:\Source\data.csv", 2048, timestamp, timestamp));
        _fileSystem.GetMetadata(@"C:\Target\data.csv")
            .Returns(new FileMetadata(@"C:\Target\data.csv", 1024, timestamp, timestamp));

        // User picks OverwriteTargetStrategy
        var overwriteStrategy = _strategies.First(s => s.Key == OverwriteTargetStrategy.StrategyKey);
        _ui.PromptForConflictResolution(Arg.Any<FileConflict>(), 1, 1, Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
           .Returns(overwriteStrategy);

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.Received(1).MoveFile(@"C:\Source\data.csv", @"C:\Target\data.csv", overwrite: true);

        summary.TotalFilesScanned.Should().Be(1);
        summary.FilesMovedDirectly.Should().Be(0);
        summary.IdenticalFilesSkipped.Should().Be(0);
        summary.ConflictsResolved.Should().Be(1);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNonIdenticalConflictAndUserSkips_IncrementsConflictsSkipped()
    {
        // Arrange
        var relPath = new RelativeFilePath("data.csv");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath]);
        _fileSystem.FileExists(@"C:\Target\data.csv").Returns(true);

        var timestamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        _fileSystem.GetMetadata(@"C:\Source\data.csv")
            .Returns(new FileMetadata(@"C:\Source\data.csv", 2048, timestamp, timestamp));
        _fileSystem.GetMetadata(@"C:\Target\data.csv")
            .Returns(new FileMetadata(@"C:\Target\data.csv", 1024, timestamp, timestamp));

        // User picks SkipConflictStrategy
        var skipStrategy = _strategies.First(s => s.Key == SkipConflictStrategy.StrategyKey);
        _ui.PromptForConflictResolution(Arg.Any<FileConflict>(), 1, 1, Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
           .Returns(skipStrategy);

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        _fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());

        summary.TotalFilesScanned.Should().Be(1);
        summary.FilesMovedDirectly.Should().Be(0);
        summary.IdenticalFilesSkipped.Should().Be(0);
        summary.ConflictsResolved.Should().Be(0);
        summary.ConflictsSkipped.Should().Be(1);
        summary.ErrorsOccurred.Should().Be(0);
    }

    #endregion

    #region Fault Tolerance Tests

    [Fact]
    public async Task ExecuteAsync_WhenMoveThrowsIOException_CatchesErrorAndContinuesProcessing()
    {
        // Arrange
        var relPath1 = new RelativeFilePath("locked.txt");
        var relPath2 = new RelativeFilePath("success.txt");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath1, relPath2]);

        _fileSystem.FileExists(@"C:\Target\locked.txt").Returns(false);
        _fileSystem.FileExists(@"C:\Target\success.txt").Returns(false);

        _fileSystem.When(x => x.MoveFile(@"C:\Source\locked.txt", @"C:\Target\locked.txt", overwrite: false))
                   .Do(_ => throw new IOException("File locked by process"));

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _ui.Received(1).ShowWarning(Arg.Is<string>(msg => msg.Contains("locked.txt") && msg.Contains("File locked by process")));
        _fileSystem.Received(1).MoveFile(@"C:\Source\success.txt", @"C:\Target\success.txt", overwrite: false);

        summary.TotalFilesScanned.Should().Be(2);
        summary.FilesMovedDirectly.Should().Be(1);
        summary.ErrorsOccurred.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMoveThrowsUnauthorizedAccessException_CatchesErrorAndIncrementsErrors()
    {
        // Arrange
        var relPath = new RelativeFilePath("forbidden.txt");
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relPath]);
        _fileSystem.FileExists(@"C:\Target\forbidden.txt").Returns(false);

        _fileSystem.When(x => x.MoveFile(@"C:\Source\forbidden.txt", @"C:\Target\forbidden.txt", overwrite: false))
                   .Do(_ => throw new UnauthorizedAccessException("Access denied"));

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _ui.Received(1).ShowWarning(Arg.Is<string>(msg => msg.Contains("forbidden.txt") && msg.Contains("Access denied")));
        summary.ErrorsOccurred.Should().Be(1);
        summary.FilesMovedDirectly.Should().Be(0);
    }

    #endregion

    #region Dry-Run Mode Tests

    [Fact]
    public async Task ExecuteAsync_WhenIsDryRunIsTrue_DoesNotPerformAnyWriteOperations()
    {
        // Arrange: 1 direct move, 1 identical skip, 1 conflict to resolve
        var relDirect = new RelativeFilePath("direct.txt");
        var relIdentical = new RelativeFilePath("identical.txt");
        var relConflict = new RelativeFilePath("conflict.txt");

        _fileSystem.GetAllRelativeFilePaths(_source).Returns([relDirect, relIdentical, relConflict]);

        _fileSystem.FileExists(@"C:\Target\direct.txt").Returns(false);
        _fileSystem.FileExists(@"C:\Target\identical.txt").Returns(true);
        _fileSystem.FileExists(@"C:\Target\conflict.txt").Returns(true);

        var timestamp = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        _fileSystem.GetMetadata(@"C:\Source\identical.txt")
            .Returns(new FileMetadata(@"C:\Source\identical.txt", 100, timestamp, timestamp));
        _fileSystem.GetMetadata(@"C:\Target\identical.txt")
            .Returns(new FileMetadata(@"C:\Target\identical.txt", 100, timestamp, timestamp));

        _fileSystem.GetMetadata(@"C:\Source\conflict.txt")
            .Returns(new FileMetadata(@"C:\Source\conflict.txt", 200, timestamp, timestamp));
        _fileSystem.GetMetadata(@"C:\Target\conflict.txt")
            .Returns(new FileMetadata(@"C:\Target\conflict.txt", 100, timestamp, timestamp));

        var overwriteStrategy = _strategies.First(s => s.Key == OverwriteTargetStrategy.StrategyKey);
        _ui.PromptForConflictResolution(Arg.Any<FileConflict>(), 1, 1, Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
           .Returns(overwriteStrategy);

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: true);

        // Assert
        _fileSystem.DidNotReceive().CreateDirectory(Arg.Any<string>());
        _fileSystem.DidNotReceive().EnsureDirectoryExistsForFile(Arg.Any<string>());
        _fileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        _fileSystem.DidNotReceive().DeleteFile(Arg.Any<string>());
        _fileSystem.DidNotReceive().RemoveEmptyDirectories(Arg.Any<DirectoryRoot>());

        summary.TotalFilesScanned.Should().Be(3);
        summary.FilesMovedDirectly.Should().Be(1);
        summary.IdenticalFilesSkipped.Should().Be(1);
        summary.ConflictsResolved.Should().Be(1);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
        summary.IsDryRun.Should().BeTrue();
    }

    #endregion

    #region Directory Cleanup Tests

    [Fact]
    public async Task ExecuteAsync_WhenNotDryRun_RemovesEmptyDirectoriesInSource()
    {
        // Arrange
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([]);

        // Act
        await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        _fileSystem.Received(1).RemoveEmptyDirectories(_source);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemoveEmptyDirectoriesThrowsIOException_CatchesAndIncrementsErrors()
    {
        // Arrange
        _fileSystem.GetAllRelativeFilePaths(_source).Returns([]);
        _fileSystem.When(x => x.RemoveEmptyDirectories(_source))
                   .Do(_ => throw new IOException("Cannot remove folder"));

        // Act
        var summary = await _orchestrator.ExecuteAsync(_source, _target, isDryRun: false);

        // Assert
        summary.ErrorsOccurred.Should().Be(1);
        _ui.Received(1).ShowWarning(Arg.Is<string>(msg => msg.Contains("Cannot remove folder")));
    }

    #endregion

    #region ConflictResolutionRunner Specific Tests

    [Fact]
    public void ConflictResolutionRunner_ResolveConflicts_WhenArgumentsAreNull_ThrowsArgumentNullException()
    {
        // Arrange
        var conflict = new FileConflict(
            new RelativeFilePath("test.txt"),
            new FileMetadata(@"C:\Source\test.txt", 100, DateTime.UtcNow, DateTime.UtcNow),
            new FileMetadata(@"C:\Target\test.txt", 200, DateTime.UtcNow, DateTime.UtcNow));

        // Act & Assert
        var actConflictsNull = () => _conflictRunner.ResolveConflicts(null!, _source, _target, _strategies, false);
        var actSourceNull = () => _conflictRunner.ResolveConflicts([conflict], null!, _target, _strategies, false);
        var actTargetNull = () => _conflictRunner.ResolveConflicts([conflict], _source, null!, _strategies, false);
        var actStrategiesNull = () => _conflictRunner.ResolveConflicts([conflict], _source, _target, null!, false);

        actConflictsNull.Should().Throw<ArgumentNullException>();
        actSourceNull.Should().Throw<ArgumentNullException>();
        actTargetNull.Should().Throw<ArgumentNullException>();
        actStrategiesNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConflictResolutionRunner_ResolveConflicts_WhenEmptyList_ReturnsZero()
    {
        // Act
        var (resolved, skipped) = _conflictRunner.ResolveConflicts([], _source, _target, _strategies, false);

        // Assert
        resolved.Should().Be(0);
        skipped.Should().Be(0);
    }

    #endregion
}
