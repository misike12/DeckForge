using System.IO;
using System.Net.Http;

namespace DeckForge.App.Services;

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
public sealed class DocsSnapshotService
{
    private const string DocsOrigin = "https://docs.macro-deck.app";
    private const string VirtualHost = "docs.localhost.deckforge";
    private const string ManifestFileName = "snapshot.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Raised after a snapshot download or refresh completes.</summary>
    public event Action? SnapshotChanged;

    public string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeckForge", "docs");

    /// <summary>Directory of the currently newest snapshot, or null when none exists.</summary>
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
                .FirstOrDefault();
        }
    }

    /// <summary>Version string of the newest snapshot ("2026.09.27"), or null.</summary>
    public string? CurrentVersion => Path.GetFileName(CurrentDirectory);

    public bool HasSnapshot => CurrentDirectory is not null;

    public string VirtualHostName => VirtualHost;

    /// <summary>Loads the snapshot manifest (url path -> relative file path), or null.</summary>
    public Dictionary<string, string>? LoadManifest()
    {
        var dir = CurrentDirectory;
        if (dir is null)
        {
            return null;
        }
        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
        }
        catch (System.Text.Json.JsonException)
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

        // 1. Sitemap -> url list.
        var sitemapUrl = ResolveSitemapUrl(await _http.GetStringAsync($"{DocsOrigin}/sitemap-index.xml", cancellationToken));
        var sitemap = await _http.GetStringAsync(sitemapUrl, cancellationToken);
        var urls = System.Text.RegularExpressions.Regex.Matches(sitemap, @"<loc>([^<]+)</loc>")
            .Select(m => m.Groups[1].Value.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            throw new InvalidOperationException("Sitemap contained no URLs - aborting so an empty snapshot is not installed.");
        }

        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var downloaded = 0;
        var failed = new List<string>();

        // 2. Every page (url path -> file path mirrors the site layout).
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathPart = url[DocsOrigin.Length..].Trim('/') is { Length: > 0 } p ? p : "index";
            var fileName = pathPart + ".html";
            var target = Path.Combine(dir, fileName);

            if (force || !File.Exists(target))
            {
                try
                {
                    var html = await _http.GetStringAsync(url + "/", cancellationToken);
                    await File.WriteAllTextAsync(target, html, cancellationToken);
                    downloaded++;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    failed.Add(url);
                    continue;
                }
            }
            manifest["/" + pathPart] = fileName;
        }

        // 3. Static assets the pages reference (best effort: css/js/images under /_astro, /favicon*, /fonts).
        var assetUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.html"))
        {
            var html = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"(/[^\":]+)\""))
            {
                var asset = match.Groups[1].Value.Split('#')[0];
                if (asset.EndsWith(".css") || asset.EndsWith(".js") || asset.EndsWith(".png")
                    || asset.EndsWith(".svg") || asset.EndsWith(".woff2") || asset.EndsWith(".woff")
                    || asset.EndsWith(".ico") || asset.EndsWith(".jpg") || asset.EndsWith(".webp"))
                {
                    assetUrls.Add(asset);
                }
            }
        }
        foreach (var asset in assetUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeName = asset.TrimStart('/').Replace("..", "__");
            var target = Path.Combine(dir, safeName.Replace('/', Path.DirectorySeparatorChar));
            if (!force && File.Exists(target))
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
            catch (Exception)
            {
                // Non-critical: the page still renders text-only.
            }
        }

        File.WriteAllText(Path.Combine(dir, ManifestFileName),
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(dir, "meta.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                downloadedAt = DateTime.UtcNow,
                pageCount = manifest.Count,
                failures = failed,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        // Keep only the two newest snapshots.
        foreach (var old in Directory.GetDirectories(Root)
                     .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                     .Skip(2)
                     .ToList())
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        SnapshotChanged?.Invoke();
        return new DocsSnapshotResult(version, manifest.Count, downloaded, failed);
    }

    public sealed record DocsSnapshotResult(string Version, int PageCount, int FilesDownloaded, List<string> Failures);

    /// <summary>Extracts title + readable plain text for the search index.</summary>
    public IReadOnlyList<DocsPageInfo> BuildSearchIndex()
    {
        var dir = CurrentDirectory;
        var index = new List<DocsPageInfo>();
        if (dir is null)
        {
            return index;
        }
        foreach (var file in Directory.GetFiles(dir, "*.html"))
        {
            try
            {
                var html = File.ReadAllText(file);
                var title = System.Text.RegularExpressions.Regex.Match(html, "<title>([^<]*)</title>").Groups[1].Value;
                index.Add(new DocsPageInfo(UrlPathOf(file, dir), title, StripHtml(html)));
            }
            catch (IOException)
            {
            }
        }
        return index;
    }

    private static string UrlPathOf(string file, string dir)
    {
        var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
        return "/" + (relative == "index.html" ? "" : relative[..^".html".Length]);
    }

    internal static string StripHtml(string html)
    {
        html = System.Text.RegularExpressions.Regex.Replace(html, "<script[\\s\\S]*?</script>", " ");
        html = System.Text.RegularExpressions.Regex.Replace(html, "<style[\\s\\S]*?</style>", " ");
        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return System.Text.RegularExpressions.Regex.Replace(html, "\\s+", " ").Trim();
    }

    private static string ResolveSitemapUrl(string sitemapIndex)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sitemapIndex, @"<loc>([^<]+sitemap[^<]*\.xml)</loc>");
        return match.Success ? match.Groups[1].Value : $"{DocsOrigin}/sitemap-0.xml";
    }
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
        return (start > 0 ? "… " : "") + text.Substring(start, length).Trim() + (start + length < text.Length ? " …" : "");
    }

    internal static List<string> Tokenize(string query) =>
        [.. query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct()];
}
