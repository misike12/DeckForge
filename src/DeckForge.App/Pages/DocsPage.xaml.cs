using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Services;
using Microsoft.Web.WebView2.Core;

namespace DeckForge.App.Pages;

public partial class DocsPage : Page
{
    private const string DocsHome = "https://docs.macro-deck.app/";

    private readonly DocsSnapshotService _snapshots;
    private readonly DocsSearchService _search;
    private bool _offline;
    private string? _pendingOfflinePath;

    public DocsPage(DocsSnapshotService snapshots, DocsSearchService search)
    {
        _snapshots = snapshots;
        _search = search;
        InitializeComponent();
        UpdateStatusLine();

        Loaded += async (_, _) =>
        {
            AddressBox.Text = DocsHome;
            OfflineToggle.IsChecked = _snapshots.HasSnapshot && !_snapshots.LoadManifest().IsNullOrEmptyEquivalent();
            await InitializeBrowserAsync();
        };
    }

    private void UpdateStatusLine()
    {
        StatusLine.Text = _snapshots.CurrentVersion is { } version
            ? $"Offline snapshot {version} available ({_search.PageCount} pages indexed). Offline mode reads it; online mode opens docs.macro-deck.app."
            : "No offline snapshot yet - use 'Download snapshot' to store the full docs inside the app (once, ~a few MB).";
    }

    private async System.Threading.Tasks.Task InitializeBrowserAsync()
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            if (OfflineToggle.IsChecked == true)
            {
                ApplyVirtualHostMapping();
                if (_pendingOfflinePath is not null)
                {
                    NavigateOffline(_pendingOfflinePath);
                }
                else
                {
                    NavigateOffline("/");
                }
            }
        }
        catch (Exception)
        {
            AddressBox.Text = "WebView2 runtime missing - install 'Evergreen WebView2 Runtime'.";
        }
    }

    private void ApplyVirtualHostMapping()
    {
        if (_snapshots.CurrentDirectory is not { } dir)
        {
            return;
        }
        try
        {
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _snapshots.VirtualHostName,
                dir,
                CoreWebView2HostResourceAccessKind.Allow);
        }
        catch (Exception)
        {
            // Mapping already applied for this host.
        }
    }

    private void NavigateOffline(string docsPath)
    {
        var path = docsPath.Trim('/');
        var manifest = _snapshots.LoadManifest();
        string target;
        if (manifest is not null && manifest.TryGetValue("/" + path, out var file))
        {
            target = file;
        }
        else
        {
            var guess = path.Length == 0 ? "index.html" : path + ".html";
            if (File.Exists(Path.Combine(_snapshots.CurrentDirectory!, guess)))
            {
                target = guess;
            }
            else
            {
                StatusLine.Text = $"Page '{path}' is not in the snapshot - showing the index. Refresh the snapshot to include newer pages.";
                target = "index.html";
            }
        }

        AddressBox.Text = $"offline://{_snapshots.VirtualHostName}/{path}";
        Browser.CoreWebView2.Navigate($"https://{_snapshots.VirtualHostName}/{target}");
    }

    /// <summary>Navigates the embedded browser (used by deep links from every editor page).</summary>
    public void NavigateToDocs(string docsPath)
    {
        UpdateStatusLine();
        if (_offline || OfflineToggle.IsChecked == true)
        {
            _pendingOfflinePath = docsPath;
            if (Browser.CoreWebView2 is not null)
            {
                ApplyVirtualHostMapping();
                NavigateOffline(docsPath);
            }
            return;
        }

        var url = docsPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? docsPath
            : DocsHome + docsPath.TrimStart('/') + "/";
        AddressBox.Text = url;
        _ = EnsureAndNavigate(url);
    }

    private async System.Threading.Tasks.Task EnsureAndNavigate(string url)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            Browser.Source = new Uri(url);
        }
        catch (Exception)
        {
            AddressBox.Text = "WebView2 runtime missing.";
        }
    }

    private async void OfflineToggle_Changed(object sender, RoutedEventArgs e)
    {
        _offline = OfflineToggle.IsChecked == true;
        if (!_offline)
        {
            _ = EnsureAndNavigate(ResolveCurrentOnlineUrl());
            return;
        }
        await InitializeBrowserAsync();
    }

    private string ResolveCurrentOnlineUrl()
    {
        // offline://host/features/events -> https://docs.macro-deck.app/features/events/
        var text = AddressBox.Text;
        if (text.StartsWith("offline://", StringComparison.Ordinal))
        {
            var path = text[("offline://" + _snapshots.VirtualHostName).Length..];
            return DocsHome + path.TrimStart('/') + "/";
        }
        return text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : DocsHome;
    }

    private async void DownloadSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button)
        {
            button.IsEnabled = false;
        }
        StatusLine.Text = "Downloading the docs snapshot (one-time, a few MB)...";
        try
        {
            var result = await _snapshots.DownloadAsync(force: false);
            StatusLine.Text = $"Snapshot {result.Version} stored: {result.PageCount} pages, {result.FilesDownloaded} files."
                + (result.Failures.Count > 0 ? $" {result.Failures.Count} pages failed (shown online instead)." : "");
            if (OfflineToggle.IsChecked != true)
            {
                OfflineToggle.IsChecked = true;
            }
            else
            {
                await InitializeBrowserAsync();
            }
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"Snapshot download failed: {ex.Message} - the docs stay available online.";
        }
        finally
        {
            if (sender is Wpf.Ui.Controls.Button reenabled)
            {
                reenabled.IsEnabled = true;
            }
        }
    }

    private void SearchToggle_Click(object sender, RoutedEventArgs e) => ToggleSearch();

    private void ToggleSearch()
    {
        SearchPanel.Visibility = SearchPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (SearchPanel.Visibility == Visibility.Visible)
        {
            SearchBox.Focus();
            if (_search.PageCount == 0)
            {
                StatusLine.Text = "Search indexes the offline snapshot - download it first (button in the toolbar).";
            }
        }
    }

    private void SearchClose_Click(object sender, RoutedEventArgs e) => SearchPanel.Visibility = Visibility.Collapsed;

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedSearchHit();
        }
    }

    private void SearchResult_Open(object sender, MouseButtonEventArgs e) => OpenSelectedSearchHit();

    private void RunSearch()
    {
        var hits = _search.Search(SearchBox.Text);
        SearchResults.ItemsSource = hits;
        StatusLine.Text = hits.Count == 0
            ? $"No pages match '{SearchBox.Text}' in the {(_snapshots.CurrentVersion ?? "online")} docs."
            : $"{hits.Count} page(s) match '{SearchBox.Text}'.";
    }

    private void OpenSelectedSearchHit()
    {
        if (SearchResults.SelectedItem is DocsSearchService.SearchHit hit)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            NavigateToDocs(hit.Page.UrlPath.Trim('/'));
        }
    }

    private void Go_Click(object sender, RoutedEventArgs e) => _ = EnsureAndNavigate(AddressBox.Text);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = AddressBox.Text;
            if (text.StartsWith("offline://", StringComparison.Ordinal) || OfflineToggle.IsChecked == true)
            {
                var path = text.StartsWith("offline://", StringComparison.Ordinal)
                    ? text[("offline://" + _snapshots.VirtualHostName).Length..]
                    : "/" + text.Trim('/');
                ApplyVirtualHostMapping();
                NavigateOffline(path);
            }
            else
            {
                _ = EnsureAndNavigate(text);
            }
            e.Handled = true;
        }
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ResolveCurrentOnlineUrl()) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // The browser may be blocked; not fatal.
        }
    }
}

/// <summary>Tiny helper so the toggle default reads clearly.</summary>
internal static class DocsManifestExtensions
{
    public static bool IsNullOrEmptyEquivalent(this Dictionary<string, string>? manifest) =>
        manifest is null || manifest.Count == 0;
}
