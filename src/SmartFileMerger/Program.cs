namespace SmartFileMerger;

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SmartFileMerger.Application;
using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
using SmartFileMerger.Domain.Models;
using SmartFileMerger.Infrastructure;
using SmartFileMerger.Presentation;

/// <summary>
/// Einstiegspunkt der Anwendung und Konfiguration des DI-Containers.
/// </summary>
public static class Program
{
    private const int SuccessExitCode = 0;
    private const int ErrorExitCode = 1;
    private const int UpdateTimeoutSeconds = 2;
    private const string UserCancelledMessage = "Vorgang durch Benutzer abgebrochen.";
    private const string DefaultVersionFallback = "1.0.0";
    private const string ExeFileName = "SmartFileMerger.exe";
    private const string GitHubOwner = "mrambow";
    private const string GitHubRepo = "smart-file-merger";
    private const string UpdateSuccessMessage = "Update erfolgreich installiert! Die Anwendung startet neu.";
    private const string UpdateFailedWarningMessage = "Update konnte nicht angewendet werden. Fahre mit aktueller Version fort.";

    public static async Task<int> Main(string[] args) =>
        await RunAsync(args, null);

    public static async Task<int> RunAsync(string[]? args = null, IServiceProvider? customServices = null)
    {
        var serviceProvider = customServices ?? ConfigureServices();
        var ui = serviceProvider.GetRequiredService<IUserInterface>();

        try
        {
            return await ExecuteWorkflowAsync(serviceProvider, ui, args);
        }
        catch (Exception ex)
        {
            ui.ShowError($"Ein unerwarteter Fehler ist aufgetreten: {ex.Message}");
            return ErrorExitCode;
        }
    }

    public static Task<int> RunAsync(IServiceProvider serviceProvider, IUserInterface ui, string[]? args = null) =>
        ExecuteWorkflowAsync(serviceProvider, ui, args);

    public static ServiceProvider ConfigureServices()
    {
        var services = CreateServiceCollection();
        return services.BuildServiceProvider();
    }

    public static IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileSystemService, WindowsFileSystemService>();
        services.AddSingleton<IUserInterface, SpectreConsoleUserInterface>();
        services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();
        services.AddSingleton<IAppInstallationService, WindowsAppInstallationService>();
        services.AddSingleton<IUpdateDownloadService, WindowsUpdateDownloadService>();
        services.AddSingleton<IProcessLauncher, WindowsProcessLauncher>();

        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();
        services.AddTransient<IConflictResolutionStrategy, MoveOrRenameTargetStrategy>();
        services.AddTransient<IConflictResolutionStrategy, RenameSourceStrategy>();
        services.AddTransient<IConflictResolutionStrategy, SkipConflictStrategy>();

        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();

        return services;
    }

    public static async Task<int> ExecuteWorkflowAsync(
        IServiceProvider serviceProvider,
        IUserInterface ui,
        string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(ui);

        CleanupOldExecutable(serviceProvider);
        ui.ShowHeader();

        if (TryPerformInitialSetup(serviceProvider, ui, args))
        {
            return SuccessExitCode;
        }

        var updateApplied = await TryApplyUpdateIfAvailableAsync(serviceProvider, ui, args);
        if (updateApplied)
        {
            return SuccessExitCode;
        }

        return await ExecuteMergeWorkflowAsync(serviceProvider, ui, args);
    }

    private static void CleanupOldExecutable(IServiceProvider serviceProvider)
    {
        var updateDownloader = serviceProvider.GetService<IUpdateDownloadService>();
        updateDownloader?.CleanupOldExecutableIfExists();
    }

    private static bool TryPerformInitialSetup(
        IServiceProvider serviceProvider,
        IUserInterface ui,
        string[]? args)
    {
        var installer = serviceProvider.GetService<IAppInstallationService>();
        if (installer is null || installer.IsRunningFromInstalledLocation() || HasDirectCliArguments(args))
        {
            return false;
        }

        if (!ui.PromptForInstallation())
        {
            return false;
        }

        try
        {
            installer.InstallToUserProgramsAndCreateShortcuts();
            ui.ShowInfo($"SmartFileMerger wurde erfolgreich installiert nach: {installer.GetInstalledDirectoryPath()}");
            var processLauncher = serviceProvider.GetRequiredService<IProcessLauncher>();
            var started = processLauncher.Start(installer.GetInstalledExecutablePath(), args);
            if (!started)
            {
                ui.ShowWarning("Die installierte Anwendung konnte nicht automatisch gestartet werden.");
            }
            return true;
        }
        catch (Exception ex)
        {
            ui.ShowWarning($"Installation konnte nicht abgeschlossen werden: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryApplyUpdateIfAvailableAsync(
        IServiceProvider serviceProvider,
        IUserInterface ui,
        string[]? args)
    {
        var updateChecker = serviceProvider.GetService<IUpdateCheckService>();
        if (updateChecker is null)
        {
            return false;
        }

        var installer = serviceProvider.GetService<IAppInstallationService>();
        if (installer is not null && !installer.IsRunningFromInstalledLocation())
        {
            return false;
        }

        (bool hasUpdate, string latestVersion, string releaseUrl) checkResult;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(UpdateTimeoutSeconds));
            var currentVersion = GetCurrentVersion();
            checkResult = await updateChecker.CheckForUpdateAsync(currentVersion, cts.Token);
        }
        catch
        {
            // Update-Prüfung ist Best-Effort und blockiert den Hauptablauf niemals
            return false;
        }

        if (!checkResult.hasUpdate)
        {
            return false;
        }

        ui.ShowUpdateNotice(checkResult.latestVersion, checkResult.releaseUrl);

        var updateDownloader = serviceProvider.GetService<IUpdateDownloadService>();
        if (updateDownloader is null)
        {
            return false;
        }

        var downloadUrl = ResolveDownloadUrl(checkResult.latestVersion, checkResult.releaseUrl);
        if (!updateDownloader.IsValidDownloadUrl(downloadUrl))
        {
            ui.ShowWarning($"Die Update-URL '{downloadUrl}' ist ungültig oder stammt von einem nicht vertrauenswürdigen Host.");
            return false;
        }

        var processLauncher = serviceProvider.GetService<IProcessLauncher>() ?? new WindowsProcessLauncher();

        return await HandleUpdateInstallationAsync(updateDownloader, processLauncher, ui, checkResult.latestVersion, downloadUrl, args);
    }

    private static async Task<bool> HandleUpdateInstallationAsync(
        IUpdateDownloadService updateDownloader,
        IProcessLauncher processLauncher,
        IUserInterface ui,
        string latestVersion,
        string downloadUrl,
        string[]? args)
    {
        if (!ui.PromptForUpdateInstallation(latestVersion))
        {
            return false;
        }

        var updateApplied = false;

        try
        {
            await ui.RunWithDownloadProgressAsync(latestVersion, async progress =>
            {
                updateApplied = await updateDownloader.DownloadAndApplyUpdateAsync(downloadUrl, progress);
            });
        }
        catch (Exception ex)
        {
            ui.ShowWarning($"{UpdateFailedWarningMessage}: {ex.Message}");
            return false;
        }

        if (!updateApplied)
        {
            ui.ShowWarning(UpdateFailedWarningMessage);
            return false;
        }

        ui.ShowInfo(UpdateSuccessMessage);
        RestartApplication(processLauncher, ui, args);
        return true;
    }

    public static string ResolveDownloadUrl(string latestVersion, string? releaseUrl)
    {
        var normalizedVersion = latestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? latestVersion
            : "v" + latestVersion;

        if (string.IsNullOrWhiteSpace(releaseUrl))
        {
            return $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{normalizedVersion}/{ExeFileName}";
        }

        if (releaseUrl.EndsWith(ExeFileName, StringComparison.OrdinalIgnoreCase))
        {
            return releaseUrl;
        }

        if (releaseUrl.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
        {
            var baseDownload = releaseUrl.Replace("/releases/tag/", "/releases/download/", StringComparison.OrdinalIgnoreCase);
            return $"{baseDownload.TrimEnd('/')}/{ExeFileName}";
        }

        return $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/download/{normalizedVersion}/{ExeFileName}";
    }

    private static void RestartApplication(IProcessLauncher processLauncher, IUserInterface ui, string[]? args)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        var started = processLauncher.Start(exePath, args);
        if (!started)
        {
            ui.ShowWarning("Die Anwendung konnte nicht automatisch neu gestartet werden. Bitte starten Sie sie manuell neu.");
        }
    }

    private static async Task<int> ExecuteMergeWorkflowAsync(
        IServiceProvider serviceProvider,
        IUserInterface ui,
        string[]? args)
    {
        var orchestrator = serviceProvider.GetRequiredService<MergeOrchestrator>();
        var (source, target, isDryRun) = ResolveConfiguration(args, ui);

        if (!ui.ConfirmMerge(source, target, isDryRun))
        {
            ui.ShowInfo(UserCancelledMessage);
            return SuccessExitCode;
        }

        var summary = await orchestrator.ExecuteAsync(source, target, isDryRun);
        ui.DisplayMergeSummary(summary);

        return summary.ErrorsOccurred > 0 ? ErrorExitCode : SuccessExitCode;
    }

    private static (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) ResolveConfiguration(
        string[]? args,
        IUserInterface ui)
    {
        if (TryParseCommandLineArguments(args, out var cliConfig))
        {
            return cliConfig;
        }

        return ui.PromptConfiguration();
    }

    private static bool HasDirectCliArguments(string[]? args) =>
        TryParseCommandLineArguments(args, out _);

    private static bool TryParseCommandLineArguments(
        string[]? args,
        out (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) config)
    {
        config = default;

        if (args is null || args.Length < 2)
        {
            return false;
        }

        var nonFlagArgs = args.Where(arg => !IsFlag(arg)).ToList();
        if (nonFlagArgs.Count < 2)
        {
            return false;
        }

        var isDryRun = args.Any(IsDryRunFlag);

        return TryCreateDirectoryRoots(nonFlagArgs[0], nonFlagArgs[1], isDryRun, out config);
    }

    private static bool IsFlag(string arg) =>
        arg.StartsWith("-", StringComparison.Ordinal);

    private static bool IsDryRunFlag(string arg) =>
        string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateDirectoryRoots(
        string sourcePath,
        string targetPath,
        bool isDryRun,
        out (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) config)
    {
        try
        {
            var source = new DirectoryRoot(sourcePath);
            var target = new DirectoryRoot(targetPath);
            config = (source, target, isDryRun);
            return true;
        }
        catch
        {
            config = default;
            return false;
        }
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0];
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }

        return DefaultVersionFallback;
    }
}
