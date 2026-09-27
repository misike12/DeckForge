using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Pages;
using ShellMessenger = DeckForge.App.Services.ShellMessenger;

namespace DeckForge.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>Shortcut order mirrors the sidebar: Ctrl+1..9, 0 = terminal.</summary>
    private static readonly string[] ShortcutTags =
    [
        "home", "new-project", "capabilities", "explorer", "manifest",
        "blocks", "icons", "buildrun", "ship", "terminal",
    ];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShellMessenger.Attach(this);
        TryApplyLiquidChrome();
        NavigateTo("home");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers is not (ModifierKeys.Control or ModifierKeys.None))
        {
            return;
        }

        // F-keys and Ctrl combos.
        switch (e.Key)
        {
            case Key.F1:
                NavigateTo("docs");
                e.Handled = true;
                return;
            case Key.F5:
                NavigateTo("buildrun");
                e.Handled = true;
                return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                NavigateTo("new-project");
                e.Handled = true;
                break;
            case Key.B:
                NavigateTo("buildrun");
                e.Handled = true;
                break;
            case Key.T:
                NavigateTo("terminal");
                e.Handled = true;
                break;
            case Key.E:
                NavigateTo("explorer");
                e.Handled = true;
                break;
            case Key.OemComma:
                NavigateTo("settings");
                e.Handled = true;
                break;
            case Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5
                or Key.D6 or Key.D7 or Key.D8 or Key.D9 or Key.D0:
            {
                var digit = e.Key is Key.D0 ? 9 : (int)(e.Key - Key.D1);
                if (digit < ShortcutTags.Length)
                {
                    NavigateTo(ShortcutTags[digit]);
                    e.Handled = true;
                }
                break;
            }
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            NavigateTo(tag);
        }
    }

    /// <summary>Navigates the page host and keeps the sidebar selection in sync.
    /// Supports deep links: "docs::features/actions" opens the embedded docs browser.</summary>
    public void NavigateTo(string tag)
    {
        string? docsPath = null;
        if (tag.StartsWith("docs::", StringComparison.Ordinal))
        {
            docsPath = tag[6..];
            tag = "docs";
        }

        if (PageRegistry.Create(tag) is not { } page)
        {
            return;
        }

        // Workspace-dependent pages refresh when they come into view.
        if (page is ManifestPage manifest)
        {
            manifest.RefreshOnNavigate();
        }
        else if (page is TerminalPage terminal)
        {
            terminal.RefreshOnNavigate();
        }
        else if (page is ExplorerPage explorer)
        {
            explorer.RefreshOnNavigate();
        }
        else if (page is PublishPage publish)
        {
            publish.RefreshOnNavigate();
        }
        else if (page is BlockActionPage blocks)
        {
            blocks.RefreshOnNavigate();
        }
        else if (page is IconStudioPage icons)
        {
            icons.RefreshOnNavigate();
        }
        else if (page is ActionsEditorPage actions)
        {
            actions.RefreshOnNavigate();
        }
        else if (page is EventsEditorPage events)
        {
            events.RefreshOnNavigate();
        }
        else if (page is ConfigFlowEditorPage configFlow)
        {
            configFlow.RefreshOnNavigate();
        }
        else if (page is LocalizationPage localization)
        {
            localization.RefreshOnNavigate();
        }
        else if (page is WidgetDesignerPage widget)
        {
            widget.RefreshOnNavigate();
        }
        else if (page is IconPackPage iconPack)
        {
            iconPack.RefreshOnNavigate();
        }
        else if (page is DocsPage docs && docsPath is not null)
        {
            docs.NavigateToDocs(docsPath);
        }

        PageHost.Navigate(page);

        foreach (var item in RootNavigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>()
                     .Concat(RootNavigation.FooterMenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>()))
        {
            item.IsActive = Equals(item.Tag, tag);
        }
    }

    /// <summary>
    /// Applies the Mica backdrop + rounded corners where the OS supports them (Windows 11).
    /// On older systems this silently keeps the solid Fluent look - the liquid feel comes
    /// from the acrylic card brushes either way.
    /// </summary>
    private void TryApplyLiquidChrome()
    {
        try
        {
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round;
        }
        catch (Exception)
        {
            // Unsupported OS/DWM: keep the default chrome.
        }
    }
}

/// <summary>
/// Page registry: maps navigation tags to pages resolved from the DI container.
/// New pages register one line here and one in App.xaml.cs - nothing else changes.
/// </summary>
public static class PageRegistry
{
    public static Page? Create(string tag) => tag switch
    {
        "home" => App.Services.GetService(typeof(HomePage)) as Page,
        "new-project" => App.Services.GetService(typeof(NewProjectPage)) as Page,
        "manifest" => App.Services.GetService(typeof(ManifestPage)) as Page,
        "buildrun" => App.Services.GetService(typeof(BuildRunPage)) as Page,
        "terminal" => App.Services.GetService(typeof(TerminalPage)) as Page,
        "capabilities" => App.Services.GetService(typeof(CapabilitiesPage)) as Page,
        "explorer" => App.Services.GetService(typeof(ExplorerPage)) as Page,
        "blocks" => App.Services.GetService(typeof(BlockActionPage)) as Page,
        "icons" => App.Services.GetService(typeof(IconStudioPage)) as Page,
        "ship" => App.Services.GetService(typeof(ShipPage)) as Page,
        "publish" => App.Services.GetService(typeof(PublishPage)) as Page,
        "actions" => App.Services.GetService(typeof(ActionsEditorPage)) as Page,
        "events" => App.Services.GetService(typeof(EventsEditorPage)) as Page,
        "configflow" => App.Services.GetService(typeof(ConfigFlowEditorPage)) as Page,
        "localization" => App.Services.GetService(typeof(LocalizationPage)) as Page,
        "widget" => App.Services.GetService(typeof(WidgetDesignerPage)) as Page,
        "iconpack" => App.Services.GetService(typeof(IconPackPage)) as Page,
        "docs" => App.Services.GetService(typeof(DocsPage)) as Page,
        "settings" => App.Services.GetService(typeof(SettingsPage)) as Page,
        _ => null,
    };
}
