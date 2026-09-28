using System.Windows;
using DeckForge.App.Services;
using DeckForge.App.Themes;
using DeckForge.CliAdapter;
using DeckForge.CliAdapter.Processes;
using DeckForge.CliAdapter.Tools;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;

namespace DeckForge.App;

public partial class App : Application
{
    private static readonly ILogger Log =
        LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger("DeckForge");
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack hooks: handles install/update/uninstall flows when running as a packaged
        // build; a plain `dotnet run` is a no-op. Must run before anything else touches the UI.
        TryRunVelopack();

        var services = new ServiceCollection();

        // Logging (debug window output for now).
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));

        // Core services.
        services.AddSingleton<SettingsService>();
        services.AddSingleton<WorkspaceManager>();

        // Process adapters.
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<DotNetCli>();
        services.AddSingleton<MacroDeckCli>();
        services.AddSingleton<EnvironmentDoctor>();

        // Generators. The generator resolves its contributors from this same provider,
        // so extensions can register IProjectContentContributor implementations and
        // immediately take part in every generation run.
        services.AddSingleton<PluginProjectGenerator>();
        services.AddSingleton<CodeGen.Capabilities.CapabilityPresetContributor>();
        services.AddSingleton<IProjectContentContributor>(
            sp => sp.GetRequiredService<CodeGen.Capabilities.CapabilityPresetContributor>());

        // ViewModels.
        services.AddSingleton<ViewModels.WorkspaceViewModel>();
        services.AddSingleton<ViewModels.NewProjectViewModel>();
        services.AddSingleton<ViewModels.BuildRunViewModel>();

        // Pages (singletons preserve in-page state like console output and forms).
        services.AddSingleton<Pages.HomePage>();
        services.AddSingleton<Pages.NewProjectPage>();
        services.AddSingleton<Pages.ManifestPage>();
        services.AddSingleton<ViewModels.ManifestStudioViewModel>();
        services.AddSingleton<Pages.BuildRunPage>();
        services.AddSingleton<Pages.TerminalPage>();
        services.AddSingleton<ViewModels.TerminalViewModel>();
        services.AddSingleton<Pages.DocsPage>();
        services.AddSingleton<Pages.SettingsPage>();
        services.AddSingleton<Pages.CapabilitiesPage>();
        services.AddSingleton<ViewModels.CapabilityGalleryViewModel>();
        services.AddSingleton<Pages.ExplorerPage>();
        services.AddSingleton<ViewModels.ExplorerViewModel>();
        services.AddSingleton<Pages.BlockActionPage>();
        services.AddSingleton<ViewModels.BlockActionViewModel>();
        services.AddSingleton<Pages.IconStudioPage>();
        services.AddSingleton<ViewModels.IconStudioViewModel>();
        services.AddSingleton<Pages.ShipPage>();
        services.AddSingleton<ViewModels.ShipViewModel>();
        services.AddSingleton<Pages.PublishPage>();
        services.AddSingleton<ViewModels.PublishViewModel>();
        services.AddSingleton<Pages.ActionsEditorPage>();
        services.AddSingleton<ViewModels.ActionsEditorViewModel>();
        services.AddSingleton<Pages.EventsEditorPage>();
        services.AddSingleton<ViewModels.EventsEditorViewModel>();
        services.AddSingleton<Pages.ConfigFlowEditorPage>();
        services.AddSingleton<ViewModels.ConfigFlowEditorViewModel>();
        services.AddSingleton<Pages.LocalizationPage>();
        services.AddSingleton<ViewModels.LocalizationManagerViewModel>();
        services.AddSingleton<Pages.WidgetDesignerPage>();
        services.AddSingleton<ViewModels.WidgetDesignerViewModel>();
        services.AddSingleton<Services.DocsSnapshotService>();
        services.AddSingleton<Services.DocsSearchService>();
        services.AddSingleton<Services.VelopackPackagingService>();
        services.AddSingleton<Pages.IconPackPage>();
        services.AddSingleton<ViewModels.IconPackDesignerViewModel>();
        services.AddSingleton<Services.ResxMergerService>();
        services.AddSingleton<Services.UpdateCheckService>();
        services.AddSingleton<ViewModels.SettingsViewModel>();

        Services = services.BuildServiceProvider();

        var settings = Services.GetRequiredService<SettingsService>();
        settings.Load();
        settings.LoadFailed += message =>
            Log.LogError("Settings could not be read, so the defaults are in use: {Message}", message);
        ApplyTheme(settings);

        settings.SettingsChanged += () => ApplyTheme(settings);

        // The Environment page gates on the CLI version the user has pinned, rather than on the
        // constant this build was compiled with.
        Services.GetRequiredService<EnvironmentDoctor>().ExpectedCliVersionOverride =
            settings.Settings.MacroDeckCliVersion;

        // WorkspaceManager.CurrentChanged had no subscribers at all, so the shell's workspace
        // broadcast was raised by hand from two places and missed from the rest. The shell now
        // listens to the one event that already exists, and every Open or Close is announced.
        var workspaces = Services.GetRequiredService<WorkspaceManager>();
        workspaces.CurrentChanged += context =>
            ShellMessenger.NotifyWorkspaceChanged(context?.SolutionPath);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // CheckForUpdatesOnStart had nothing to switch on: the check lived in the Settings page's
        // click handler. It runs after the window is up, so a slow network never delays startup.
        if (settings.Settings.CheckForUpdatesOnStart)
        {
            _ = CheckForUpdatesInBackgroundAsync();
        }
    }

    /// <summary>
    /// Runs the update check off the startup path and announces anything worth the user's attention.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget from the constructor, so a hung network call cannot stop the window opening.
    /// The task body catches everything, so nothing becomes an unobserved task exception either.
    /// </remarks>
    private static async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var result = await Services.GetRequiredService<UpdateCheckService>().CheckAsync();
            if (result.Available)
            {
                ShellMessenger.AnnounceUpdate(result);
            }
        }
        catch (Exception ex)
        {
            // UpdateCheckService already turns expected failures into results. Anything reaching
            // here is unexpected, and must not take the app down on the way past.
            Log.LogError(ex, "The start-of-session update check failed");
        }
    }

    /// <summary>Velopack entry point; never lets packaging hooks crash a dev run.</summary>
    private static void TryRunVelopack()
    {
        try
        {
            Velopack.VelopackApp.Build().Run();
        }
        catch (Exception)
        {
            // Not installed via Velopack (dotnet run / portable folder) - nothing to do.
        }
    }

    /// <summary>Re-applies the theme, for when the OS changes its own while the app is open.</summary>
    internal static void ReapplyTheme(SettingsService settings)
    {
        if (settings is not null)
        {
            ApplyTheme(settings);
        }
    }

    private static void ApplyTheme(SettingsService settings)
    {
        var theme = settings.Settings.Theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };
        ApplicationThemeManager.Apply(theme);

        var accent = Themes.LiquidTheme.Accents.FirstOrDefault(a => a.Name == settings.Settings.Accent)
                     ?? Themes.LiquidTheme.Accents[0];

        // WPF-UI control accent: Primary buttons, toggles, selection highlight all follow.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            accent.Color,
            theme,
            systemGlassColor: false,
            systemAccentColor: false);

        var effective = theme == ApplicationTheme.Light ? AppTheme.Light : AppTheme.Dark;
        LiquidTheme.Apply(Current.Resources, effective, settings.Settings.Accent);
    }

    private int _crashGuard;

    /// <summary>How large the crash log may grow before it is rotated.</summary>
    /// <remarks>
    /// It used to be appended to forever. A crash loop - which is exactly when this handler runs
    /// most - filled the disk in an afternoon, and a full disk makes the next start fail in a way
    /// that looks unrelated. It is rotated instead.
    /// </remarks>
    private const int MaxCrashLogBytes = 512 * 1024;

    /// <summary>
    /// Crash-safe handler: logs to disk, never shows nested message boxes (a UI fault here
    /// would otherwise recurse - MessageBox-in-crash-handler-crash), and gives up after a
    /// burst so the app exits instead of spinning.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _crashGuard++;
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeckForge", "crash.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);

            if (System.IO.File.Exists(logPath)
                && new System.IO.FileInfo(logPath).Length > MaxCrashLogBytes)
            {
                System.IO.File.Move(logPath, logPath + ".1", overwrite: true);
            }

            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch (Exception logFailure)
        {
            // Never let the logger itself throw - but do not make it invisible either.
            System.Diagnostics.Debug.WriteLine($"Could not write the crash log: {logFailure.Message}");
        }

        Log.LogError(e.Exception, "Unhandled exception on the UI thread (occurrence {Count})", _crashGuard);

        if (_crashGuard <= 3)
        {
            // Survive it, but say so. The first three were swallowed with nothing on screen: the
            // crash went to a log nobody had opened and the UI carried on in whatever state it was
            // left in, which is how a misbehaving page becomes an unexplained bug report.
            ShellMessenger.ReportUnhandled(e.Exception);
            e.Handled = true;
            return;
        }

        // Four in one session is a loop, and continuing would corrupt whatever the user was
        // editing. Exit so the next launch starts clean.
        e.Handled = false;
        Environment.Exit(70);
    }
}
