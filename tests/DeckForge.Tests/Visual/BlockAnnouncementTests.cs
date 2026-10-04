using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// What a screen reader is told about a block, which is text and therefore testable.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.8 asks a tile to expose "repeat 10, C block, 3 statements inside" through an automation peer.
/// The peer is WPF and this project may not reference WPF, so the string the peer announces is computed in
/// Core and pinned here — a test that can only assert "an AutomationPeer subclass exists" would pass with a
/// peer that announced the letter "b", and that is the failure this file exists to make impossible.
/// </para>
/// <para>
/// The block's shape is in every sentence on purpose. Shape is the type system made visible in this
/// editor: Part 9.5 refuses a drop because two silhouettes do not fit, and a user who cannot see the
/// silhouettes cannot know why.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockAnnouncementTests
{
    [Test]
    public void The_announcement_is_the_sentence_part_9_8_asks_for()
    {
        var repeat = Repeat(count: 10, statements: 3);

        Assert.That(
            BlockAnnouncement.Describe(repeat),
            Is.EqualTo("repeat 10, C block, 3 statements inside"),
            "the design wrote one concrete sentence; paraphrasing it would be a second opinion about what a "
            + "screen reader should hear");
    }

    [Test]
    public void One_statement_is_singular_and_the_count_is_still_here()
    {
        var repeat = Repeat(count: 3, statements: 1);

        Assert.That(
            BlockAnnouncement.Describe(repeat),
            Is.EqualTo("repeat 3, C block, 1 statement inside"),
            "\"1 statements\" reads as a bug in the thing being read aloud, which is exactly the impression a "
            + "screen-reader string cannot afford");
    }

    [Test]
    public void An_empty_container_says_it_is_empty_rather_than_saying_nothing()
    {
        var repeat = Repeat(count: 3, statements: 0);

        Assert.That(
            BlockAnnouncement.Describe(repeat),
            Is.EqualTo("repeat 3, C block, nothing inside yet"),
            "an empty C-block and a C-block holding nothing are the same thing, and stopping at \"C block\" "
            + "tells the user nothing about whether their loop is finished");
    }

    [Test]
    public void The_label_carries_the_values_the_holes_hold_and_not_the_placeholders()
    {
        // BlockLabel.PreviewText is what the palette and the tooltips use, and it drops the holes on
        // purpose: "repeat" with no number is a different block from "repeat 10", and a preview is exactly
        // what a screen reader must not be given.
        var block = Set(name: "count", value: "42");

        Assert.Multiple(() =>
        {
            Assert.That(BlockAnnouncement.Label(block), Is.EqualTo("set count to 42"));
            Assert.That(BlockLabel.PreviewText(BlockCatalog.Find("var.set")!), Does.Contain("set "),
                "and the preview really is the hole-less reading this has to differ from");
        });
    }

    [Test]
    public void A_nested_reporter_is_read_as_its_own_label()
    {
        var change = new Block { Kind = "var.change", Id = "change-1" };
        change.Inputs["var"] = new BlockInput { Variable = "count" };
        change.Inputs["amount"] = new BlockInput { Block = Set("delta", "7") };

        Assert.That(
            BlockAnnouncement.Label(change),
            Is.EqualTo("change count by set delta to 7"),
            "a hole holding another block reads as that block, which is what the tile draws inside it");
    }

    [Test]
    public void An_announcement_says_a_block_is_disabled_before_it_describes_it()
    {
        var block = Set("count", "1");
        block.Disabled = true;

        Assert.Multiple(() =>
        {
            Assert.That(BlockAnnouncement.Describe(block), Is.EqualTo("set count to 1, stack block, disabled"),
                "\"disabled\" changes what the rest of the sentence means: everything after it describes a "
                + "block that will not run");
        });
    }

    [Test]
    public void The_position_is_appended_only_when_it_is_known()
    {
        var block = Set("count", "1");

        Assert.Multiple(() =>
        {
            Assert.That(BlockAnnouncement.Describe(block), Does.Not.Contain("block 1 of"),
                "a palette row is in no document and has no position, and inventing one is worse than "
                + "leaving it out");
            Assert.That(
                BlockAnnouncement.Describe(block, position: 4, of: 11),
                Is.EqualTo("set count to 1, stack block, block 4 of 11"),
                "and it is appended rather than leading, because the label is what identifies the block");
            Assert.That(BlockAnnouncement.Describe(block, position: 4, of: 0), Does.Not.Contain("of"),
                "half a position is not a position");
        });
    }

    [Test]
    public void A_kind_this_build_does_not_know_still_says_something_a_user_can_repeat_back()
    {
        var block = new Block { Kind = "mystery.block", Id = "b99" };

        Assert.Multiple(() =>
        {
            Assert.That(BlockAnnouncement.Describe(block),
                Is.EqualTo("mystery.block, block from a newer version of DeckForge"),
                "silence is the worst answer: a user reporting \"a block I cannot read\" cannot report a "
                + "string that is not there");
            Assert.That(BlockAnnouncement.Shape(block), Does.Contain("newer"),
                "and the shape says the block is unrecognised rather than guessing at one");
        });
    }

    [Test]
    public void A_reporter_says_it_holds_a_value_and_a_boolean_says_which_one()
    {
        var condition = new Block { Kind = "ops.compare", Id = "c1" };
        var left = new Block { Kind = "ops.number", Id = "n1" };
        left.Inputs["value"] = new BlockInput { Number = 3 };
        condition.Inputs["left"] = new BlockInput { Block = left };
        condition.Inputs["right"] = new BlockInput { Number = 4 };
        condition.Fields["op"] = "=";

        Assert.That(
            BlockAnnouncement.Shape(condition),
            Is.EqualTo("boolean reporter, holds true or false"),
            "a reader told only \"stack block\" for a condition has been told nothing about why the drop was "
            + "refused");
    }

    [Test]
    public void A_number_hole_reads_as_the_number_the_document_holds()
    {
        // The current culture, deliberately: a screen reader reading "1,5" aloud in a locale that writes
        // it with a comma is right, and forcing InvariantCulture produces a decimal point nobody speaks.
        var block = new Block { Kind = "control.wait-seconds", Id = "w1" };
        block.Inputs["seconds"] = new BlockInput { Number = 1.5 };

        Assert.That(
            BlockAnnouncement.Label(block),
            Is.EqualTo("wait " + (1.5).ToString(System.Globalization.CultureInfo.CurrentCulture) + " seconds"));
    }

    [Test]
    public void An_empty_hole_says_it_is_empty_rather_than_reading_as_nothing()
    {
        var block = new Block { Kind = "control.repeat", Id = "r1" };

        Assert.That(
            BlockAnnouncement.Label(block),
            Is.EqualTo("repeat empty"),
            "a hole with nothing in it is a fact about the document the user probably wants to hear, and "
            + "\"repeat \" read aloud is indistinguishable from a truncated label");
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>A <c>repeat {count}</c> with a body, built from the catalogue's own row.</summary>
    private static Block Repeat(double count, int statements)
    {
        var repeat = new Block { Kind = "control.repeat", Id = "repeat-1" };
        repeat.Inputs["count"] = new BlockInput { Number = count };

        var body = repeat.Body("body");
        for (var index = 0; index < statements; index++)
        {
            body.Add(Set($"v{index}", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return repeat;
    }

    /// <summary>A <c>set {var} to {value}</c>.</summary>
    private static Block Set(string name, string value)
    {
        var block = new Block { Kind = "var.set", Id = $"set-{name}" };
        block.Inputs["var"] = new BlockInput { Variable = name };
        block.Inputs["value"] = new BlockInput { Text = value };

        return block;
    }
}
