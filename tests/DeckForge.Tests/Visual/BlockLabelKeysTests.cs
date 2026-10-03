using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// §21's localization seam: what a block's strings are keyed by, and what happens when one is missing.
/// </summary>
/// <remarks>
/// The property being protected is that a missing translation is boring. A build with no localization loaded
/// is the normal build, so the fallback is the common path, and it has to produce English rather than an
/// empty label — an empty label on the canvas looks like a rendering fault rather than a missing string, and
/// that is a bug report nobody can act on.
/// </remarks>
[TestFixture]
public sealed class BlockLabelKeysTests
{
    private static BlockDescriptor Row(string kind) =>
        BlockCatalog.Find(kind) ?? throw new InvalidOperationException($"{kind} is not in the catalogue");

    [Test]
    public void A_label_key_is_the_documented_shape()
    {
        Assert.That(
            BlockLabelKeys.Label(Row("deck.open-folder")),
            Is.EqualTo("Blocks.Deck.DeckOpenFolder"),
            "Part 21.1's table: Blocks.<Category>.<BlockId>");
    }

    [Test]
    public void A_slot_and_a_menu_key_hang_off_the_label_key()
    {
        var log = Row("ui.log");

        Assert.Multiple(() =>
        {
            Assert.That(BlockLabelKeys.Slot(log, (log.Slots ?? []).First(slot => slot.Name == "template")),
                Is.EqualTo("Blocks.Ui.UiLog.Slot.Template"));
            Assert.That(BlockLabelKeys.Menu(log, "Warning"), Is.EqualTo("Blocks.Ui.UiLog.Menu.Warning"));
        });
    }

    [Test]
    public void A_category_and_a_hat_have_keys_of_their_own()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockLabelKeys.Category(BlockCategory.Control), Is.EqualTo("Blocks.Category.Control"));
            Assert.That(
                BlockLabelKeys.Hat(Row("hat.action-runs")),
                Is.EqualTo("Blocks.Hat.HatActionRuns"));
        });
    }

    [Test]
    public void A_kind_with_dashes_becomes_one_pascal_case_word()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockLabelKeys.LabelId("deck.open-folder-on-client"),
                Is.EqualTo("DeckOpenFolderOnClient"));
            Assert.That(BlockLabelKeys.Id("long-press"), Is.EqualTo("LongPress"));
        });
    }

    [Test]
    public void Every_block_in_the_catalogue_has_a_label_key_that_is_unique()
    {
        var keys = BlockCatalog.Blocks.Select(block => BlockLabelKeys.Label(Row(block.Kind))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.Unique,
                "two blocks sharing a key means one of them silently takes the other's translation");
            Assert.That(keys.All(key => key.StartsWith("Blocks.", StringComparison.Ordinal)), Is.True);
            Assert.That(keys.All(key => key.Split('.').Length == 3), Is.True,
                "exactly three segments, which is what a resx file's nesting will assume");
        });
    }

    [Test]
    public void Every_slot_and_menu_of_every_block_has_a_key_that_is_unique()
    {
        var keys = new List<string>();

        foreach (var kind in BlockCatalog.Blocks.Select(block => block.Kind))
        {
            var descriptor = Row(kind);
            keys.AddRange((descriptor.Slots ?? []).Select(slot => BlockLabelKeys.Slot(descriptor, slot)));
            keys.AddRange((descriptor.Menus ?? []).SelectMany(menu => menu.Options.Select(option => BlockLabelKeys.Menu(descriptor, option))));
        }

        Assert.That(keys, Is.Unique,
            "a slot and a menu option sharing a key is the same failure as two blocks sharing one, and just "
            + "as invisible");
    }

    [Test]
    public void Every_key_is_a_legal_resx_key()
    {
        var keys = BlockCatalog.Blocks
            .Select(block => BlockLabelKeys.Label(Row(block.Kind)))
            .Concat(BlockCatalog.Categories.Select(category => BlockLabelKeys.Category(category.Category)))
            .ToList();

        foreach (var key in keys)
        {
            Assert.Multiple(() =>
            {
                Assert.That(key, Does.Not.Contain(" "), $"{key} has a space in it");
                Assert.That(key, Does.Not.Contain("-"), $"{key} has a dash in it");
                Assert.That(key, Does.Match("^[A-Za-z0-9_.]+$"), $"{key} is not a legal resx key");
            });
        }
    }

    [Test]
    public void A_lookup_with_nothing_in_it_leaves_every_label_in_english()
    {
        var descriptor = Row("ui.log");

        Assert.That(
            BlockLabelKeys.Text(NoTranslations.Instance, BlockLabelKeys.Label(descriptor), descriptor.Label),
            Is.EqualTo(descriptor.Label),
            "a build with no localization loaded is the normal build, and a blank label on the canvas looks "
            + "like a rendering fault");
    }

    [Test]
    public void A_translation_is_used_when_there_is_one()
    {
        var lookup = new SingleTranslation(BlockLabelKeys.Label(Row("ui.log")), "Protokoll schreiben");

        Assert.That(
            BlockLabelKeys.Text(lookup, BlockLabelKeys.Label(Row("ui.log")), "log"),
            Is.EqualTo("Protokoll schreiben"));
    }

    [Test]
    public void An_empty_translation_is_treated_as_no_translation_rather_than_as_an_empty_label()
    {
        // The distinction is the whole reason the interface returns null rather than a string: "" is a
        // translation of nothing, and a label with nothing in it is the failure this design exists to avoid.
        var lookup = new SingleTranslation(BlockLabelKeys.Label(Row("ui.log")), string.Empty);

        Assert.That(
            BlockLabelKeys.Text(lookup, BlockLabelKeys.Label(Row("ui.log")), "log"),
            Is.EqualTo("log"));
    }

    [Test]
    public void A_key_that_survives_a_rename_is_a_translation_that_is_not_orphaned()
    {
        // The reason LabelId strips dashes and PascalCases rather than using the catalogue kind: a key built
        // from "deck.open-folder" would change if the row were ever renamed, and every translation would
        // become an orphan in a file nobody reads - with nothing reporting a problem.
        var before = BlockLabelKeys.Label(Row("deck.open-folder"));

        Assert.Multiple(() =>
        {
            Assert.That(before, Does.Not.Contain("deck.open-folder"),
                "a key containing the catalogue kind moves with it");
            Assert.That(before, Does.Not.Contain("-"), "and so does a key containing the row's dashes");
            Assert.That(before.Split('.').Last(), Is.EqualTo("DeckOpenFolder"));
        });
    }

    /// <summary>A lookup holding exactly one translation.</summary>
    private sealed class SingleTranslation(string key, string value) : IBlockTextLookup
    {
        public string? Find(string candidate) => string.Equals(candidate, key, StringComparison.Ordinal) ? value : null;
    }
}