namespace SmartFileMerger.Infrastructure;

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SmartFileMerger.Domain.Interfaces;

/// <summary>
/// Verwaltet die Installation der Anwendung im Benutzerprofil (%LocalAppData%\Programs\SmartFileMerger)
/// sowie das Erstellen und Aktualisieren von Desktop- und Startmenü-Verknüpfungen.
/// </summary>
public sealed class WindowsAppInstallationService : IAppInstallationService
{
    private const string ProgramsFolderName = "Programs";
    private const string AppFolderName = "SmartFileMerger";
    private const string AppExecutableFileName = "SmartFileMerger.exe";
    private const string ShortcutFileName = "SmartFileMerger.lnk";
    private const string ShortcutDescription = "SmartFileMerger - Intelligent Folder and File Merge Utility";
    private const string WScriptShellProgId = "WScript.Shell";

    private readonly IFileSystemService _fileSystemService;
    private readonly Func<string?> _processPathAccessor;
    private readonly string _localAppDataPath;
    private readonly string _desktopPath;
    private readonly string _startMenuProgramsPath;

    public WindowsAppInstallationService(
        IFileSystemService? fileSystemService = null,
        Func<string?>? processPathAccessor = null,
        string? localAppDataOverride = null,
        string? desktopOverride = null,
        string? startMenuOverride = null)
    {
        _fileSystemService = fileSystemService ?? new WindowsFileSystemService();
        _processPathAccessor = processPathAccessor ?? (() => Environment.ProcessPath);
        _localAppDataPath = localAppDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _desktopPath = desktopOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _startMenuProgramsPath = startMenuOverride ?? ResolveStartMenuProgramsPath();
    }

    private static string ResolveStartMenuProgramsPath()
    {
        var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(programsPath))
        {
            return programsPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            ProgramsFolderName);
    }

    public string GetInstalledDirectoryPath()
    {
        return Path.Combine(_localAppDataPath, ProgramsFolderName, AppFolderName);
    }

    public string GetInstalledExecutablePath()
    {
        return Path.Combine(GetInstalledDirectoryPath(), AppExecutableFileName);
    }

    public bool IsRunningFromInstalledLocation()
    {
        var currentProcessPath = _processPathAccessor();
        if (string.IsNullOrWhiteSpace(currentProcessPath))
        {
            return false;
        }

        try
        {
            var cleanProcessPath = currentProcessPath.Trim().Trim('\"', '\'');
            var normalizedCurrent = Path.GetFullPath(cleanProcessPath);
            var normalizedInstalled = Path.GetFullPath(GetInstalledExecutablePath());

            return string.Equals(normalizedCurrent, normalizedInstalled, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void InstallToUserProgramsAndCreateShortcuts()
    {
        var currentExecutablePath = ResolveCurrentExecutablePath();
        var installedExecutablePath = GetInstalledExecutablePath();

        CopyExecutableToInstallLocation(currentExecutablePath, installedExecutablePath);
        CreateShortcuts(installedExecutablePath);
    }

    private string ResolveCurrentExecutablePath()
    {
        var processPath = _processPathAccessor();
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Could not determine current executable path.");
        }

        var cleanPath = Path.GetFullPath(processPath.Trim().Trim('\"', '\''));
        if (!_fileSystemService.FileExists(cleanPath))
        {
            throw new FileNotFoundException($"Current executable was not found at '{cleanPath}'.", cleanPath);
        }

        return cleanPath;
    }

    private void CopyExecutableToInstallLocation(string sourceExecutablePath, string targetExecutablePath)
    {
        if (string.Equals(sourceExecutablePath, targetExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _fileSystemService.CopyFile(sourceExecutablePath, targetExecutablePath, overwrite: true);
    }

    private void CreateShortcuts(string targetExecutablePath)
    {
        var desktopShortcutPath = Path.Combine(_desktopPath, ShortcutFileName);
        var startMenuShortcutPath = Path.Combine(_startMenuProgramsPath, ShortcutFileName);

        CreateShortcutSafely(desktopShortcutPath, targetExecutablePath);
        CreateShortcutSafely(startMenuShortcutPath, targetExecutablePath);
    }

    private void CreateShortcutSafely(string shortcutFilePath, string targetExecutablePath)
    {
        try
        {
            _fileSystemService.EnsureDirectoryExistsForFile(shortcutFilePath);

            if (_fileSystemService.FileExists(shortcutFilePath))
            {
                _fileSystemService.StripReadOnlyAttribute(shortcutFilePath);
            }

            if (OperatingSystem.IsWindows())
            {
                CreateShellShortcut(shortcutFilePath, targetExecutablePath);
            }
        }
        catch
        {
            // Non-fatal fallback for non-Windows or restricted environments
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateShellShortcut(string shortcutFilePath, string targetExecutablePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shellType = Type.GetTypeFromProgID(WScriptShellProgId);
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(shortcutFilePath);
            if (shortcut is null)
            {
                return;
            }

            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = targetExecutablePath;
            dynamicShortcut.WorkingDirectory = Path.GetDirectoryName(targetExecutablePath) ?? string.Empty;
            dynamicShortcut.Description = ShortcutDescription;
            dynamicShortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? comObject)
    {
        if (OperatingSystem.IsWindows() && comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
