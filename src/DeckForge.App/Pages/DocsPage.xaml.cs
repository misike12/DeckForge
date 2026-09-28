using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.Services;
using DeckForge.Core.Services;
using Microsoft.Web.WebView2.Core;

namespace DeckForge.App.Pages;

public partial class DocsPage : Page, INavigateWithin
{
    private const string DocsHome = "https://docs.macro-deck.app/";

    private readonly DocsSnapshotService _snapshots;
    private readonly DocsSearchService _search;
    private bool _offline;

    /// <summary>
    /// The docs path a deep link asked for, consumed by the first navigation it causes.
    /// </summary>
    private string? _requestedPath;

    /// <summary>Whether <c>Loaded</c> has run, so a deep link can tell if it is too early to navigate.</summary>
    private bool _loaded;

    /// <summary>Whether the embedded browser has been created and given a page to show.</summary>
    private bool _browserReady;

    public DocsPage(DocsSnapshotService snapshots, DocsSearchService search)
    {
        _snapshots = snapshots;
        _search = search;
        InitializeComponent();
        UpdateStatusLine();

        Loaded += async (_, _) =>
        {
            _loaded = true;
            AddressBox.Text = DocsHome;
            OfflineToggle.IsChecked = _snapshots.HasSnapshot && !_snapshots.LoadManifest().IsNullOrEmptyEquivalent();
            await InitializeBrowserAsync();
        };
    }

    private void UpdateStatusLine()
    {
        var version = _snapshots.CurrentVersion;
        if (version is null)
        {
            // Anything left in the store that is not a real snapshot - a leftover directory from a
            // failed or interrupted download, a stray folder - is no longer reported as one, so this
            // is the message the user actually needs: nothing is stored yet.
            StatusLine.Text =
                "No offline snapshot yet - use 'Download snapshot' to store the full docs inside "
                + "the app (once, a few MB).";
            return;
        }

        // The meta.json written by every download was never read, so the page count and the list of
        // pages that could not be fetched were recorded and then thrown away.
        var meta = _snapshots.CurrentMeta();
        StatusLine.Text = meta is null
            ? $"Offline snapshot {version} available ({_search.PageCount} pages indexed). "
              + "Offline mode reads it; online mode opens docs.macro-deck.app."
            : $"Offline snapshot {meta.Summary} Offline mode reads it; online mode opens "
              + "docs.macro-deck.app."
              + (meta.Failures.Count > 0 ? $" Not stored: {meta.Failures.Count} page(s)." : "");
    }

    /// <summary>
    /// Creates the embedded browser and shows a page, once per page instance.
    /// </summary>
    /// <remarks>
    /// The flag is claimed before the first <c>await</c>, not after. Two callers reach this method
    /// routinely - <c>Loaded</c> calls it, and setting the offline toggle from <c>Loaded</c> raises
    /// <c>Checked</c>, which calls it again - and both were getting past a flag that was only set once
    /// the browser existed. The first consumed the requested deep-link page and the second then found
    /// nothing left and put the docs index back, so every "Docs -&gt; ..." button opened the index.
    /// A later <c>Loaded</c> is a no-op for the same reason.
    /// </remarks>
    private async System.Threading.Tasks.Task InitializeBrowserAsync()
    {
        if (_browserReady)
        {
            return;
        }

        _browserReady = true;
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            if (OfflineToggle.IsChecked == true)
            {
                ApplyVirtualHostMapping();

                // A deep link asked for a specific page; the snapshot root is only the default.
                var target = _requestedPath ?? "/";
                _requestedPath = null;
                NavigateOffline(target);
            }
        }
        catch (Exception)
        {
            _browserReady = false;
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
    public void NavigateToDocs(string docsPath) => NavigateWithin(docsPath);

    /// <inheritdoc />
    public void NavigateWithin(string path)
    {
        UpdateStatusLine();

        // Kept until it is actually used. Every place that consumes it clears it, so a path that is
        // never consumed - because the page is not on screen yet - survives until the load that
        // shows it. Clearing it eagerly here is what made every deep link open the index: the request
        // arrived before Loaded had run, so the path was dropped on the floor and the load that
        // followed had nothing but the root to show.
        _requestedPath = path;

        if (!_loaded)
        {
            // The Frame raises Loaded later, not inside Navigate, so at this point the page has no
            // browser and the offline toggle is still at its default. Navigating now would start a
            // second, competing navigation; leaving it to Loaded makes the order irrelevant.
            return;
        }

        if (_offline || OfflineToggle.IsChecked == true)
        {
            ApplyVirtualHostMapping();
            NavigateOffline(path);
            _requestedPath = null;
            return;
        }

        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : DocsHome + path.TrimStart('/') + "/";
        AddressBox.Text = url;
        _ = EnsureAndNavigate(url);
        _requestedPath = null;
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

    private async void DownloadSnapshot_Click(object sender, RoutedEventArgs e) =>
        await DownloadAsync(force: false, sender as Wpf.Ui.Controls.Button);

    /// <summary>
    /// Re-downloads every file, overwriting what is there.
    /// </summary>
    /// <remarks>
    /// This button did not exist: the only download call passed <c>force: false</c>, so the
    /// <c>force</c> parameter was unreachable and the UI's promise to "refresh today's snapshot"
    /// could not be kept. The docs site changing under a cached copy is the whole reason to
    /// refresh, and skipping existing files is exactly wrong for that.
    /// </remarks>
    private async void RefreshSnapshot_Click(object sender, RoutedEventArgs e) =>
        await DownloadAsync(force: true, sender as Wpf.Ui.Controls.Button);

    private async Task DownloadAsync(bool force, Wpf.Ui.Controls.Button? button)
    {
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        StatusLine.Text = force
            ? "Re-downloading the docs snapshot, replacing today's copy..."
            : "Downloading the docs snapshot (one-time, a few MB)...";

        try
        {
            var result = await _snapshots.DownloadAsync(force);
            StatusLine.Text = $"Snapshot {result.Version} stored: {result.PageCount} pages, {result.FilesDownloaded} files."
                + (result.Failures.Count > 0 ? $" {result.Failures.Count} could not be fetched." : "");

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
            // A failed refresh must not take the existing snapshot with it. The versioned
            // directory is untouched until the new one is complete, so offline reading still works.
            StatusLine.Text = $"Snapshot download failed: {ex.Message} - the docs stay available online.";
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }

            UpdateStatusLine();
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
