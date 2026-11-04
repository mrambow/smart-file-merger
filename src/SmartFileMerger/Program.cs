namespace SmartFileMerger;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SmartFileMerger.Application;
using SmartFileMerger.Application.Strategies;
using SmartFileMerger.Domain.Interfaces;
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

    public static async Task<int> Main(string[] args) =>
        await RunAsync(args, null);

    public static async Task<int> RunAsync(string[]? args = null, IServiceProvider? customServices = null)
    {
        var serviceProvider = customServices ?? ConfigureServices();
        var ui = serviceProvider.GetRequiredService<IUserInterface>();

        try
        {
            return await ExecuteWorkflowAsync(serviceProvider, ui);
        }
        catch (Exception ex)
        {
            ui.ShowError($"Ein unerwarteter Fehler ist aufgetreten: {ex.Message}");
            return ErrorExitCode;
        }
    }

    public static Task<int> RunAsync(IServiceProvider serviceProvider, IUserInterface ui) =>
        ExecuteWorkflowAsync(serviceProvider, ui);

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

        services.AddTransient<IConflictResolutionStrategy, OverwriteTargetStrategy>();
        services.AddTransient<IConflictResolutionStrategy, MoveOrRenameTargetStrategy>();
        services.AddTransient<IConflictResolutionStrategy, RenameSourceStrategy>();
        services.AddTransient<IConflictResolutionStrategy, SkipConflictStrategy>();

        services.AddSingleton<ConflictResolutionRunner>();
        services.AddSingleton<MergeOrchestrator>();

        return services;
    }

    public static async Task<int> ExecuteWorkflowAsync(IServiceProvider serviceProvider, IUserInterface ui)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(ui);

        var updateCheckService = serviceProvider.GetRequiredService<IUpdateCheckService>();
        var orchestrator = serviceProvider.GetRequiredService<MergeOrchestrator>();

        ui.ShowHeader();
        await CheckForUpdatesAsync(updateCheckService, ui);

        var (source, target, isDryRun) = ui.PromptConfiguration();
        if (!ui.ConfirmMerge(source, target, isDryRun))
        {
            ui.ShowInfo(UserCancelledMessage);
            return SuccessExitCode;
        }

        var summary = await orchestrator.ExecuteAsync(source, target, isDryRun);
        ui.DisplayMergeSummary(summary);

        return summary.ErrorsOccurred > 0 ? ErrorExitCode : SuccessExitCode;
    }

    private static async Task CheckForUpdatesAsync(IUpdateCheckService updateCheckService, IUserInterface ui)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(UpdateTimeoutSeconds));
            var currentVersion = GetCurrentVersion();

            var (hasUpdate, latestVersion, releaseUrl) = await updateCheckService.CheckForUpdateAsync(currentVersion, cts.Token);
            if (!hasUpdate)
            {
                return;
            }

            ui.ShowUpdateNotice(latestVersion, releaseUrl);
        }
        catch
        {
            // Update-Prüfung ist Best-Effort und blockiert den Hauptablauf niemals
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
