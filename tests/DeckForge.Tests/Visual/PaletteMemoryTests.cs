using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// What a workspace remembers about how its author uses the palette.
/// </summary>
/// <remarks>
/// Small rules, and small rules are where the wrongness hides: a recency list that keeps duplicates, a cap
/// that drops the newest instead of the oldest, a star that unpins when a block is merely used. Each of
/// those is invisible in a screenshot and obvious to somebody who uses the palette for an hour.
/// </remarks>
[TestFixture]
public sealed class PaletteMemoryTests
{
    [Test]
    public void The_block_you_just_used_is_the_first_one_in_the_recent_row()
    {
        var memory = new PaletteMemory();

        memory.Note("var.set");
        memory.Note("control.repeat");

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Is.EqualTo(new[] { "control.repeat", "var.set" }));
            Assert.That(memory.Recents.First(), Is.EqualTo("control.repeat"));
        });
    }

    [Test]
    public void Using_a_block_again_moves_it_rather_than_listing_it_twice()
    {
        var memory = new PaletteMemory();

        memory.Note("var.set");
        memory.Note("ui.log");
        memory.Note("var.set");

        Assert.That(memory.Recents, Is.EqualTo(new[] { "var.set", "ui.log" }),
            "a row that repeats a block is a row that has quietly become a history instead of a recency");
    }

    [Test]
    public void The_row_is_capped_and_the_cap_keeps_the_newest()
    {
        var memory = new PaletteMemory();

        for (var index = 0; index < PaletteMemory.RecentsLimit + 6; index++)
        {
            memory.Note($"block.{index}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Has.Count.EqualTo(PaletteMemory.RecentsLimit));
            Assert.That(memory.Recents.First(), Is.EqualTo($"block.{PaletteMemory.RecentsLimit + 5}"),
                "the newest block is still there, which is the only entry a user would notice missing");
            Assert.That(memory.Recents, Does.Not.Contain("block.0"),
                "and the oldest has gone, which is the point of a cap");
        });
    }

    [Test]
    public void Nothing_is_recorded_for_a_block_with_no_name()
    {
        var memory = new PaletteMemory();

        memory.Note("   ");

        Assert.That(memory.Recents, Is.Empty,
            "a blank kind in the row is a row that cannot be dragged from, and it is how a bug that "
            + "records the wrong thing looks");
    }

    [Test]
    public void Pinning_and_unpinning_are_the_same_operation_and_both_are_quiet()
    {
        var memory = new PaletteMemory();

        Assert.Multiple(() =>
        {
            Assert.That(memory.ToggleFavourite("var.set"), Is.True);
            Assert.That(memory.IsFavourite("var.set"), Is.True);
            Assert.That(memory.ToggleFavourite("var.set"), Is.False);
            Assert.That(memory.IsFavourite("var.set"), Is.False);
            Assert.That(memory.ToggleFavourite("var.set"), Is.True, "and it can be pinned again");
        });
    }

    [Test]
    public void Using_a_pinned_block_does_not_unpin_it()
    {
        // The obvious bug: one handler for both the star and the recency, so the row you use most becomes
        // the row you cannot find.
        var memory = new PaletteMemory();

        memory.ToggleFavourite("var.set");
        memory.Note("var.set");
        memory.Note("ui.log");

        Assert.That(memory.IsFavourite("var.set"), Is.True);
    }

    [Test]
    public void Favourites_keep_the_order_they_were_pinned_in()
    {
        var memory = new PaletteMemory();

        memory.ToggleFavourite("first");
        memory.ToggleFavourite("second");

        Assert.That(memory.ToggleFavourite("first"), Is.False, "toggling a pinned block unpins it");

        memory.ToggleFavourite("third");
        memory.ToggleFavourite("first");

        Assert.That(memory.Favourites, Is.EqualTo(new[] { "second", "third", "first" }),
            "re-pinning puts a block at the end rather than back where it was, because the row reads as "
            + "the order the user chose them - and a star that never moved would make the list a set with "
            + "extra steps");
    }

    [Test]
    public void A_block_the_catalogue_no_longer_has_is_forgotten_from_both_lists()
    {
        // A row that cannot be dragged from is worse than no row, and a star that unpins nothing is worse
        // than no star.
        var memory = new PaletteMemory();

        memory.Note("still.here");
        memory.Note("gone.away");
        memory.ToggleFavourite("gone.away");
        memory.ToggleFavourite("still.here");

        memory.PruneTo(new HashSet<string>(["still.here"], StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Is.EqualTo(new[] { "still.here" }));
            Assert.That(memory.Favourites, Is.EqualTo(new[] { "still.here" }));
        });
    }

    [Test]
    public void What_was_stored_comes_back_in_the_same_order()
    {
        var state = new PaletteMemoryState(
            ["control.repeat", "var.set", "removed.block"],
            ["var.set", "removed.block"]);

        var memory = PaletteMemory.Restore(state, new HashSet<string>(["control.repeat", "var.set"], StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Is.EqualTo(new[] { "control.repeat", "var.set" }),
                "most recent first survives the round trip");
            Assert.That(memory.Favourites, Is.EqualTo(new[] { "var.set" }),
                "and a pinned block the catalogue dropped is gone, not resurrected");
        });
    }

    [Test]
    public void A_workspace_with_no_palette_memory_starts_with_two_empty_lists_rather_than_null()
    {
        var memory = PaletteMemory.Restore(null, new HashSet<string>(StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Is.Empty);
            Assert.That(memory.Favourites, Is.Empty);
            Assert.That(memory.IsFavourite("anything"), Is.False);
        });
    }

    [Test]
    public void The_stored_state_survives_its_own_round_trip()
    {
        var memory = new PaletteMemory();
        memory.Note("var.set");
        memory.Note("ui.log");
        memory.ToggleFavourite("var.set");

        var known = new HashSet<string>(["var.set", "ui.log"], StringComparer.Ordinal);
        var again = PaletteMemory.Restore(memory.ToState(), known);

        Assert.Multiple(() =>
        {
            Assert.That(again.Recents, Is.EqualTo(memory.Recents));
            Assert.That(again.Favourites, Is.EqualTo(memory.Favourites));
        });
    }

    [Test]
    public void Every_kind_in_the_catalogue_can_be_pinned_and_recalled()
    {
        // The real catalogue, not a fixture of three names: a memory that worked on the kinds a test
        // invented and not on the ones that ship is a memory that does not work.
        var known = BlockCatalog.Blocks.Select(block => block.Kind).ToHashSet(StringComparer.Ordinal);
        var memory = new PaletteMemory();

        foreach (var block in BlockCatalog.Blocks)
        {
            memory.Note(block.Kind);
            memory.ToggleFavourite(block.Kind);
        }

        Assert.Multiple(() =>
        {
            Assert.That(memory.Recents, Has.Count.EqualTo(PaletteMemory.RecentsLimit));
            Assert.That(memory.Favourites, Has.Count.EqualTo(known.Count));
            Assert.That(
                PaletteMemory.Restore(memory.ToState(), known).Favourites,
                Has.Count.EqualTo(known.Count),
                "and the cap applies only to the recents: a user with forty pinned blocks has forty "
                + "pinned blocks");
        });
    }
}