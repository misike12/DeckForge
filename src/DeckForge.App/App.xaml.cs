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
        services.AddSingleton<Services.CapabilityScaffolder>();

        // ViewModels.
        services.AddSingleton<ViewModels.WorkspaceViewModel>();
        services.AddSingleton<ViewModels.MainViewModel>();
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

        Services = services.BuildServiceProvider();

        var settings = Services.GetRequiredService<SettingsService>();
        settings.Load();
        ApplyTheme(settings);

        settings.SettingsChanged += () => ApplyTheme(settings);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
            // Never let the logger itself throw.
        }

        if (_crashGuard <= 3)
        {
            e.Handled = true;
            return;
        }

        e.Handled = false;
        Environment.Exit(70);
    }
}
