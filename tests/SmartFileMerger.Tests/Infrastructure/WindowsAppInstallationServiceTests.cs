namespace SmartFileMerger.Tests.Infrastructure;

using System.IO;
using FluentAssertions;
using SmartFileMerger.Infrastructure;
using Xunit;

public sealed class WindowsAppInstallationServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _localAppDataDir;
    private readonly string _desktopDir;
    private readonly string _startMenuDir;
    private readonly string _sourceDir;
    private readonly string _sourceExePath;

    public WindowsAppInstallationServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "SmartFileMergerInstallTests_" + Guid.NewGuid().ToString("N"));
        _localAppDataDir = Path.Combine(_testRoot, "LocalAppData");
        _desktopDir = Path.Combine(_testRoot, "Desktop");
        _startMenuDir = Path.Combine(_testRoot, "StartMenu", "Programs");
        _sourceDir = Path.Combine(_testRoot, "SourceDir");

        Directory.CreateDirectory(_localAppDataDir);
        Directory.CreateDirectory(_desktopDir);
        Directory.CreateDirectory(_startMenuDir);
        Directory.CreateDirectory(_sourceDir);

        _sourceExePath = Path.Combine(_sourceDir, "SmartFileMerger.exe");
        File.WriteAllText(_sourceExePath, "dummy binary content");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_testRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void GetInstalledDirectoryPath_ReturnsExpectedLocalAppDataProgramsPath()
    {
        var sut = new WindowsAppInstallationService(
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.GetInstalledDirectoryPath();

        var expected = Path.Combine(_localAppDataDir, "Programs", "SmartFileMerger");
        result.Should().Be(expected);
    }

    [Fact]
    public void GetInstalledExecutablePath_ReturnsExpectedExecutableFullPath()
    {
        var sut = new WindowsAppInstallationService(
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.GetInstalledExecutablePath();

        var expected = Path.Combine(_localAppDataDir, "Programs", "SmartFileMerger", "SmartFileMerger.exe");
        result.Should().Be(expected);
    }

    [Fact]
    public void IsRunningFromInstalledLocation_WhenProcessPathMatchesInstalledLocation_ReturnsTrue()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => Path.Combine(_localAppDataDir, "Programs", "SmartFileMerger", "SmartFileMerger.exe"),
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.IsRunningFromInstalledLocation();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsRunningFromInstalledLocation_WhenProcessPathHasDifferentCasingAndQuotes_ReturnsTrue()
    {
        var installedExe = Path.Combine(_localAppDataDir, "Programs", "SmartFileMerger", "SmartFileMerger.exe");
        var quotedPath = $"\"{installedExe.ToUpperInvariant()}\"";

        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => quotedPath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.IsRunningFromInstalledLocation();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsRunningFromInstalledLocation_WhenRunningFromAnotherLocation_ReturnsFalse()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => _sourceExePath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.IsRunningFromInstalledLocation();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsRunningFromInstalledLocation_WhenProcessPathIsNull_ReturnsFalse()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => null,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.IsRunningFromInstalledLocation();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsRunningFromInstalledLocation_WhenProcessPathIsWhitespace_ReturnsFalse()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => "   ",
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var result = sut.IsRunningFromInstalledLocation();

        result.Should().BeFalse();
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenCalled_CopiesExecutableAndCreatesShortcuts()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => _sourceExePath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        sut.InstallToUserProgramsAndCreateShortcuts();

        var installedExe = sut.GetInstalledExecutablePath();
        File.Exists(installedExe).Should().BeTrue();
        File.ReadAllText(installedExe).Should().Be("dummy binary content");

        var desktopShortcut = Path.Combine(_desktopDir, "SmartFileMerger.lnk");
        File.Exists(desktopShortcut).Should().BeTrue();

        var startMenuShortcut = Path.Combine(_startMenuDir, "SmartFileMerger.lnk");
        File.Exists(startMenuShortcut).Should().BeTrue();
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenTargetFileIsReadOnly_OverwritesSuccessfully()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => _sourceExePath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var targetExe = sut.GetInstalledExecutablePath();
        Directory.CreateDirectory(Path.GetDirectoryName(targetExe)!);
        File.WriteAllText(targetExe, "old stale content");
        File.SetAttributes(targetExe, FileAttributes.ReadOnly);

        sut.InstallToUserProgramsAndCreateShortcuts();

        File.Exists(targetExe).Should().BeTrue();
        File.ReadAllText(targetExe).Should().Be("dummy binary content");
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenShortcutDirectoriesDoNotExist_CreatesThemAndOverwritesExisting()
    {
        var customDesktop = Path.Combine(_testRoot, "NonExistentDesktop");
        var customStartMenu = Path.Combine(_testRoot, "NonExistentStartMenu");

        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => _sourceExePath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: customDesktop,
            startMenuOverride: customStartMenu);

        sut.InstallToUserProgramsAndCreateShortcuts();

        var desktopShortcut = Path.Combine(customDesktop, "SmartFileMerger.lnk");
        File.Exists(desktopShortcut).Should().BeTrue();

        var startMenuShortcut = Path.Combine(customStartMenu, "SmartFileMerger.lnk");
        File.Exists(startMenuShortcut).Should().BeTrue();

        // Overwrite again with stale read-only shortcuts
        File.SetAttributes(desktopShortcut, FileAttributes.ReadOnly);
        File.SetAttributes(startMenuShortcut, FileAttributes.ReadOnly);

        sut.InstallToUserProgramsAndCreateShortcuts();

        File.Exists(desktopShortcut).Should().BeTrue();
        File.Exists(startMenuShortcut).Should().BeTrue();
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenAlreadyRunningFromInstalledLocation_PreservesFileAndCreatesShortcuts()
    {
        var sut = new WindowsAppInstallationService(
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var installedExe = sut.GetInstalledExecutablePath();
        Directory.CreateDirectory(Path.GetDirectoryName(installedExe)!);
        File.WriteAllText(installedExe, "already installed content");

        var runningFromInstalledSut = new WindowsAppInstallationService(
            processPathAccessor: () => installedExe,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var act = () => runningFromInstalledSut.InstallToUserProgramsAndCreateShortcuts();

        act.Should().NotThrow();
        File.ReadAllText(installedExe).Should().Be("already installed content");
        File.Exists(Path.Combine(_desktopDir, "SmartFileMerger.lnk")).Should().BeTrue();
        File.Exists(Path.Combine(_startMenuDir, "SmartFileMerger.lnk")).Should().BeTrue();
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenProcessPathIsNull_ThrowsInvalidOperationException()
    {
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => null,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var act = () => sut.InstallToUserProgramsAndCreateShortcuts();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*executable path*");
    }

    [Fact]
    public void InstallToUserProgramsAndCreateShortcuts_WhenSourceExecutableNotFound_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(_testRoot, "DoesNotActuallyExist.exe");
        var sut = new WindowsAppInstallationService(
            processPathAccessor: () => nonExistentPath,
            localAppDataOverride: _localAppDataDir,
            desktopOverride: _desktopDir,
            startMenuOverride: _startMenuDir);

        var act = () => sut.InstallToUserProgramsAndCreateShortcuts();

        act.Should().Throw<FileNotFoundException>()
            .WithMessage($"*{nonExistentPath}*");
    }
}
