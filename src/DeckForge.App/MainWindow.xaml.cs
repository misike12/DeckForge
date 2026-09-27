using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Pages;
using ShellMessenger = DeckForge.App.Services.ShellMessenger;

namespace DeckForge.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>
    /// Ctrl+digit targets, read from the sidebar in the order it is declared.
    /// </summary>
    /// <remarks>
    /// This used to be a hand-written list of ten tags next to a sidebar of nineteen, and it had
    /// already drifted: position 6 was "blocks" while the sidebar's sixth item is "actions", so
    /// Ctrl+6 opened the wrong page. Reading the tags from the nav items makes drift impossible
    /// and gives every page a shortcut rather than the ten that happened to be listed.
    /// </remarks>
    private string[] _shortcutTags = [];

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
        _shortcutTags =
        [
            .. RootNavigation.MenuItems
                .OfType<System.Collections.IEnumerable>()
                .SelectMany(items => items.Cast<Wpf.Ui.Controls.NavigationViewItem>())
                .Concat(RootNavigation.FooterMenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
                .Select(item => item.Tag as string)
                .Where(tag => !string.IsNullOrEmpty(tag))
                .Select(tag => tag!),
        ];

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
                // F5 runs the stub host, which is what the Terminal page's help text and the
                // Build & Run page both tell the user it does. It only navigated there, so the
                // claim was false in three places.
                NavigateTo("buildrun");
                ShellMessenger.RequestRun();
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
                if (digit < _shortcutTags.Length)
                {
                    NavigateTo(_shortcutTags[digit]);
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

        // Workspace-dependent pages refresh when they come into view. One test, not a chain: a page
        // that implements the interface refreshes, and a page that does not cannot be forgotten.
        if (page is IRefreshOnNavigate refreshable)
        {
            refreshable.RefreshOnNavigate();
        }

        if (docsPath is not null && page is INavigateWithin within)
        {
            within.NavigateWithin(docsPath);
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
/// </summary>
/// <remarks>
/// A new page needs three edits, not one: a line here, a registration in App.xaml.cs, and a
/// NavigationViewItem in MainWindow.xaml. The third is also what gives it a Ctrl+digit shortcut,
/// so the nav item is not optional garnish.
/// </remarks>
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
