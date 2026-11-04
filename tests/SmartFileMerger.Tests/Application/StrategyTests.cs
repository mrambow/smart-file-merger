namespace SmartFileMerger.Tests.Application;

using FluentAssertions;
using NSubstitute;
using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using Xunit;

public sealed class StrategyTests
{
    private readonly IFileSystemService _fileSystem = Substitute.For<IFileSystemService>();
    private readonly IUserInterface _ui = Substitute.For<IUserInterface>();

    private readonly DirectoryRoot _sourceRoot = new(@"C:\Source");
    private readonly DirectoryRoot _targetRoot = new(@"C:\Target");

    #region OverwriteTargetStrategy Tests

    [Fact]
    public void OverwriteTargetStrategy_Properties_ReturnExpectedMetadata()
    {
        // Arrange
        var strategy = new OverwriteTargetStrategy(_fileSystem);

        // Assert
        strategy.Key.Should().Be("overwrite");
        strategy.DisplayName.Should().Be("Ziel in B überschreiben (Quelle A gewinnt)");
    }

    [Fact]
    public void OverwriteTargetStrategy_Constructor_WhenFileSystemIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new OverwriteTargetStrategy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OverwriteTargetStrategy_Execute_WhenArgumentsAreNull_ThrowsArgumentNullException()
    {
        // Arrange
        var strategy = new OverwriteTargetStrategy(_fileSystem);
        var conflict = CreateConflict("document.txt");

        // Act & Assert
        var actConflictNull = () => strategy.Execute(null!, _sourceRoot, _targetRoot);
        var actSourceNull = () => strategy.Execute(conflict, null!, _targetRoot);
        var actTargetNull = () => strategy.Execute(conflict, _sourceRoot, null!);

        actConflictNull.Should().Throw<ArgumentNullException>();
        actSourceNull.Should().Throw<ArgumentNullException>();
        actTargetNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OverwriteTargetStrategy_Execute_MovesSourceToTargetWithOverwriteFlag()
    {
        // Arrange
        var strategy = new OverwriteTargetStrategy(_fileSystem);
        var conflict = CreateConflict("report.pdf");

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _fileSystem.Received(1).MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: true);
    }

    #endregion

    #region MoveOrRenameTargetStrategy Tests

    [Fact]
    public void MoveOrRenameTargetStrategy_Properties_ReturnExpectedMetadata()
    {
        // Arrange
        var strategy = new MoveOrRenameTargetStrategy(_fileSystem, _ui);

        // Assert
        strategy.Key.Should().Be("backup_target");
        strategy.DisplayName.Should().Be("Bestehendes Ziel B sichern/umbenennen, dann Quelle A nachziehen");
    }

    [Fact]
    public void MoveOrRenameTargetStrategy_Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var actFsNull = () => new MoveOrRenameTargetStrategy(null!, _ui);
        var actUiNull = () => new MoveOrRenameTargetStrategy(_fileSystem, null!);

        actFsNull.Should().Throw<ArgumentNullException>();
        actUiNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MoveOrRenameTargetStrategy_Execute_WhenArgumentsAreNull_ThrowsArgumentNullException()
    {
        // Arrange
        var strategy = new MoveOrRenameTargetStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("document.txt");

        // Act & Assert
        var actConflictNull = () => strategy.Execute(null!, _sourceRoot, _targetRoot);
        var actSourceNull = () => strategy.Execute(conflict, null!, _targetRoot);
        var actTargetNull = () => strategy.Execute(conflict, _sourceRoot, null!);

        actConflictNull.Should().Throw<ArgumentNullException>();
        actSourceNull.Should().Throw<ArgumentNullException>();
        actTargetNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MoveOrRenameTargetStrategy_Execute_PromptsCustomPathAndMovesTargetToBackupThenSourceToTarget()
    {
        // Arrange
        var strategy = new MoveOrRenameTargetStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("data.csv");
        const string chosenBackupPath = @"C:\Target\data_backup_custom.csv";

        _ui.PromptForCustomTargetFilePath(Arg.Any<string>()).Returns(chosenBackupPath);

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        Received.InOrder(() =>
        {
            _ui.PromptForCustomTargetFilePath(Arg.Is<string>(p => p.Contains("data") && p.Contains("backup")));
            _fileSystem.MoveFile(conflict.TargetFile.FullPath, chosenBackupPath, overwrite: false);
            _fileSystem.MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: false);
        });
    }

    [Fact]
    public void MoveOrRenameTargetStrategy_Execute_WhenNestedPath_ConstructsNestedDefaultBackupPath()
    {
        // Arrange
        var strategy = new MoveOrRenameTargetStrategy(_fileSystem, _ui);
        var conflict = CreateConflict(@"sub\nested\data.csv");
        string capturedDefaultPath = string.Empty;

        _ui.PromptForCustomTargetFilePath(Arg.Do<string>(p => capturedDefaultPath = p))
            .Returns(@"C:\Target\sub\nested\data_backup_custom.csv");

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        capturedDefaultPath.Should().StartWith(@"C:\Target\sub\nested\data_backup_");
        capturedDefaultPath.Should().EndWith(".csv");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveOrRenameTargetStrategy_Execute_WhenPromptReturnsNullOrWhitespace_FallsBackToDefaultBackupPath(string? blankInput)
    {
        // Arrange
        var strategy = new MoveOrRenameTargetStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("data.csv");
        string capturedDefaultPath = string.Empty;

        _ui.PromptForCustomTargetFilePath(Arg.Do<string>(p => capturedDefaultPath = p))
            .Returns(blankInput!);

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _fileSystem.Received(1).MoveFile(conflict.TargetFile.FullPath, capturedDefaultPath, overwrite: false);
        _fileSystem.Received(1).MoveFile(conflict.SourceFile.FullPath, conflict.TargetFile.FullPath, overwrite: false);
    }

    #endregion

    #region RenameSourceStrategy Tests

    [Fact]
    public void RenameSourceStrategy_Properties_ReturnExpectedMetadata()
    {
        // Arrange
        var strategy = new RenameSourceStrategy(_fileSystem, _ui);

        // Assert
        strategy.Key.Should().Be("rename_source");
        strategy.DisplayName.Should().Be("Quelle A umbenennen und nach B schieben");
    }

    [Fact]
    public void RenameSourceStrategy_Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var actFsNull = () => new RenameSourceStrategy(null!, _ui);
        var actUiNull = () => new RenameSourceStrategy(_fileSystem, null!);

        actFsNull.Should().Throw<ArgumentNullException>();
        actUiNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenameSourceStrategy_Execute_WhenArgumentsAreNull_ThrowsArgumentNullException()
    {
        // Arrange
        var strategy = new RenameSourceStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("document.txt");

        // Act & Assert
        var actConflictNull = () => strategy.Execute(null!, _sourceRoot, _targetRoot);
        var actSourceNull = () => strategy.Execute(conflict, null!, _targetRoot);
        var actTargetNull = () => strategy.Execute(conflict, _sourceRoot, null!);

        actConflictNull.Should().Throw<ArgumentNullException>();
        actSourceNull.Should().Throw<ArgumentNullException>();
        actTargetNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenameSourceStrategy_Execute_PromptsNewFileNameAndMovesSourceToNewTargetPath()
    {
        // Arrange
        var strategy = new RenameSourceStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("image.png");
        const string chosenNewFileName = "image_v2.png";

        _ui.PromptForNewFileName(Arg.Any<string>(), Arg.Any<string>()).Returns(chosenNewFileName);

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _ui.Received(1).PromptForNewFileName("image_from_source.png", Arg.Any<string>());
        _fileSystem.Received(1).MoveFile(conflict.SourceFile.FullPath, @"C:\Target\image_v2.png", overwrite: false);
    }

    [Fact]
    public void RenameSourceStrategy_Execute_WhenNestedPath_BuildsNestedTargetPath()
    {
        // Arrange
        var strategy = new RenameSourceStrategy(_fileSystem, _ui);
        var conflict = CreateConflict(@"deep\folder\archive.zip");
        const string chosenNewFileName = "archive_renamed.zip";

        _ui.PromptForNewFileName(Arg.Any<string>(), Arg.Any<string>()).Returns(chosenNewFileName);

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _ui.Received(1).PromptForNewFileName("archive_from_source.zip", Arg.Any<string>());
        _fileSystem.Received(1).MoveFile(conflict.SourceFile.FullPath, @"C:\Target\deep\folder\archive_renamed.zip", overwrite: false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameSourceStrategy_Execute_WhenPromptReturnsNullOrWhitespace_FallsBackToSuggestedFileName(string? blankInput)
    {
        // Arrange
        var strategy = new RenameSourceStrategy(_fileSystem, _ui);
        var conflict = CreateConflict("image.png");

        _ui.PromptForNewFileName(Arg.Any<string>(), Arg.Any<string>()).Returns(blankInput!);

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _fileSystem.Received(1).MoveFile(conflict.SourceFile.FullPath, @"C:\Target\image_from_source.png", overwrite: false);
    }

    #endregion

    #region SkipConflictStrategy Tests

    [Fact]
    public void SkipConflictStrategy_Properties_ReturnExpectedMetadata()
    {
        // Arrange
        var strategy = new SkipConflictStrategy();

        // Assert
        strategy.Key.Should().Be("skip");
        strategy.DisplayName.Should().Be("Überspringen (Datei in A belassen)");
    }

    [Fact]
    public void SkipConflictStrategy_Execute_DoesNotCallFileSystem()
    {
        // Arrange
        var strategy = new SkipConflictStrategy();
        var conflict = CreateConflict("test.txt");

        // Act
        strategy.Execute(conflict, _sourceRoot, _targetRoot);

        // Assert
        _fileSystem.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default);
        _fileSystem.DidNotReceiveWithAnyArgs().DeleteFile(default!);
    }

    #endregion

    #region Helper Methods

    private FileConflict CreateConflict(string relativePathString)
    {
        var relativePath = new RelativeFilePath(relativePathString);
        var sourceMetadata = new FileMetadata(
            _sourceRoot.CombineWith(relativePath),
            1024,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow);
        var targetMetadata = new FileMetadata(
            _targetRoot.CombineWith(relativePath),
            2048,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddMinutes(-30));

        return new FileConflict(relativePath, sourceMetadata, targetMetadata);
    }

    #endregion
}
