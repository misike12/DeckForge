using System.Text.Json;
using DeckForge.Core.Settings;
using NUnit.Framework;

namespace DeckForge.Tests.Settings;

/// <summary>
/// Reading and writing the settings file: what survives a round trip, what a broken file does, and what a
/// save that cannot finish leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// The store used to live in the WPF app, which the test project may not reference, so the file format and
/// the failure behaviour had no tests at all - and the file format is the one part of the settings system a
/// user can edit by hand, which is exactly the part that gets handed values nobody designed for.
/// </para>
/// <para>
/// The two tests that matter most here are the corrupt-file one and the atomic-write one, and they are the
/// two that were unwritable before. A settings file that cannot be parsed loses a user's accent, their
/// recent list and their CLI version at once; a settings file half written by a crash does the same thing
/// on the next start, which is why the write goes beside the file and then replaces it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SettingsServiceTests
{
    private TempDirectory _temp = null!;

    private string _root = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-settings");
        _root = _temp.Root;
        _path = Path.Combine(_root, "DeckForge", "settings.json");
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    [Test]
    public void A_workspace_with_no_file_starts_on_the_defaults_rather_than_reported_as_an_error()
    {
        var service = new SettingsService(_path);
        var failed = false;
        service.LoadFailed += _ => failed = true;

        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.False, "no file is not a failure");
            Assert.That(service.Settings.VisualReduceMotion, Is.EqualTo(MotionPreference.System));
            Assert.That(service.Settings.VisualSnapRadius, Is.EqualTo(40));
        });
    }

    [Test]
    public void A_file_that_is_not_json_falls_back_to_the_defaults_and_is_left_exactly_as_it_was()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json at all");

        var service = new SettingsService(_path);
        string? reason = null;
        service.LoadFailed += message => reason = message;

        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Not.Null, "the reason is reported; a silent reset is how the original bug hid");
            Assert.That(service.Settings.VisualSnapEnabled, Is.True, "the defaults are in force");
            Assert.That(File.ReadAllText(_path), Is.EqualTo("{ not json at all"),
                "and the file survives, so it can be inspected or repaired by hand");
        });
    }

    [Test]
    public void A_truncated_file_falls_back_to_the_defaults_rather_than_throwing_out_of_startup()
    {
        // The shape a crash mid-write leaves behind, and the reason the write is atomic.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"theme\": \"Light\", \"visualSnapRa");

        var service = new SettingsService(_path);
        Assert.That(() => service.Load(), Throws.Nothing, "a broken file must never stop the window opening");

        Assert.That(service.Settings.Theme, Is.EqualTo(AppTheme.Dark),
            "all the defaults, because half a file is not half a set of preferences");
    }

    [Test]
    public void A_partial_file_keeps_the_keys_it_has_and_defaults_the_rest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"accent\": \"Aqua\", \"visualReduceMotion\": \"Always\" }");

        var service = new SettingsService(_path);
        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.Accent, Is.EqualTo("Aqua"));
            Assert.That(service.Settings.VisualReduceMotion, Is.EqualTo(MotionPreference.Always));
            Assert.That(service.Settings.Theme, Is.EqualTo(AppTheme.Dark),
                "a key the file never mentioned gets its default, not a zero");
            Assert.That(service.Settings.VisualSnapRadius, Is.EqualTo(40));
        });
    }

    [Test]
    public void A_value_outside_its_range_is_repaired_when_the_file_is_read_and_the_repair_is_announced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"visualSnapRadius\": 400, \"accent\": \"Aqua\" }");

        var service = new SettingsService(_path);
        var announcements = new List<string>();
        service.SettingsRepaired += announcements.Add;

        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.VisualSnapRadius, Is.EqualTo(96), "clamped to the largest radius that works");
            Assert.That(service.Settings.Accent, Is.EqualTo("Aqua"),
                "one absurd number does not cost the user the rest of their preferences");
            Assert.That(announcements, Has.Count.EqualTo(1), "and the clamp says so, because a value that "
                + "changed silently is indistinguishable from one that was ignored");
            Assert.That(announcements[0], Does.Contain("visualSnapRadius").Or.Contain("VisualSnapRadius"));
        });
    }

    [Test]
    public void A_file_with_nothing_wrong_in_it_is_never_reported_as_repaired()
    {
        var service = new SettingsService(_path);
        service.Load();
        service.Update(settings => settings.VisualSnapRadius = 33);

        var announcements = 0;
        service.SettingsRepaired += _ => announcements++;
        service.Load();

        Assert.That(announcements, Is.Zero, "otherwise the signal is noise and stops being read");
    }

    [Test]
    public void A_repair_says_what_it_changed_and_to_what()
    {
        // The sentences exist so a caller can tell the user their file was adjusted. A silent clamp looks
        // exactly like a bug: a magnet radius of 400 that quietly became 96 would be reported as "the
        // setting ignores me".
        var settings = new AppSettings
        {
            VisualSnapRadius = 400,
            VisualStageStepDelayMs = 9000,
        };

        var repairs = settings.Normalise();

        Assert.Multiple(() =>
        {
            Assert.That(repairs, Has.Count.EqualTo(2), "one per repaired key, and nothing else is mentioned");
            Assert.That(repairs[0], Does.Contain("VisualSnapRadius").And.Contain("400").And.Contain("96"));
            Assert.That(repairs[1], Does.Contain("VisualStageStepDelayMs").And.Contain("9000").And.Contain("1000"));
            Assert.That(settings.Normalise(), Is.Empty, "and a second pass has nothing left to say");
        });
    }

    [Test]
    public void A_null_list_in_the_file_does_not_survive_the_load()
    {
        // An explicit `"recentWorkspaces": null` deserialises to a null property and the next Insert on it is
        // a NullReferenceException from opening a project - the one action the user cannot avoid.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            "{ \"recentWorkspaces\": null, \"disabledExtensions\": null, \"visualBreakpoints\": null }");

        var service = new SettingsService(_path);
        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(() => service.AddRecentWorkspace("C:\\x.slnx"), Throws.Nothing);
            Assert.That(service.Settings.RecentWorkspaces, Has.Count.EqualTo(1));
            Assert.That(service.Settings.DisabledExtensions, Is.Empty);
            Assert.That(service.Settings.VisualBreakpoints, Is.Empty);
        });
    }

    [Test]
    public void Every_one_of_the_sixteen_keys_comes_back_exactly_as_it_went_in()
    {
        var service = new SettingsService(_path);
        service.Load();

        service.Update(settings =>
        {
            settings.VisualSnapEnabled = false;
            settings.VisualSnapRadius = 72;
            settings.VisualSnapToGrid = true;
            settings.VisualGridSize = 24;
            settings.VisualDefaultZoom = 150;
            settings.VisualMinimapVisible = false;
            settings.VisualPaletteShowDeprecated = true;
            settings.VisualAutosaveSeconds = 30;
            settings.VisualReduceMotion = MotionPreference.Never;
            settings.VisualBlockTextSize = 16;
            settings.VisualStageAllowNetwork = true;
            settings.VisualStageStepDelayMs = 500;
            settings.VisualBreakpoints.AddRange(["b-1", "b-2"]);
            settings.VisualCannedResponses["https://example.test/*"] = "{}";
            settings.VisualShowBlockCode = true;
            settings.VisualOnboardingSeen = true;
        });

        var reloaded = new SettingsService(_path);
        reloaded.Load();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Settings.VisualSnapEnabled, Is.False);
            Assert.That(reloaded.Settings.VisualSnapRadius, Is.EqualTo(72));
            Assert.That(reloaded.Settings.VisualSnapToGrid, Is.True);
            Assert.That(reloaded.Settings.VisualGridSize, Is.EqualTo(24));
            Assert.That(reloaded.Settings.VisualDefaultZoom, Is.EqualTo(150));
            Assert.That(reloaded.Settings.VisualMinimapVisible, Is.False);
            Assert.That(reloaded.Settings.VisualPaletteShowDeprecated, Is.True);
            Assert.That(reloaded.Settings.VisualAutosaveSeconds, Is.EqualTo(30));
            Assert.That(reloaded.Settings.VisualReduceMotion, Is.EqualTo(MotionPreference.Never));
            Assert.That(reloaded.Settings.VisualBlockTextSize, Is.EqualTo(16));
            Assert.That(reloaded.Settings.VisualStageAllowNetwork, Is.True);
            Assert.That(reloaded.Settings.VisualStageStepDelayMs, Is.EqualTo(500));
            Assert.That(reloaded.Settings.VisualBreakpoints, Is.EqualTo(new[] { "b-1", "b-2" }));
            Assert.That(reloaded.Settings.VisualCannedResponses["https://example.test/*"], Is.EqualTo("{}"));
            Assert.That(reloaded.Settings.VisualShowBlockCode, Is.True);
            Assert.That(reloaded.Settings.VisualOnboardingSeen, Is.True);
        });
    }

    [Test]
    public void The_sixteen_keys_are_written_under_the_names_Part_20_gives_them()
    {
        // The JSON names are the contract with a file a user can edit, so they are pinned rather than
        // left to whatever the naming policy does to a property that gets renamed.
        var service = new SettingsService(_path);
        service.Load();
        service.Save();

        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var written = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        var expected = SettingCatalog.Ids
            .Select(id => char.ToLowerInvariant(id[0]) + id[1..])
            .ToList();

        Assert.Multiple(() =>
        {
            foreach (var name in expected)
            {
                Assert.That(written, Does.Contain(name), $"the file has no '{name}'");
            }

            Assert.That(
                document.RootElement.GetProperty("visualReduceMotion").GetString(),
                Is.EqualTo("System"),
                "and an enum is written as its name, so the file is readable by a person");
        });
    }

    [Test]
    public void A_successful_save_leaves_no_temporary_file_and_a_file_that_parses()
    {
        var service = new SettingsService(_path);
        service.Load();

        Assert.Multiple(() =>
        {
            Assert.That(service.Save(), Is.True);
            Assert.That(File.Exists(_path), Is.True);
            Assert.That(File.Exists(_path + ".tmp"), Is.False,
                "the temporary is renamed into place, not left beside it");
            Assert.That(() => JsonDocument.Parse(File.ReadAllText(_path)), Throws.Nothing);
        });
    }

    [Test]
    public void A_save_that_cannot_replace_the_file_leaves_the_previous_one_readable_and_says_it_failed()
    {
        var service = new SettingsService(_path);
        service.Load();
        service.Update(settings => settings.Accent = "Aqua");
        var good = File.ReadAllText(_path);

        // An exclusive hold on the file is what a backup tool or a second DeckForge looks like from here,
        // and on Windows it makes the replace fail while leaving the write-beside succeed. That is the
        // whole point of writing beside first: the file on disk is still the last good one.
        var saved = true;
        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.Update(settings => settings.Accent = "Lime");
            saved = service.Save();
        }

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.False, "and the caller is told, rather than being handed a crash banner");
            Assert.That(File.ReadAllText(_path), Is.EqualTo(good), "the previous file is intact");
            Assert.That(() => JsonDocument.Parse(File.ReadAllText(_path)), Throws.Nothing,
                "and still parses, which is the property the write-beside exists to guarantee");
            Assert.That(File.Exists(_path + ".tmp"), Is.False,
                "and the half-written temporary is cleaned up, because the page has a button that opens this folder");
        });
    }

    [Test]
    public void A_failed_save_does_not_announce_that_the_settings_changed()
    {
        var service = new SettingsService(_path);
        service.Load();
        service.Update(settings => settings.Accent = "Aqua");

        var announced = 0;
        service.SettingsChanged += () => announced++;

        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.Save();
        }

        Assert.That(announced, Is.Zero,
            "the theme and the motion policy re-apply on this signal, and neither should be told to do "
            + "nothing because the change never reached the disk");
    }

    [Test]
    public void One_save_raises_one_notification()
    {
        var service = new SettingsService(_path);
        service.Load();

        var announced = 0;
        service.SettingsChanged += () => announced++;

        service.Update(settings => settings.Accent = "Aqua");
        service.Update(settings => settings.Accent = "Lime");

        Assert.That(announced, Is.EqualTo(2),
            "one per save, so a caller that re-applies the theme does not do it twice for one keystroke");
    }

    [Test]
    public void Removing_a_workspace_that_was_never_there_does_not_rewrite_the_file()
    {
        var service = new SettingsService(_path);
        service.Load();
        service.Update(settings => settings.RecentWorkspaces.Add("C:\\kept.slnx"));
        var written = File.GetLastWriteTimeUtc(_path);

        var announced = 0;
        service.SettingsChanged += () => announced++;
        service.RemoveRecentWorkspace("C:\\never-opened.slnx");

        Assert.Multiple(() =>
        {
            Assert.That(announced, Is.Zero, "nothing changed, so nothing is announced");
            Assert.That(File.GetLastWriteTimeUtc(_path), Is.EqualTo(written));
        });
    }

    [Test]
    public void A_recent_list_keeps_the_newest_first_and_stops_at_ten()
    {
        var service = new SettingsService(_path);
        service.Load();

        for (var i = 0; i < 12; i++)
        {
            service.AddRecentWorkspace($"C:\\p{i}.slnx");
        }

        // Re-adding the one that is already first must move it, not duplicate it.
        service.AddRecentWorkspace("C:\\p11.slnx");

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.RecentWorkspaces, Has.Count.EqualTo(10),
                "a recency list stops being a shortcut once it is a history");
            Assert.That(service.Settings.RecentWorkspaces[0], Is.EqualTo("C:\\p11.slnx"));
            Assert.That(service.Settings.RecentWorkspaces.Distinct().Count(), Is.EqualTo(10), "and has no duplicates");
        });
    }

    [Test]
    public void A_save_creates_the_folder_it_needs()
    {
        // The path under test is inside a directory that does not exist, so this is the first run.
        var service = new SettingsService(_path);
        service.Load();

        Assert.That(service.Save(), Is.True, "and it is true on the very first save, not only after a failure");
        Assert.That(File.Exists(_path), Is.True);
    }
}