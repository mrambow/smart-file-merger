namespace SmartFileMerger.Tests;

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SmartFileMerger.Application;
using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Infrastructure;
using SmartFileMerger.Presentation;
using Xunit;

/// <summary>
/// Verifiziert den gesamten Merge-Workflow auf einem echten NTFS-Dateisystem inklusive
/// Dependency-Injection-Verdrahtung, Konfliktlösungsstrategien und Dry-Run-Garantie.
/// </summary>
public sealed class EndToEndWorkflowTests : IDisposable
{
    private const string UniqueFile1Name = "unique1.txt";
    private const string UniqueFile1Content = "Content of unique file 1";
    private const string UniqueFile2Relative = @"sub\unique2.txt";
    private const string UniqueFile2Content = "Content of unique file 2 in subfolder";
    private const string IdenticalFileName = "identical.txt";
    private const string IdenticalFileContent = "Exact identical content between source and target";
    private const string ConflictFileName = "conflict.txt";
    private const string TargetConflictContent = "Target version (short & older)";
    private const string SourceConflictContent = "Source version (longer content & newer timestamp)";
    private const string TargetOnlyFileName = "target_only.txt";
    private const string TargetOnlyContent = "Untouched existing target file";
    private const string EmptySubdirectoryName = "empty_dir";

    private readonly string _tempRoot;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly DateTime _identicalTimestamp = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    public EndToEndWorkflowTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SFM_E2E_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempRoot, "Source");
        _targetDir = Path.Combine(_tempRoot, "Target");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempRoot))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    #region Dependency Injection Wiring Tests

    [Fact]
    public void ConfigureServices_ResolvesAllRequiredDependenciesCleanly()
    {
        // Arrange & Act
        var services = Program.CreateServiceCollection();
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IFileSystemService>().Should().BeOfType<WindowsFileSystemService>();
        provider.GetRequiredService<IUserInterface>().Should().BeOfType<SpectreConsoleUserInterface>();
        provider.GetRequiredService<IUpdateCheckService>().Should().BeOfType<GitHubUpdateCheckService>();
        provider.GetRequiredService<IAppInstallationService>().Should().BeOfType<WindowsAppInstallationService>();
        provider.GetRequiredService<IUpdateDownloadService>().Should().BeOfType<WindowsUpdateDownloadService>();
        provider.GetRequiredService<IProcessLauncher>().Should().BeOfType<WindowsProcessLauncher>();
        provider.GetRequiredService<ConflictResolutionRunner>().Should().NotBeNull();
        provider.GetRequiredService<MergeOrchestrator>().Should().NotBeNull();

        var strategies = provider.GetServices<IConflictResolutionStrategy>().ToList();
        strategies.Should().HaveCount(4);
        strategies.Should().Contain(s => s is OverwriteTargetStrategy);
        strategies.Should().Contain(s => s is MoveOrRenameTargetStrategy);
        strategies.Should().Contain(s => s is RenameSourceStrategy);
        strategies.Should().Contain(s => s is SkipConflictStrategy);
    }

    #endregion

    #region End-to-End Filesystem Workflow Tests

    [Fact]
    public async Task ExecuteAsync_WhenPerformingNormalMerge_MovesFilesResolvesConflictCleansDuplicatesAndDirectories()
    {
        // Arrange
        CreateTestScenarioHierarchy();
        var fileSystem = new WindowsFileSystemService();
        var overwriteStrategy = new OverwriteTargetStrategy(fileSystem);
        var ui = CreateMockUi(overwriteStrategy);
        var orchestrator = CreateRealOrchestrator(fileSystem, ui, overwriteStrategy);

        var sourceRoot = new DirectoryRoot(_sourceDir);
        var targetRoot = new DirectoryRoot(_targetDir);

        // Act
        var summary = await orchestrator.ExecuteAsync(sourceRoot, targetRoot, isDryRun: false);

        // Assert - Summary Counters
        summary.TotalFilesScanned.Should().Be(4);
        summary.FilesMovedDirectly.Should().Be(2);
        summary.IdenticalFilesSkipped.Should().Be(1);
        summary.ConflictsResolved.Should().Be(1);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
        summary.IsDryRun.Should().BeFalse();

        // Assert - Source Directory State
        Directory.EnumerateFiles(_sourceDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.EnumerateDirectories(_sourceDir).Should().BeEmpty();

        // Assert - Target Directory State
        var targetUnique1 = Path.Combine(_targetDir, UniqueFile1Name);
        var targetUnique2 = Path.Combine(_targetDir, UniqueFile2Relative);
        var targetIdentical = Path.Combine(_targetDir, IdenticalFileName);
        var targetConflict = Path.Combine(_targetDir, ConflictFileName);
        var targetOnly = Path.Combine(_targetDir, TargetOnlyFileName);

        File.Exists(targetUnique1).Should().BeTrue();
        File.ReadAllText(targetUnique1).Should().Be(UniqueFile1Content);

        File.Exists(targetUnique2).Should().BeTrue();
        File.ReadAllText(targetUnique2).Should().Be(UniqueFile2Content);

        File.Exists(targetIdentical).Should().BeTrue();
        File.ReadAllText(targetIdentical).Should().Be(IdenticalFileContent);

        File.Exists(targetConflict).Should().BeTrue();
        File.ReadAllText(targetConflict).Should().Be(SourceConflictContent);

        File.Exists(targetOnly).Should().BeTrue();
        File.ReadAllText(targetOnly).Should().Be(TargetOnlyContent);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDryRunEnabled_LeavesDiskCompletelyUnmutated()
    {
        // Arrange
        CreateTestScenarioHierarchy();
        var fileSystem = new WindowsFileSystemService();
        var overwriteStrategy = new OverwriteTargetStrategy(fileSystem);
        var ui = CreateMockUi(overwriteStrategy);
        var orchestrator = CreateRealOrchestrator(fileSystem, ui, overwriteStrategy);

        var sourceRoot = new DirectoryRoot(_sourceDir);
        var targetRoot = new DirectoryRoot(_targetDir);

        // Act
        var summary = await orchestrator.ExecuteAsync(sourceRoot, targetRoot, isDryRun: true);

        // Assert - Summary Counters
        summary.TotalFilesScanned.Should().Be(4);
        summary.FilesMovedDirectly.Should().Be(2);
        summary.IdenticalFilesSkipped.Should().Be(1);
        summary.ConflictsResolved.Should().Be(1);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);
        summary.IsDryRun.Should().BeTrue();

        // Assert - Source files must remain untouched
        File.Exists(Path.Combine(_sourceDir, UniqueFile1Name)).Should().BeTrue();
        File.Exists(Path.Combine(_sourceDir, UniqueFile2Relative)).Should().BeTrue();
        File.Exists(Path.Combine(_sourceDir, IdenticalFileName)).Should().BeTrue();
        File.Exists(Path.Combine(_sourceDir, ConflictFileName)).Should().BeTrue();
        Directory.Exists(Path.Combine(_sourceDir, EmptySubdirectoryName)).Should().BeTrue();

        // Assert - Target files must remain unmutated
        File.Exists(Path.Combine(_targetDir, UniqueFile1Name)).Should().BeFalse();
        File.Exists(Path.Combine(_targetDir, UniqueFile2Relative)).Should().BeFalse();
        File.ReadAllText(Path.Combine(_targetDir, ConflictFileName)).Should().Be(TargetConflictContent);
        File.ReadAllText(Path.Combine(_targetDir, TargetOnlyFileName)).Should().Be(TargetOnlyContent);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConflictIsSkipped_LeavesConflictFileInSource()
    {
        // Arrange
        CreateTestScenarioHierarchy();
        var fileSystem = new WindowsFileSystemService();
        var skipStrategy = new SkipConflictStrategy();
        var ui = CreateMockUi(skipStrategy);
        var orchestrator = CreateRealOrchestrator(fileSystem, ui, skipStrategy);

        var sourceRoot = new DirectoryRoot(_sourceDir);
        var targetRoot = new DirectoryRoot(_targetDir);

        // Act
        var summary = await orchestrator.ExecuteAsync(sourceRoot, targetRoot, isDryRun: false);

        // Assert - Summary Counters
        summary.ConflictsResolved.Should().Be(0);
        summary.ConflictsSkipped.Should().Be(1);
        summary.ErrorsOccurred.Should().Be(0);

        // Assert - Source still has the skipped file
        File.Exists(Path.Combine(_sourceDir, ConflictFileName)).Should().BeTrue();
        File.ReadAllText(Path.Combine(_sourceDir, ConflictFileName)).Should().Be(SourceConflictContent);

        // Assert - Target still has the original conflict file
        File.ReadAllText(Path.Combine(_targetDir, ConflictFileName)).Should().Be(TargetConflictContent);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceConflictRenamed_MovesSourceFileWithNewName()
    {
        // Arrange
        CreateTestScenarioHierarchy();
        var fileSystem = new WindowsFileSystemService();
        var ui = CreateMockUi();
        var renameStrategy = new RenameSourceStrategy(fileSystem, ui);

        ui.PromptForConflictResolution(Arg.Any<FileConflict>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
          .Returns(renameStrategy);
        ui.PromptForNewFileName(Arg.Any<string>(), Arg.Any<string>())
          .Returns("conflict_renamed.txt");

        var orchestrator = CreateRealOrchestrator(fileSystem, ui, renameStrategy);
        var sourceRoot = new DirectoryRoot(_sourceDir);
        var targetRoot = new DirectoryRoot(_targetDir);

        // Act
        var summary = await orchestrator.ExecuteAsync(sourceRoot, targetRoot, isDryRun: false);

        // Assert - Summary Counters
        summary.ConflictsResolved.Should().Be(1);
        summary.ConflictsSkipped.Should().Be(0);
        summary.ErrorsOccurred.Should().Be(0);

        // Assert - Target has both original and renamed file
        File.ReadAllText(Path.Combine(_targetDir, ConflictFileName)).Should().Be(TargetConflictContent);
        File.ReadAllText(Path.Combine(_targetDir, "conflict_renamed.txt")).Should().Be(SourceConflictContent);
        File.Exists(Path.Combine(_sourceDir, ConflictFileName)).Should().BeFalse();
    }

    #endregion

    #region Program Workflow Tests

    [Fact]
    public async Task RunAsync_WhenUserCancelsMerge_ReturnsSuccessCodeAndShowsCancellationMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).ShowHeader();
        ui.Received(1).ShowInfo("Vorgang durch Benutzer abgebrochen.");
    }

    [Fact]
    public async Task RunAsync_WhenUpdateIsAvailable_DisplaysUpdateNotice()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((true, "v2.0.0", "https://github.com/releases/v2.0.0"));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).ShowUpdateNotice("v2.0.0", "https://github.com/releases/v2.0.0");
    }

    [Fact]
    public async Task RunAsync_WhenMergeExecutesSuccessfully_ReturnsSuccessCodeAndShowsSummary()
    {
        // Arrange
        CreateTestScenarioHierarchy();
        var services = new ServiceCollection();
        var ui = CreateMockUi();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = new WindowsFileSystemService();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var overwriteStrategy = new OverwriteTargetStrategy(fileSystem);
        ui.PromptForConflictResolution(Arg.Any<FileConflict>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
          .Returns(overwriteStrategy);

        services.AddSingleton<IFileSystemService>(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddTransient<IConflictResolutionStrategy>(_ => overwriteStrategy);
        services.AddTransient<IConflictResolutionStrategy, MoveOrRenameTargetStrategy>();
        services.AddTransient<IConflictResolutionStrategy, RenameSourceStrategy>();
        services.AddTransient<IConflictResolutionStrategy, SkipConflictStrategy>();
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).DisplayMergeSummary(Arg.Is<MergeSummary>(s => s.ErrorsOccurred == 0));
    }

    [Fact]
    public async Task RunAsync_WhenMergeEncounteredErrors_ReturnsErrorExitCode()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        fileSystem.DirectoryExists(source.FullPath).Returns(true);
        fileSystem.DirectoryExists(target.FullPath).Returns(true);
        fileSystem.GetAllRelativeFilePaths(source).Returns([new RelativeFilePath("failing_file.txt")]);
        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        fileSystem.When(x => x.MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
                  .Do(_ => throw new IOException("I/O failure during file move"));

        ui.When(x => x.RunWithProgress(Arg.Any<int>(), Arg.Any<Action<Action<string, int>>>()))
          .Do(callInfo =>
          {
              var work = callInfo.Arg<Action<Action<string, int>>>();
              work((_, _) => { });
          });

        ui.When(x => x.RunWithDownloadProgressAsync(Arg.Any<string>(), Arg.Any<Func<Action<double>, Task>>()))
          .Do(async callInfo =>
          {
              var work = callInfo.Arg<Func<Action<double>, Task>>();
              await work(_ => { });
          });

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(1);
        ui.Received(1).DisplayMergeSummary(Arg.Is<MergeSummary>(s => s.ErrorsOccurred == 1));
    }

    [Fact]
    public async Task RunAsync_WhenUnhandledExceptionThrown_CatchesErrorDisplaysMessageAndReturnsErrorExitCode()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        ui.When(x => x.ShowHeader()).Do(_ => throw new InvalidOperationException("Fatal terminal error"));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(null, provider);

        // Assert
        exitCode.Should().Be(1);
        ui.Received(1).ShowError(Arg.Is<string>(msg => msg.Contains("Fatal terminal error")));
    }

    [Fact]
    public async Task RunAsync_WhenValidCliArgumentsProvided_BypassesPromptConfigurationAndUsesCliValues()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var expectedSource = new DirectoryRoot(_sourceDir);
        var expectedTarget = new DirectoryRoot(_targetDir);

        ui.ConfirmMerge(expectedSource, expectedTarget, isDryRun: true).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var args = new[] { _sourceDir, _targetDir, "--dry-run" };
        var exitCode = await Program.RunAsync(args, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.DidNotReceive().PromptConfiguration();
        ui.Received(1).ConfirmMerge(expectedSource, expectedTarget, isDryRun: true);
        ui.Received(1).ShowInfo("Vorgang durch Benutzer abgebrochen.");
    }

    [Fact]
    public async Task RunAsync_WhenShortDryRunFlagUsed_SetsIsDryRunToTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var expectedSource = new DirectoryRoot(_sourceDir);
        var expectedTarget = new DirectoryRoot(_targetDir);

        ui.ConfirmMerge(expectedSource, expectedTarget, isDryRun: true).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var args = new[] { "-d", _sourceDir, _targetDir };
        var exitCode = await Program.RunAsync(args, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.DidNotReceive().PromptConfiguration();
        ui.Received(1).ConfirmMerge(expectedSource, expectedTarget, isDryRun: true);
    }

    [Fact]
    public async Task RunAsync_WhenFewerThanTwoNonFlagArgumentsProvided_FallsBackToPromptConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var args = new[] { "--dry-run", _sourceDir };
        var exitCode = await Program.RunAsync(args, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptConfiguration();
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Fact]
    public async Task RunAsync_WhenCliArgumentsContainInvalidPath_FallsBackToPromptConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var fileSystem = Substitute.For<IFileSystemService>();

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);

        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var args = new[] { "   ", _targetDir };
        var exitCode = await Program.RunAsync(args, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptConfiguration();
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Fact]
    public async Task RunAsync_OnStartup_ExecutesCleanupOldExecutableIfExists()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(true);
        updateDownloader.IsValidDownloadUrl(Arg.Any<string>()).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        updateDownloader.Received(1).CleanupOldExecutableIfExists();
    }

    [Fact]
    public async Task RunAsync_WhenNotRunningFromInstalledLocationAndInteractive_PromptsAndInstallsToUserPrograms()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        const string installedPath = @"C:\Users\test\AppData\Local\Programs\SmartFileMerger";
        const string installedExe = @"C:\Users\test\AppData\Local\Programs\SmartFileMerger\SmartFileMerger.exe";
        installer.IsRunningFromInstalledLocation().Returns(false);
        installer.GetInstalledDirectoryPath().Returns(installedPath);
        installer.GetInstalledExecutablePath().Returns(installedExe);
        ui.PromptForInstallation().Returns(true);

        var processLaunched = false;
        string? launchedPath = null;
        var processLauncher = Substitute.For<IProcessLauncher>();
        processLauncher.Start(Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(ci =>
            {
                processLaunched = true;
                launchedPath = ci.Arg<string>();
                return true;
            });

        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(processLauncher);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(args: null, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptForInstallation();
        installer.Received(1).InstallToUserProgramsAndCreateShortcuts();
        ui.Received(1).ShowInfo(Arg.Is<string>(msg => msg.Contains(installedPath)));
        processLaunched.Should().BeTrue();
        launchedPath.Should().Be(installedExe);
        ui.DidNotReceive().PromptConfiguration();
        ui.DidNotReceive().ConfirmMerge(Arg.Any<DirectoryRoot>(), Arg.Any<DirectoryRoot>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RunAsync_WhenInstallationThrows_ShowsWarningAndContinuesWorkflow()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(false);
        ui.PromptForInstallation().Returns(true);
        installer.When(x => x.InstallToUserProgramsAndCreateShortcuts())
                 .Do(_ => throw new UnauthorizedAccessException("Simulated access denied during shortcut creation"));

        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(args: null, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptForInstallation();
        installer.Received(1).InstallToUserProgramsAndCreateShortcuts();
        ui.Received(1).ShowWarning(Arg.Is<string>(msg => msg.Contains("Simulated access denied")));
        ui.Received(1).PromptConfiguration();
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Fact]
    public async Task RunAsync_WhenNotRunningFromInstalledLocationAndInstallationDeclined_DoesNotInstall()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(false);
        ui.PromptForInstallation().Returns(false);

        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(args: null, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptForInstallation();
        installer.DidNotReceive().InstallToUserProgramsAndCreateShortcuts();
    }

    [Fact]
    public async Task RunAsync_WhenNotRunningFromInstalledLocationButHasCliArguments_BypassesInstallationPrompt()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(false);

        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((false, string.Empty, string.Empty));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var args = new[] { _sourceDir, _targetDir };
        var exitCode = await Program.RunAsync(args, provider);

        // Assert
        exitCode.Should().Be(0);
        ui.DidNotReceive().PromptForInstallation();
        installer.DidNotReceive().InstallToUserProgramsAndCreateShortcuts();
    }

    [Fact]
    public async Task RunAsync_WhenUpdateAvailableAndConfirmed_DownloadsUpdateShowsInfoAndExitsSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(true);
        updateDownloader.IsValidDownloadUrl(Arg.Any<string>()).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((true, "v2.1.0", "https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0"));

        ui.PromptForUpdateInstallation("v2.1.0").Returns(true);
        ui.When(x => x.RunWithDownloadProgressAsync(Arg.Any<string>(), Arg.Any<Func<Action<double>, Task>>()))
          .Do(async callInfo =>
          {
              var work = callInfo.Arg<Func<Action<double>, Task>>();
              await work(_ => { });
          });

        updateDownloader.DownloadAndApplyUpdateAsync(
            Arg.Any<string>(),
            Arg.Any<Action<double>>(),
            Arg.Any<CancellationToken>())
          .Returns(true);

        var processLaunched = false;
        string? launchedPath = null;
        var processLauncher = Substitute.For<IProcessLauncher>();
        processLauncher.Start(Arg.Any<string>(), Arg.Any<IEnumerable<string>?>())
            .Returns(ci =>
            {
                processLaunched = true;
                launchedPath = ci.Arg<string>();
                return true;
            });

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(processLauncher);
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).PromptForUpdateInstallation("v2.1.0");
        await updateDownloader.Received(1).DownloadAndApplyUpdateAsync(
            Arg.Is<string>(url => url.Contains("v2.1.0") && url.EndsWith("SmartFileMerger.exe")),
            Arg.Any<Action<double>>(),
            Arg.Any<CancellationToken>());
        ui.Received(1).ShowInfo("Update erfolgreich installiert! Die Anwendung startet neu.");
        processLaunched.Should().BeTrue();
        launchedPath.Should().NotBeNullOrWhiteSpace();
        ui.DidNotReceive().ConfirmMerge(Arg.Any<DirectoryRoot>(), Arg.Any<DirectoryRoot>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RunAsync_WhenUpdateAvailableAndConfirmedButDownloadFails_ShowsWarningAndContinues()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(true);
        updateDownloader.IsValidDownloadUrl(Arg.Any<string>()).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((true, "v2.1.0", "https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0"));

        ui.PromptForUpdateInstallation("v2.1.0").Returns(true);
        ui.When(x => x.RunWithDownloadProgressAsync(Arg.Any<string>(), Arg.Any<Func<Action<double>, Task>>()))
          .Do(async callInfo =>
          {
              var work = callInfo.Arg<Func<Action<double>, Task>>();
              await work(_ => { });
          });

        updateDownloader.DownloadAndApplyUpdateAsync(
            Arg.Any<string>(),
            Arg.Any<Action<double>>(),
            Arg.Any<CancellationToken>())
          .Returns(false);

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.Received(1).ShowWarning("Update konnte nicht angewendet werden. Fahre mit aktueller Version fort.");
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Fact]
    public async Task RunAsync_WhenUpdateAvailableButDeclined_ContinuesWithoutDownloading()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(true);
        updateDownloader.IsValidDownloadUrl(Arg.Any<string>()).Returns(true);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((true, "v2.1.0", "https://github.com/mrambow/smart-file-merger/releases/tag/v2.1.0"));

        ui.PromptForUpdateInstallation("v2.1.0").Returns(false);

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        await updateDownloader.DidNotReceive().DownloadAndApplyUpdateAsync(
            Arg.Any<string>(),
            Arg.Any<Action<double>>(),
            Arg.Any<CancellationToken>());
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Fact]
    public async Task RunAsync_WhenUpdateUrlIsInvalid_ShowsWarningAndContinuesWithoutPrompting()
    {
        // Arrange
        var services = new ServiceCollection();
        var ui = Substitute.For<IUserInterface>();
        var updateCheck = Substitute.For<IUpdateCheckService>();
        var updateDownloader = Substitute.For<IUpdateDownloadService>();
        var fileSystem = Substitute.For<IFileSystemService>();
        var installer = Substitute.For<IAppInstallationService>();

        installer.IsRunningFromInstalledLocation().Returns(true);
        updateDownloader.IsValidDownloadUrl(Arg.Any<string>()).Returns(false);
        updateCheck.CheckForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((true, "v2.1.0", "http://untrusted-http.com/malicious"));

        var source = new DirectoryRoot(_sourceDir);
        var target = new DirectoryRoot(_targetDir);
        ui.PromptConfiguration().Returns((source, target, false));
        ui.ConfirmMerge(source, target, false).Returns(false);

        services.AddSingleton(fileSystem);
        services.AddSingleton(ui);
        services.AddSingleton(updateCheck);
        services.AddSingleton(updateDownloader);
        services.AddSingleton(installer);
        services.AddSingleton(Substitute.For<IProcessLauncher>());
        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();
        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();

        using var provider = services.BuildServiceProvider();

        // Act
        var exitCode = await Program.RunAsync(provider, ui);

        // Assert
        exitCode.Should().Be(0);
        ui.DidNotReceive().PromptForUpdateInstallation(Arg.Any<string>());
        ui.Received(1).ShowWarning(Arg.Is<string>(msg => msg.Contains("nicht vertrauenswürdigen")));
        ui.Received(1).ConfirmMerge(source, target, false);
    }

    [Theory]
    [InlineData("v1.2.0", "https://github.com/mrambow/smart-file-merger/releases/tag/v1.2.0", "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe")]
    [InlineData("1.2.0", "https://github.com/mrambow/smart-file-merger/releases/tag/v1.2.0", "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe")]
    [InlineData("v1.2.0", "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe", "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe")]
    [InlineData("v1.2.0", null, "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe")]
    [InlineData("1.2.0", null, "https://github.com/mrambow/smart-file-merger/releases/download/v1.2.0/SmartFileMerger.exe")]
    public void ResolveDownloadUrl_ResolvesCorrectGitHubReleaseDownloadUrl(string version, string? releaseUrl, string expectedUrl)
    {
        var result = Program.ResolveDownloadUrl(version, releaseUrl);

        result.Should().Be(expectedUrl);
    }

    #endregion

    #region Private Helpers

    private void CreateTestScenarioHierarchy()
    {
        // 1. Source unique file 1
        File.WriteAllText(Path.Combine(_sourceDir, UniqueFile1Name), UniqueFile1Content);

        // 2. Source unique file 2 in subfolder
        var subDir = Path.Combine(_sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_sourceDir, UniqueFile2Relative), UniqueFile2Content);

        // 3. Source empty folder
        Directory.CreateDirectory(Path.Combine(_sourceDir, EmptySubdirectoryName));

        // 4. Identical duplicate in source and target (same content, size & timestamp)
        var sourceIdentical = Path.Combine(_sourceDir, IdenticalFileName);
        var targetIdentical = Path.Combine(_targetDir, IdenticalFileName);
        File.WriteAllText(sourceIdentical, IdenticalFileContent);
        File.WriteAllText(targetIdentical, IdenticalFileContent);
        File.SetLastWriteTimeUtc(sourceIdentical, _identicalTimestamp);
        File.SetLastWriteTimeUtc(targetIdentical, _identicalTimestamp);

        // 5. Conflict file in source (larger & newer) and target (smaller & older)
        var sourceConflict = Path.Combine(_sourceDir, ConflictFileName);
        var targetConflict = Path.Combine(_targetDir, ConflictFileName);
        File.WriteAllText(sourceConflict, SourceConflictContent);
        File.WriteAllText(targetConflict, TargetConflictContent);
        File.SetLastWriteTimeUtc(sourceConflict, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(targetConflict, DateTime.UtcNow.AddDays(-5));

        // 6. Target only file (must remain untouched)
        File.WriteAllText(Path.Combine(_targetDir, TargetOnlyFileName), TargetOnlyContent);
    }

    private static IUserInterface CreateMockUi(IConflictResolutionStrategy? chosenStrategy = null)
    {
        var ui = Substitute.For<IUserInterface>();

        ui.When(x => x.RunWithProgress(Arg.Any<int>(), Arg.Any<Action<Action<string, int>>>()))
          .Do(callInfo =>
          {
              var work = callInfo.Arg<Action<Action<string, int>>>();
              work((_, _) => { });
          });

        ui.When(x => x.RunWithDownloadProgressAsync(Arg.Any<string>(), Arg.Any<Func<Action<double>, Task>>()))
          .Do(async callInfo =>
          {
              var work = callInfo.Arg<Func<Action<double>, Task>>();
              await work(_ => { });
          });

        if (chosenStrategy is not null)
        {
            ui.PromptForConflictResolution(
                Arg.Any<FileConflict>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<IConflictResolutionStrategy>>())
              .Returns(chosenStrategy);
        }

        return ui;
    }

    private static MergeOrchestrator CreateRealOrchestrator(
        IFileSystemService fileSystem,
        IUserInterface ui,
        IConflictResolutionStrategy primaryStrategy)
    {
        var strategies = new List<IConflictResolutionStrategy>
        {
            primaryStrategy,
            new MoveOrRenameTargetStrategy(fileSystem, ui),
            new RenameSourceStrategy(fileSystem, ui),
            new SkipConflictStrategy()
        };

        var runner = new ConflictResolutionRunner(ui);
        return new MergeOrchestrator(fileSystem, ui, runner, strategies);
    }

    #endregion
}
