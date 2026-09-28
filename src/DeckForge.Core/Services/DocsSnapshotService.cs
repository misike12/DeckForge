using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeckForge.Core.Services;

/// <summary>
/// A downloaded page of the docs snapshot, with its plain text for the search index.
/// </summary>
public sealed record DocsPageInfo(string UrlPath, string Title, string PlainText);

/// <summary>
/// Offline docs engine: a versioned snapshot of docs.macro-deck.app stored under
/// %LOCALAPPDATA%/DeckForge/docs/&lt;version&gt;/, served to the embedded WebView2 through a
/// virtual host mapping so assets, styles and navigation work without a server.
/// The snapshot is downloaded once (or refreshed on demand) and never required at startup.
/// </summary>
public sealed partial class DocsSnapshotService
{
    private const string DocsOrigin = "https://docs.macro-deck.app";
    private const string VirtualHost = "docs.localhost.deckforge";
    private const string ManifestFileName = "snapshot.json";
    private const string MetaFileName = "meta.json";

    /// <summary>How many snapshots to keep. Two meant a good download was gone before anyone noticed.</summary>
    private const int SnapshotsToKeep = 3;

    /// <summary>Asset extensions the pages reference. Anything else is a page, not a file.</summary>
    private static readonly string[] AssetExtensions =
        [".css", ".js", ".mjs", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico",
         ".woff", ".woff2", ".ttf", ".otf", ".eot", ".webmanifest", ".xml", ".json", ".txt"];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Overrides where snapshots are stored. Set by tests so they do not read or write the real
    /// %LOCALAPPDATA% store - the version selection looks for the newest directory under the root,
    /// so a test that wrote there would change what every other test saw.
    /// </summary>
    public string? RootOverride { get; set; }

    /// <summary>Raised after a snapshot download or refresh completes.</summary>
    public event Action? SnapshotChanged;

    /// <summary>Raised when a download or refresh fails, with a reason worth showing.</summary>
    public event Action<string>? Failed;

    public string Root => RootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeckForge", "docs");

    /// <summary>
    /// Directory of the currently newest snapshot, or null when there is no usable one.
    /// </summary>
    /// <remarks>
    /// A directory only counts when it holds a non-empty manifest, and directories that do not are
    /// skipped rather than deleted.
    ///
    /// It used to return the newest directory by name, full stop. A snapshot is named for the day it
    /// was taken, so a leftover directory whose name sorts higher - a future date, or a stray folder -
    /// won permanently and was reported as the current snapshot. That is what happened here: three
    /// directories left behind by an earlier version of the test suite (which wrote to the real
    /// %LOCALAPPDATA% store before <see cref="RootOverride"/> existed) were the only ones present, the
    /// newest was <c>2099.01.04</c>, and the docs page reported "Offline snapshot 2099.01.04 available
    /// (0 pages indexed)" for a store that had never successfully downloaded anything.
    /// </remarks>
    public string? CurrentDirectory
    {
        get
        {
            if (!Directory.Exists(Root))
            {
                return null;
            }

            return Directory.GetDirectories(Root)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(IsUsableSnapshot);
        }
    }

    /// <summary>
    /// Whether a snapshot directory holds a readable manifest naming at least one page.
    /// </summary>
    private static bool IsUsableSnapshot(string directory) =>
        LoadManifestFrom(directory) is { Count: > 0 };

    /// <summary>Version string of the newest snapshot ("2026.09.27"), or null.</summary>
    public string? CurrentVersion => CurrentDirectory is null ? null : Path.GetFileName(CurrentDirectory);

    public bool HasSnapshot => CurrentDirectory is not null;

    public string VirtualHostName => VirtualHost;

    /// <summary>What a download recorded, read from meta.json.</summary>
    public sealed record SnapshotMeta(string Version, DateTimeOffset DownloadedAt, int PageCount, IReadOnlyList<string> Failures)
    {
        public string Summary =>
            $"{PageCount} page(s) downloaded {DownloadedAt.LocalDateTime:g}"
            + (Failures.Count == 0 ? "." : $", {Failures.Count} failed.");
    }

    /// <summary>
    /// The meta.json of the current snapshot, or null when there is none or it is unreadable.
    /// </summary>
    /// <remarks>
    /// This used to be written on every download and never read again, so the file count and the
    /// list of pages that failed were recorded and then thrown away. The About panel shows it.
    /// </remarks>
    public SnapshotMeta? CurrentMeta()
    {
        var dir = CurrentDirectory;
        if (dir is null)
        {
            return null;
        }

        var path = Path.Combine(dir, MetaFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new SnapshotMeta(
                CurrentVersion ?? "",
                root.TryGetProperty("downloadedAt", out var at) && at.TryGetDateTimeOffset(out var when)
                    ? when
                    : File.GetLastWriteTime(path),
                root.TryGetProperty("pageCount", out var count) ? ReadPageCount(count) : 0,
                root.TryGetProperty("failures", out var failures)
                    ? [.. failures.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0)]
                    : []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or FormatException)
        {
            // A truncated or hand-edited meta.json is not worth failing over; the snapshot itself
            // is what matters and it is still on disk.
            return null;
        }
    }

    /// <summary>Loads the snapshot manifest (url path -> relative file path), or null.</summary>
    /// <remarks>
    /// The deserialized dictionary is case-sensitive while the one written is
    /// <c>OrdinalIgnoreCase</c>, so a lookup for a differently-cased URL silently missed. Both sides
    /// are OrdinalIgnoreCase now, and the keys are normalised so a leading slash is optional.
    /// </remarks>
    public Dictionary<string, string>? LoadManifest()
    {
        var dir = CurrentDirectory;
        return dir is null ? null : LoadManifestFrom(dir);
    }

    /// <summary>
    /// A page count that is a number, or 0 when it is anything else.
    /// </summary>
    /// <remarks>
    /// <c>GetInt32()</c> throws <see cref="InvalidOperationException"/> on a JSON string and
    /// <see cref="FormatException"/> on an out-of-range number, and the catch above did not list
    /// either. This runs from the Docs page's constructor, so a hand-edited meta.json took the whole
    /// app down on the way to showing a page that was sitting there perfectly well.
    /// </remarks>
    private static int ReadPageCount(JsonElement count) =>
        count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var value) ? value : 0;

    /// <summary>Loads the manifest of one specific snapshot directory, or null.</summary>
    private static Dictionary<string, string>? LoadManifestFrom(string dir)
    {
        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
            if (raw is null)
            {
                return null;
            }

            return raw.ToDictionary(
                pair => "/" + pair.Key.TrimStart('/'),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the full docs site (sitemap-driven, raw HTML) into a new versioned folder.
    /// Skips already-downloaded files only when <paramref name="force"/> is false.
    /// </summary>
    public async Task<DocsSnapshotResult> DownloadAsync(bool force, CancellationToken cancellationToken = default)
    {
        var version = DateTime.UtcNow.ToString("yyyy.MM.dd");
        var dir = Path.Combine(Root, version);
        Directory.CreateDirectory(dir);

        // 1. Sitemap -> url list. Only same-origin paths are kept: a sitemap entry pointing
        // elsewhere is not a page of the docs, and following it would fetch and store whatever
        // the sitemap chose to name.
        var sitemapUrl = ResolveSitemapUrl(await GetStringAsync($"{DocsOrigin}/sitemap-index.xml", cancellationToken));
        var sitemap = await GetStringAsync(sitemapUrl, cancellationToken);
        var urls = LocPattern().Matches(sitemap)
            .Select(m => m.Groups[1].Value.TrimEnd('/'))
            .Where(IsDocsUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            throw new InvalidOperationException(
                "The sitemap contained no pages on docs.macro-deck.app - aborting so an empty "
                + "snapshot does not replace a working one.");
        }

        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var downloaded = 0;
        var failed = new List<string>();

        // 2. Every page. The file name comes from the URL, so it is sanitised before it is used -
        // the old code sliced the URL at the origin length without checking that it started with
        // the origin, and never stripped a traversal segment.
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathPart = RelativePathOf(url);
            if (pathPart is null)
            {
                failed.Add(url + " (not under the docs origin)");
                continue;
            }

            var fileName = ToSnapshotFileName(pathPart, ".html");
            var target = ResolveInside(dir, fileName);
            if (target is null)
            {
                failed.Add(url + " (unsafe path)");
                continue;
            }

            if (force || !File.Exists(target))
            {
                try
                {
                    var html = await GetStringAsync(url + "/", cancellationToken);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllTextAsync(target, html, cancellationToken);
                    downloaded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Reported rather than swallowed: a page that silently did not download is a
                    // page the user later searches for and cannot find.
                    failed.Add($"{url} ({ex.Message})");
                    continue;
                }
            }

            // The index is stored as index.html but is keyed by its URL path, which is empty. Keying it
            // "/index" instead meant the one page the app opens on had no manifest entry, so
            // navigating to it fell through to the "not in the snapshot" message.
            var manifestKey = pathPart == "index" ? "/" : "/" + pathPart;
            manifest[manifestKey] = fileName;
        }

        // 3. Static assets the pages reference. Single or double quoted, with or without a query
        //    string, and any of the extensions a docs site actually serves.
        var assetUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.html", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string html;
            try
            {
                html = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{Path.GetFileName(file)} (unreadable: {ex.Message})");
                continue;
            }

            foreach (Match match in AssetPattern().Matches(html))
            {
                var asset = match.Groups[1].Value.Split('#')[0].Split('?')[0];
                if (asset.StartsWith('/')
                    && AssetExtensions.Contains(Path.GetExtension(asset), StringComparer.OrdinalIgnoreCase))
                {
                    assetUrls.Add(asset);
                }
            }
        }

        foreach (var asset in assetUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeName = ToSnapshotFileName(asset.TrimStart('/'), string.Empty);
            var target = ResolveInside(dir, safeName);
            if (target is null || (!force && File.Exists(target)))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var bytes = await _http.GetByteArrayAsync(DocsOrigin + asset, cancellationToken);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                downloaded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-critical: the page still renders text-only. Counted so the summary is honest
                // about how complete the snapshot is.
                failed.Add($"{DocsOrigin}{asset} ({ex.Message})");
            }
        }

        File.WriteAllText(
            Path.Combine(dir, ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(dir, MetaFileName),
            JsonSerializer.Serialize(
                new
                {
                    version,
                    downloadedAt = DateTimeOffset.UtcNow,
                    pageCount = manifest.Count,
                    filesDownloaded = downloaded,
                    failures = failed,
                },
                new JsonSerializerOptions { WriteIndented = true }));

        PruneOldSnapshots();
        SnapshotChanged?.Invoke();
        return new DocsSnapshotResult(version, manifest.Count, downloaded, failed);
    }

    public sealed record DocsSnapshotResult(string Version, int PageCount, int FilesDownloaded, List<string> Failures);

    /// <summary>
    /// True when the URL is on the docs origin, which is the only thing downloaded.
    /// </summary>
    /// <remarks>
    /// The bare origin counts as being on the origin, because that is the site's index page. The
    /// sitemap entries were trimmed of their trailing slash, so the root page arrives here as exactly
    /// <c>https://docs.macro-deck.app</c> with no path - and requiring a trailing slash rejected it.
    /// The index is the one page the app opens on, so a snapshot without it was a snapshot that
    /// resolved to <c>ERR_FILE_NOT_FOUND</c> the moment offline mode was switched on, while reporting
    /// 124 pages downloaded and no failures.
    ///
    /// The prefix check is what keeps a look-alike host out: <c>docs.macro-deck.evil.com</c> does not
    /// start with the origin followed by a slash or end of string.
    /// </remarks>
    private static bool IsDocsUrl(string url) =>
        url.Equals(DocsOrigin, StringComparison.OrdinalIgnoreCase)
        || url.StartsWith(DocsOrigin + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The URL's path under the docs origin, or null when it is not one.</summary>
    /// <remarks>The origin itself maps to <c>index</c>, which becomes <c>index.html</c> on disk.</remarks>
    private static string? RelativePathOf(string url)
    {
        if (!IsDocsUrl(url))
        {
            return null;
        }

        var path = url.Length <= DocsOrigin.Length ? string.Empty : url[DocsOrigin.Length..].Trim('/');
        return path.Length == 0 ? "index" : path;
    }

    /// <summary>
    /// Turns a remote path into a relative file name that cannot leave the snapshot directory.
    /// </summary>
    /// <remarks>
    /// Every segment that is not a plain name is replaced rather than stripped, so the result is
    /// both safe and reversible enough to debug. This is the only place a remote string becomes a
    /// path, which is what makes it auditable.
    /// </remarks>
    public static string ToSnapshotFileName(string remotePath, string extension)
    {
        var segments = remotePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => UnsafeSegment().Replace(segment, "_"))
            .Where(segment => segment.Length > 0 && segment != "." && segment != "..");

        var joined = string.Join('/', segments);
        if (joined.Length == 0)
        {
            joined = "index";
        }

        if (extension.Length > 0 && !joined.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            joined += extension;
        }

        return joined;
    }

    /// <summary>Resolves a relative path inside <paramref name="root"/>, or null if it would escape.</summary>
    public static string? ResolveInside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));

        // Belt and braces. The segments are already sanitised above; this is the check that makes
        // that guarantee independent of how the segments were produced.
        return candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || string.Equals(candidate, fullRoot, StringComparison.Ordinal)
            ? candidate
            : null;
    }

    /// <summary>Deletes all but the newest few snapshots.</summary>
    private void PruneOldSnapshots()
    {
        foreach (var old in Directory.GetDirectories(Root)
                     .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                     .Skip(SnapshotsToKeep)
                     .ToList())
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A snapshot still held open by the WebView cannot be removed. That is a reason to
                // report it, not to fail the download that just succeeded.
                Failed?.Invoke($"Could not remove the old snapshot {Path.GetFileName(old)}: {ex.Message}");
            }
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Could not reach {url}: {ex.Message}", ex);
        }
    }

    /// <summary>Extracts title + readable plain text for the search index.</summary>
    public IReadOnlyList<DocsPageInfo> BuildSearchIndex()
    {
        var dir = CurrentDirectory;
        var index = new List<DocsPageInfo>();
        if (dir is null)
        {
            return index;
        }

        foreach (var file in Directory.GetFiles(dir, "*.html", SearchOption.AllDirectories))
        {
            try
            {
                var html = File.ReadAllText(file);
                var title = TitlePattern().Match(html).Groups[1].Value;
                index.Add(new DocsPageInfo(UrlPathOf(file, dir), title, StripHtml(html)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Failed?.Invoke($"Skipped {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return index;
    }

    private static string UrlPathOf(string file, string dir) =>
        UrlPathForTest(Path.GetRelativePath(dir, file));

    /// <summary>
    /// A snapshot-relative file name back to the URL path it was fetched from.
    /// </summary>
    /// <remarks>
    /// Public so the index-to-manifest key mapping can be pinned by a test. The index is stored as
    /// <c>index.html</c> but its URL path is empty, so the two sides of the manifest have to agree on
    /// that or the page the app opens on cannot be resolved.
    /// </remarks>
    public static string UrlPathForTest(string relativeFileName)
    {
        var relative = relativeFileName.Replace('\\', '/');
        return "/" + (relative == "index.html" ? "" : relative[..^".html".Length]);
    }

    /// <summary>
    /// The docs-relative path of a URL on the docs origin, or null when it is not on it.
    /// </summary>
    /// <remarks>
    /// Public so the origin check can be pinned by a test. It decides what gets downloaded, and
    /// getting it wrong is silent: a page that is not in the snapshot is a page the user later
    /// searches for and cannot find.
    /// </remarks>
    public static string? RelativePathForTest(string url) => RelativePathOf(url);

    public static string StripHtml(string html)
    {
        html = ScriptPattern().Replace(html, " ");
        html = StylePattern().Replace(html, " ");
        html = TagPattern().Replace(html, " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return WhitespacePattern().Replace(html, " ").Trim();
    }

    private static string ResolveSitemapUrl(string sitemapIndex)
    {
        var match = SitemapPattern().Match(sitemapIndex);
        if (!match.Success)
        {
            return $"{DocsOrigin}/sitemap-0.xml";
        }

        // Only a same-origin sitemap is followed, for the same reason pages are filtered.
        var candidate = match.Groups[1].Value;
        return candidate.StartsWith(DocsOrigin, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : $"{DocsOrigin}/sitemap-0.xml";
    }

    [GeneratedRegex(@"<loc>([^<]+)</loc>")]
    private static partial Regex LocPattern();

    [GeneratedRegex("""(?:src|href)\s*=\s*["']([^"':]+)["']""")]
    private static partial Regex AssetPattern();

    [GeneratedRegex(@"<title>([^<]*)</title>")]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"<loc>([^<]+sitemap[^<]*\.xml)</loc>")]
    private static partial Regex SitemapPattern();

    [GeneratedRegex(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"<style[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StylePattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>Characters that have no business in a file name.</summary>
    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeSegment();
}

/// <summary>
/// Full-text search over the offline snapshot: tokenized inverted index with snippet output.
/// </summary>
public sealed class DocsSearchService
{
    public sealed record SearchHit(DocsPageInfo Page, int Score, string Snippet);

    private readonly DocsSnapshotService _snapshots;
    private IReadOnlyList<DocsPageInfo>? _index;

    public DocsSearchService(DocsSnapshotService snapshots)
    {
        _snapshots = snapshots;
        _snapshots.SnapshotChanged += () => _index = null;
    }

    private IReadOnlyList<DocsPageInfo> Index => _index ??= _snapshots.BuildSearchIndex();

    public int PageCount => Index.Count;

    /// <summary>Searches all pages; terms are AND, matched against a lowercased token index.</summary>
    public IReadOnlyList<SearchHit> Search(string query, int maxHits = 20)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var page in Index)
        {
            var text = page.PlainText;
            var lower = text.ToLowerInvariant();
            var score = 0;
            var firstPosition = -1;
            foreach (var term in terms)
            {
                var count = 0;
                var position = 0;
                while ((position = lower.IndexOf(term, position, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    position += term.Length;
                }

                if (count == 0)
                {
                    score = 0;
                    break;
                }

                if (firstPosition < 0)
                {
                    firstPosition = lower.IndexOf(term, StringComparison.Ordinal);
                }

                // Title hits weigh far more; body hits saturate.
                if (page.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 50;
                }

                score += Math.Min(count, 8) * 2;
            }

            if (score > 0)
            {
                hits.Add(new SearchHit(page, score, Snippet(text, firstPosition, terms[0])));
            }
        }

        return [.. hits.OrderByDescending(h => h.Score).Take(maxHits)];
    }

    private static string Snippet(string text, int position, string term)
    {
        if (position < 0)
        {
            position = 0;
        }

        var start = Math.Max(0, position - 60);
        var length = Math.Min(180, text.Length - start);
        return (start > 0 ? "… " : "")
            + text.Substring(start, length).Trim()
            + (start + length < text.Length ? " …" : "");
    }

    /// <summary>
    /// Splits a query into lowercase terms of at least two characters.
    /// </summary>
    /// <remarks>
    /// The length floor is the point: a single letter matches nearly every page, so the ranking
    /// became noise and the useful results were buried.
    /// </remarks>
    public static List<string> Tokenize(string query) =>
        [.. query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct()];
}
