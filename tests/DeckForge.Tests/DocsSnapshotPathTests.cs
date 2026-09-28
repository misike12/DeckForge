using System.Text.Json;
using DeckForge.Core.Services;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the offline docs snapshot's path handling.
/// </summary>
/// <remarks>
/// The snapshot turns strings taken from a remote sitemap into file paths under
/// %LOCALAPPDATA%/DeckForge/docs. The page loop sliced the URL at the origin's length without first
/// checking that the URL started with the origin, and never stripped a traversal segment, while
/// the asset loop did sanitise - so the safety of the write depended on which loop a path came
/// through. These tests pin that every remote path goes through the same sanitiser and that nothing
/// it produces can leave the snapshot directory.
/// </remarks>
[TestFixture]
public sealed class DocsSnapshotPathTests
{
    [TestCase("features/actions", "features/actions.html")]
    [TestCase("index", "index.html")]
    [TestCase("features/actions/", "features/actions.html")]
    [TestCase("a/b/c", "a/b/c.html")]
    public void An_ordinary_page_path_is_kept(string remote, string expected) =>
        Assert.That(DocsSnapshotService.ToSnapshotFileName(remote, ".html"), Is.EqualTo(expected));

    [TestCase("../../evil", "evil.html")]
    [TestCase("../../../etc/passwd", "etc/passwd.html")]
    [TestCase("features/../../escape", "features/escape.html")]
    [TestCase("..", "index.html")]
    [TestCase(".", "index.html")]
    [TestCase("", "index.html")]
    public void A_traversal_segment_cannot_survive(string remote, string expected) =>
        Assert.That(DocsSnapshotService.ToSnapshotFileName(remote, ".html"), Is.EqualTo(expected));

    [TestCase("C:\\Windows\\System32\\evil", "C_/Windows/System32/evil.html")]
    [TestCase("file:with:colons", "file_with_colons.html")]
    [TestCase("star*name?.txt", "star_name_.txt.html")]
    public void Characters_with_no_business_in_a_file_name_are_replaced(string remote, string expected) =>
        Assert.That(DocsSnapshotService.ToSnapshotFileName(remote, ".html"), Is.EqualTo(expected));

    [TestCase("_astro/app.css")]
    [TestCase("_astro/app.js")]
    [TestCase("favicon.svg")]
    [TestCase("_astro/font.woff2")]
    public void An_extension_is_not_appended_twice(string remote)
    {
        var extension = Path.GetExtension(remote);
        var result = DocsSnapshotService.ToSnapshotFileName(remote, extension);

        Assert.That(result, Is.EqualTo(remote.Replace('\\', '/')));
    }

    [Test]
    public void No_remote_path_can_resolve_outside_the_snapshot_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "docsroot");
        var attempts = new[]
        {
            "../../../Windows/System32/evil.html",
            "..\\..\\evil.html",
            "/etc/passwd",
            "....//....//evil.html",
            "features/../../../../evil.html",
        };

        foreach (var attempt in attempts)
        {
            var resolved = DocsSnapshotService.ResolveInside(root, attempt);

            // Either the path is rejected outright, or it resolves inside - never outside.
            if (resolved is not null)
            {
                var full = Path.GetFullPath(resolved);
                Assert.That(
                    full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                    Is.True,
                    $"'{attempt}' resolved to {full}, outside {root}");
            }
        }
    }

    [Test]
    public void A_url_off_the_docs_origin_is_not_treated_as_a_page()
    {
        // The sitemap is remote data. An entry naming another host is not a page of the docs, and
        // slicing it at the origin's length produced garbage that then became a file name.
        var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(RelativePath("https://evil.example.com/steal"), Is.Null);
            Assert.That(RelativePath("https://docs.macro-deck.evil.com/steal"), Is.Null);
            Assert.That(RelativePath("file:///etc/passwd"), Is.Null);
            Assert.That(RelativePath("https://docs.macro-deck.app/features/actions"),
                Is.EqualTo("features/actions"));
        });

        _ = service;
    }

    [Test]
    public void The_manifest_is_read_case_insensitively_and_with_optional_leading_slash()
    {
        // It was written OrdinalIgnoreCase and read case-sensitively, so a differently-cased URL
        // silently missed and the page fell through to the network.
        var service = CreateService();
        var root = service.Root;
        var version = "2099.01.01";
        WriteSnapshot(root, version, "/Features/Actions", "features/variables");
        File.WriteAllText(
            Path.Combine(root, version, "snapshot.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["/Features/Actions"] = "Features/Actions.html",
                ["features/variables"] = "features/variables.html",
            }));

        var manifest = service.LoadManifest();

        Assert.That(manifest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(manifest!["/features/actions"], Is.EqualTo("Features/Actions.html"),
                "A case-differing key missed, so the page fell through to the network.");
            Assert.That(manifest["/features/variables"], Is.EqualTo("features/variables.html"),
                "A key without a leading slash was not found.");
        });
    }

    [Test]
    public void A_corrupt_manifest_does_not_throw()
    {
        var service = CreateService();
        var root = service.Root;
        var version = "2099.01.02";
        Directory.CreateDirectory(Path.Combine(root, version));
        File.WriteAllText(Path.Combine(root, version, "snapshot.json"), "{ not json");

        Assert.That(service.LoadManifest(), Is.Null);
    }

    [Test]
    public void The_meta_file_records_what_the_last_download_actually_did()
    {
        // meta.json was written on every download and read by nothing, so the page count and the
        // list of pages that failed were recorded and thrown away.
        var service = CreateService();
        var root = service.Root;
        var version = "2099.01.03";
        WriteSnapshot(root, version, "/index");
        File.WriteAllText(
            Path.Combine(root, version, "meta.json"),
            JsonSerializer.Serialize(new
            {
                version,
                downloadedAt = new DateTimeOffset(2099, 1, 3, 12, 0, 0, TimeSpan.Zero),
                pageCount = 42,
                filesDownloaded = 60,
                failures = new[] { "https://docs.macro-deck.app/broken" },
            }));

        var meta = service.CurrentMeta();

        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.PageCount, Is.EqualTo(42));
            Assert.That(meta.Failures, Has.Count.EqualTo(1));
            Assert.That(meta.Summary, Does.Contain("42 page"));
            Assert.That(meta.Summary, Does.Contain("1 failed"));
        });
    }

    [Test]
    public void A_corrupt_meta_file_does_not_throw()
    {
        var service = CreateService();
        var root = service.Root;
        var version = "2099.01.04";
        Directory.CreateDirectory(Path.Combine(root, version));
        File.WriteAllText(Path.Combine(root, version, "meta.json"), "{ truncated");

        Assert.That(service.CurrentMeta(), Is.Null);
    }

    [Test]
    public void A_directory_that_is_not_a_snapshot_is_not_reported_as_the_current_one()
    {
        // The real %LOCALAPPDATA% store had three directories in it, all left by an earlier version
        // of this suite (which wrote to the real store before RootOverride existed): 2099.01.02 with
        // a hand-written manifest, and 2099.01.03 and 2099.01.04 with only a meta.json - one of them
        // deliberately corrupt. Version selection took the newest directory by name, so 2099.01.04 won
        // and the docs page reported "Offline snapshot 2099.01.04 available (0 pages indexed)" for a
        // store that had never downloaded anything.
        var service = CreateService();
        var root = service.Root;

        // Sorts highest by name, but holds no manifest at all.
        WriteMetaOnly(root, "2099.01.04", "2099.01.04");
        // Sorts second, and its manifest is corrupt rather than merely empty.
        var corrupt = Path.Combine(root, "2099.01.03");
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "snapshot.json"), "{ not json");
        File.WriteAllText(Path.Combine(corrupt, "meta.json"), "{ truncated");
        // Sorts lowest, and is a real snapshot.
        var real = Path.Combine(root, "2026.09.28");
        Directory.CreateDirectory(real);
        File.WriteAllText(
            Path.Combine(real, "snapshot.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["/index"] = "index.html" }));

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentVersion, Is.EqualTo("2026.09.28"),
                "A higher-sorting directory with nothing usable in it was reported as the snapshot.");
            Assert.That(service.CurrentDirectory, Is.EqualTo(real));
        });

        static void WriteMetaOnly(string root, string version, string body)
        {
            var dir = Path.Combine(root, version);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "meta.json"), body);
        }
    }

    [Test]
    public void An_empty_manifest_does_not_count_as_a_snapshot()
    {
        // A download that was interrupted before the first page is written leaves the directory and
        // an empty manifest behind. Reporting that as an available snapshot would promise offline
        // docs that resolve to nothing.
        var service = CreateService();
        var dir = Path.Combine(service.Root, "2026.09.28");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "snapshot.json"), "{}");

        Assert.That(service.CurrentDirectory, Is.Null);
        Assert.That(service.CurrentVersion, Is.Null);
    }

    [Test]
    public void No_snapshot_at_all_leaves_the_current_version_unset()
    {
        var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentDirectory, Is.Null);
            Assert.That(service.CurrentVersion, Is.Null);
            Assert.That(service.HasSnapshot, Is.False);
        });
    }

    [Test]
    public void The_bare_origin_is_the_index_page_and_belongs_to_the_docs()
    {
        // The sitemap entries were trimmed of their trailing slash, so the root page reached the
        // origin check as exactly "https://docs.macro-deck.app" with no path. Requiring a trailing
        // slash rejected it, so the index was the one page never downloaded: the snapshot reported 124
        // pages and no failures, and switching to offline mode gave ERR_FILE_NOT_FOUND because
        // index.html was not in it.
        //
        // A look-alike host must still be rejected, which is what the prefix check is for.
        Assert.Multiple(() =>
        {
            Assert.That(DocsSnapshotService.RelativePathForTest("https://docs.macro-deck.app"),
                Is.EqualTo("index"));
            Assert.That(DocsSnapshotService.RelativePathForTest("https://docs.macro-deck.app/"),
                Is.EqualTo("index"));
            Assert.That(DocsSnapshotService.RelativePathForTest("https://docs.macro-deck.app/features/actions"),
                Is.EqualTo("features/actions"));

            Assert.That(DocsSnapshotService.RelativePathForTest("https://docs.macro-deck.evil.com"),
                Is.Null);
            Assert.That(DocsSnapshotService.RelativePathForTest("https://docs.macro-deck.evil.com/steal"),
                Is.Null);
            Assert.That(DocsSnapshotService.RelativePathForTest("https://evil.example.com/steal"),
                Is.Null);
        });
    }

    [Test]
    public void The_index_page_is_stored_as_index_html_and_keyed_by_its_url_path()
    {
        // The manifest is keyed by URL path, and the index's path is empty. Keying it "/index"
        // instead meant the page the app opens on had no entry to resolve against.
        Assert.That(DocsSnapshotService.ToSnapshotFileName("index", ".html"), Is.EqualTo("index.html"));
        Assert.That(DocsSnapshotService.UrlPathForTest("index.html"), Is.EqualTo("/"));
        Assert.That(DocsSnapshotService.UrlPathForTest("features/actions.html"), Is.EqualTo("/features/actions"));
    }

    [Test]
    public void Stripping_html_leaves_readable_text()
    {
        var html = """
            <html><head><title>Actions</title>
            <style>body { color: red; }</style>
            <script>var x = "<b>not text</b>";</script></head>
            <body><h1>Actions</h1><p>An action does &amp; something.</p></body></html>
            """;

        var text = DocsSnapshotService.StripHtml(html);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Actions"));
            Assert.That(text, Does.Contain("An action does & something."));
            Assert.That(text, Does.Not.Contain("color: red"));
            Assert.That(text, Does.Not.Contain("not text"));
        });
    }

    [Test]
    public void Search_needs_at_least_two_characters_per_term()
    {
        // A single letter matches nearly every page, so the ranking became noise.
        Assert.Multiple(() =>
        {
            Assert.That(DocsSearchService.Tokenize("a b action"), Is.EqualTo(new[] { "action" }));
            Assert.That(DocsSearchService.Tokenize("action action"), Is.EqualTo(new[] { "action" }));
            Assert.That(DocsSearchService.Tokenize("   "), Is.Empty);
        });
    }

    private static string? RelativePath(string url) => DocsSnapshotService.RelativePathForTest(url);

    /// <summary>A service with its own store, so these tests never touch %LOCALAPPDATA%.</summary>
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "deckforge-docs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private DocsSnapshotService CreateService() => new() { RootOverride = _root };

    /// <summary>Writes a directory that is a real snapshot, so version selection accepts it.</summary>
    private static void WriteSnapshot(string root, string version, params string[] pages)
    {
        var dir = Path.Combine(root, version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "snapshot.json"),
            JsonSerializer.Serialize(pages.ToDictionary(page => page, page => page.TrimStart('/') + ".html")));
    }
}
