using System.Text;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// §21's labels, resolved through a real <c>Strings.resx</c>, and what happens when the file is not
/// cooperating.
/// </summary>
/// <remarks>
/// The properties being protected are that a translator's work reaches the canvas, and that nothing about a
/// hand-edited resx can take the page down. Both matter: the first is the feature, and the second is the
/// difference between a missing translation and a lost session.
/// </remarks>
[TestFixture]
public sealed class ResxTextLookupTests
{
    private TempDirectory _temp = null!;

    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("deckforge-blocklabels");
        _directory = _temp.Root;
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    private static BlockDescriptor Row(string kind) =>
        BlockCatalog.Find(kind) ?? throw new InvalidOperationException($"{kind} is not in the catalogue");

    private void WriteResx(string name, params (string Key, string Value)[] entries)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><root>");
        builder.Append("<resheader name=\"version\"><value>2.0</value></resheader>");

        foreach (var (key, value) in entries)
        {
            builder.Append($"<data name=\"{key}\" xml:space=\"preserve\"><value>{value}</value></data>");
        }

        builder.Append("</root>");
        File.WriteAllText(Path.Combine(_directory, name), builder.ToString(), Encoding.UTF8);
    }

    [Test]
    public void A_translated_label_reaches_the_tiles_words()
    {
        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(Row("ui.log")), "Protokoll schreiben"));

        var runs = BlockLabel.Plan(Row("ui.log"), new ResxTextLookup(_directory, "de"));

        Assert.That(runs.Any(run => run.Text == "Protokoll schreiben"), Is.True,
            "the whole point: a translator fills in one row and the block says it");
    }

    [Test]
    public void A_translated_label_keeps_its_holes_and_still_produces_editable_slots()
    {
        var row = BlockCatalog.Find("ui.log")!;
        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(row), "Schreibe {template}"));

        var runs = BlockLabel.Plan(row, new ResxTextLookup(_directory, "de"));

        Assert.Multiple(() =>
        {
            Assert.That(runs.Any(run => run.Kind == BlockLabelRunKind.Text && run.Text == "Schreibe"), Is.True);
            Assert.That(runs.Any(run => run.Kind == BlockLabelRunKind.Slot && run.Name == "template"), Is.True,
                "the translator reorders the words but the holes still have to be slots, or the block stops "
                + "being editable");
            Assert.That(runs.Any(run => run.Text.Contains('{')), Is.False);
        });
    }

    [Test]
    public void A_translation_whose_placeholders_do_not_match_the_row_shows_the_marker_rather_than_dropping_it()
    {
        var row = BlockCatalog.Find("ui.log")!;
        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(row), "Schreibe {nope}"));

        var runs = BlockLabel.Plan(row, new ResxTextLookup(_directory, "de"));

        Assert.That(runs.Any(run => run.Text.Contains("{nope}")), Is.True,
            "the same degradation as an English label with a bad hole: visible on the block rather than "
            + "silently truncated");
    }

    [Test]
    public void A_missing_translation_falls_back_to_the_neutral_file_then_to_english()
    {
        var row = BlockCatalog.Find("ui.log")!;
        WriteResx("Strings.resx", (BlockLabelKeys.Label(row), "Write {template}"));
        WriteResx("Strings.de.resx", ("Blocks.SomethingElse", "Etwas"));

        var lookup = new ResxTextLookup(_directory, "de");

        Assert.Multiple(() =>
        {
            Assert.That(BlockLabel.PreviewText(row, lookup), Does.StartWith("Write"),
                "a partially translated file is normal; the neutral file is the safety net");
            Assert.That(BlockLabel.PreviewText(Row("ui.notify"), lookup), Is.EqualTo(BlockLabel.PreviewText(Row("ui.notify"))),
                "and a key nobody translated is the catalogue's English");
        });
    }

    [Test]
    public void A_menu_option_is_translated_only_when_it_is_one_of_the_rows_own_options()
    {
        var row = Row("ui.log");
        var menu = (row.Menus ?? []).First(candidate => candidate.Options.Count > 0);
        var option = menu.Options[0];
        var stale = menu.Options[0] + "-from-an-older-row";

        WriteResx("Strings.de.resx", (BlockLabelKeys.Menu(row, option), "Warnung"));

        var lookup = new ResxTextLookup(_directory, "de");

        Assert.Multiple(() =>
        {
            Assert.That(BlockLabel.MenuText(row, menu, option, lookup), Is.EqualTo("Warnung"));
            Assert.That(BlockLabel.MenuText(row, menu, stale, lookup), Is.EqualTo(stale),
                "the chosen value is the user's data; translating a value the row does not declare is a "
                + "miss reported as a wrong word");
        });
    }

    [Test]
    public void An_empty_value_in_the_resx_is_treated_as_no_translation()
    {
        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(Row("ui.log")), string.Empty));

        Assert.That(
            BlockLabel.PreviewText(Row("ui.log"), new ResxTextLookup(_directory, "de")),
            Is.EqualTo(BlockLabel.PreviewText(Row("ui.log"))));
    }

    [Test]
    public void An_unset_menu_shows_its_default_translated_and_a_menu_with_no_default_shows_nothing()
    {
        // The only branch MenuText's tests never reached: a value nobody chose. A menu whose default is not
        // one of its declared options is nonsense data, and the row has to say something rather than throw.
        var row = Row("ui.log");
        var menu = (row.Menus ?? []).First(candidate => candidate.Options.Count > 0);
        var option = menu.Options[0];
        var noDefault = menu with { Default = null! };

        WriteResx("Strings.de.resx", (BlockLabelKeys.Menu(row, menu.Default), "Warnung"));
        var lookup = new ResxTextLookup(_directory, "de");

        Assert.Multiple(() =>
        {
            Assert.That(BlockLabel.MenuText(row, menu, null, lookup), Is.EqualTo("Warnung"),
                "an unset menu falls back to its default, and the default is translatable like any option");
            Assert.That(BlockLabel.MenuText(row, menu, "   ", lookup), Is.EqualTo("Warnung"),
                "whitespace is not a choice either");
            Assert.That(BlockLabel.MenuText(row, noDefault, null, lookup), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void A_workspace_with_no_resx_at_all_answers_nothing_and_says_so()
    {
        var lookup = new ResxTextLookup(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Loaded, Is.False);
            Assert.That(lookup.Find(BlockLabelKeys.Label(Row("ui.log"))), Is.Null);
            Assert.That(BlockLabel.PreviewText(Row("ui.log"), lookup), Is.EqualTo(BlockLabel.PreviewText(Row("ui.log"))));
        });
    }

    [Test]
    public void A_directory_that_does_not_exist_is_a_missing_file_rather_than_an_error()
    {
        var lookup = new ResxTextLookup(Path.Combine(_directory, "no-such-place"));

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Loaded, Is.False);
            Assert.That(() => lookup.Find("Blocks.Control.ControlIf"), Throws.Nothing);
        });
    }

    [Test]
    public void A_malformed_resx_leaves_the_labels_in_english_instead_of_taking_the_page_down()
    {
        // The palette is where you would fix a broken resx, so a broken resx that throws there has removed
        // the only way to reach the fix.
        File.WriteAllText(Path.Combine(_directory, "Strings.resx"), "<root><data name=\"a\"><value>b</root>");

        var lookup = new ResxTextLookup(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(lookup.Loaded, Is.False);
            Assert.That(BlockLabel.PreviewText(Row("ui.log"), lookup), Is.EqualTo(BlockLabel.PreviewText(Row("ui.log"))));
        });
    }

    [Test]
    public void A_duplicated_key_keeps_the_first_and_carries_on()
    {
        File.WriteAllText(
            Path.Combine(_directory, "Strings.resx"),
            "<root><data name=\"k\"><value>first</value></data><data name=\"k\"><value>second</value></data></root>");

        Assert.That(new ResxTextLookup(_directory).Find("k"), Is.EqualTo("first"),
            "the convention ResxMerger already follows; a resx reader that throws on the second copy is how "
            + "a Localization page took the whole app down once");
    }

    [Test]
    public void A_file_with_a_key_but_no_value_is_a_key_with_nothing_in_it()
    {
        File.WriteAllText(Path.Combine(_directory, "Strings.resx"), "<root><data name=\"k\" /></root>");

        Assert.That(new ResxTextLookup(_directory).Find("k"), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Reload_picks_up_a_translation_saved_after_the_lookup_was_built()
    {
        var lookup = new ResxTextLookup(_directory, "de");
        var row = Row("ui.log");

        Assert.That(BlockLabel.PreviewText(row, lookup), Is.EqualTo(BlockLabel.PreviewText(row)));

        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(row), "Protokoll"));
        lookup.Reload();

        Assert.That(BlockLabel.PreviewText(row, lookup), Does.StartWith("Protokoll"),
            "the Localization page saves and the canvas is already open");
    }

    [Test]
    public void Reload_forgets_what_it_knew_about_a_translation_that_has_been_removed()
    {
        var row = Row("ui.log");
        WriteResx("Strings.de.resx", (BlockLabelKeys.Label(row), "Protokoll"));
        var lookup = new ResxTextLookup(_directory, "de");

        File.Delete(Path.Combine(_directory, "Strings.de.resx"));
        lookup.Reload();

        Assert.That(BlockLabel.PreviewText(row, lookup), Is.EqualTo(BlockLabel.PreviewText(row)),
            "a stale cache is worse than no cache: the editor would keep saying a word the file no longer has");
    }

    [Test]
    public void The_invariant_culture_asks_for_no_culture_file_rather_than_for_one_with_two_dots()
    {
        // Strings..resx is a different file from Strings.resx, so an invariant culture that built the name
        // from a null string would silently find nothing in every project.
        var row = Row("ui.log");
        WriteResx("Strings.resx", (BlockLabelKeys.Label(row), "Write {template}"));
        File.WriteAllText(Path.Combine(_directory, "Strings..resx"), "<root><data name=\"k\"><value>wrong</value></data></root>");

        var lookup = new ResxTextLookup(_directory, ResxTextLookup.CurrentCultureName);

        Assert.That(BlockLabel.PreviewText(row, lookup), Does.StartWith("Write"));
    }
}
