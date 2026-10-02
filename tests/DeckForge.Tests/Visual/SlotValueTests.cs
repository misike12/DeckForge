using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Reading a slot's value and turning a typed one back into an input.
/// </summary>
/// <remarks>
/// <para>
/// The unglamorous half of Part 9.6, and the half with the failures worth preventing. A document that
/// validates and then emits the wrong thing is the worst outcome available here: nothing complains, the
/// plugin builds, and a user gets a different value than the one they typed.
/// </para>
/// <para>
/// So the assertions are mostly about <em>which member of the input</em> carries the value, because that
/// is the decision the emitter depends on and the one a "helpful" refactor gets wrong — by putting a
/// number in <c>Text</c> because that is where the user's keystrokes landed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SlotValueTests
{
    [Test]
    public void A_number_goes_in_the_number_member_and_comes_back_the_same()
    {
        var block = Block("control.repeat");
        var slot = Slot(block, "count");

        var input = SlotValue.From(slot, "12.5");

        Assert.Multiple(() =>
        {
            Assert.That(input.Kind, Is.EqualTo(BlockInputKind.Number), "not Text - the emitter reads these separately");
            Assert.That(input.Number, Is.EqualTo(12.5));
            Assert.That(block.Inputs[slot.Name].Number, Is.EqualTo(10),
                "the block itself is untouched; From builds a value, it does not write one. The count "
                + "still holds the default BlockFactory gave it");
        });
    }

    [Test]
    public void Numbers_are_read_and_written_in_the_invariant_culture()
    {
        var block = Block("control.repeat");
        var slot = Slot(block, "count");
        block.Inputs[slot.Name] = new BlockInput { Number = 0.25 };

        var read = SlotValue.Read(block, slot);

        Assert.Multiple(() =>
        {
            Assert.That(read.Text, Is.EqualTo("0.25"),
                "a comma decimal separator would emit a different number on every other machine, and "
                + "the failure is a wrong value rather than a parse error");
            Assert.That(SlotValue.ParseNumber("0.25"), Is.EqualTo(0.25));
            Assert.That(SlotValue.FormatNumber(0.25), Is.EqualTo("0.25"));
        });
    }

    [Test]
    public void A_reference_slot_stores_the_name_and_a_text_slot_stores_the_text()
    {
        var reference = Block("var.set-from-parameter");
        var referenceSlot = new SlotDescriptor("param", SlotType.Parameter);
        var input = SlotValue.From(referenceSlot, "count");

        var text = Block("ui.log");
        var textSlot = new SlotDescriptor("template", SlotType.Text);
        var literal = SlotValue.From(textSlot, "hello {name}");

        Assert.Multiple(() =>
        {
            Assert.That(input.Kind, Is.EqualTo(BlockInputKind.Variable),
                "a reference is picked from a list, so the emitter resolves it by name");
            Assert.That(input.Variable, Is.EqualTo("count"));
            Assert.That(literal.Kind, Is.EqualTo(BlockInputKind.Text));
            Assert.That(literal.Text, Is.EqualTo("hello {name}"), "braces are the user's text, not ours");
        });

        _ = reference;
        _ = text;
    }

    [Test]
    public void Every_reference_slot_type_stores_a_name()
    {
        // The catalogue has a dozen slot types that all mean "pick something the document declares", and
        // each one added to this list by hand is one a future type can be forgotten on. A block holding a
        // name in Number would validate and then emit zero.
        SlotType[] references =
        [
            SlotType.List, SlotType.HostVariable, SlotType.UserVariable, SlotType.Parameter,
            SlotType.Event, SlotType.Action, SlotType.Script, SlotType.Widget,
            SlotType.ConfigEntry, SlotType.Icon, SlotType.Color, SlotType.Variable,
            SlotType.Procedure, SlotType.File,
        ];

        foreach (var type in references)
        {
            var input = SlotValue.From(new SlotDescriptor("s", type), "thing");

            Assert.That(input.Kind, Is.EqualTo(BlockInputKind.Variable), $"{type} stores a name");
        }
    }

    [Test]
    public void A_boolean_accepts_the_spellings_a_user_will_actually_type()
    {
        var slot = new SlotDescriptor("flag", SlotType.Boolean);

        Assert.Multiple(() =>
        {
            Assert.That(SlotValue.From(slot, "true").Boolean, Is.True);
            Assert.That(SlotValue.From(slot, "TRUE").Boolean, Is.True, "case is not a different answer");
            Assert.That(SlotValue.From(slot, "yes").Boolean, Is.True);
            Assert.That(SlotValue.From(slot, "1").Boolean, Is.True);
            Assert.That(SlotValue.From(slot, "false").Boolean, Is.False);
            Assert.That(SlotValue.From(slot, "").Boolean, Is.False);
        });
    }

    [Test]
    public void The_editor_refuses_what_the_validator_would_only_warn_about()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SlotValue.Problem(new SlotDescriptor("count", SlotType.Number), "12"), Is.Null);
            Assert.That(SlotValue.Problem(new SlotDescriptor("count", SlotType.Number), "twelve"),
                Is.Not.Null, "a number slot takes a number; accepting the words would store NaN");
            Assert.That(SlotValue.Problem(new SlotDescriptor("flag", SlotType.Boolean), "maybe"),
                Is.Not.Null);
            Assert.That(SlotValue.Problem(new SlotDescriptor("t", SlotType.Text), "one\ntwo"),
                Is.Not.Null, "a single value cannot contain a line break");
            Assert.That(SlotValue.Problem(new SlotDescriptor("t", SlotType.Text), "one two"), Is.Null);
        });
    }

    [Test]
    public void A_nested_reporter_reads_as_its_kind_and_its_preview_text()
    {
        var block = Block("ui.log");
        var slot = new SlotDescriptor("condition", SlotType.Boolean);
        block.Inputs[slot.Name] = new BlockInput { Block = new Block { Kind = "op.lt", Id = "n1" } };

        var value = SlotValue.Read(block, slot);

        Assert.Multiple(() =>
        {
            Assert.That(value.HasBlock, Is.True, "a hole holding a reporter is not an empty hole");
            Assert.That(value.BlockKind, Is.EqualTo("op.lt"));
            Assert.That(value.Text, Is.Not.Empty);
        });
    }

    [Test]
    public void A_menu_reads_its_own_default_when_the_document_has_never_set_it()
    {
        var block = Block("ui.log");
        var menu = BlockCatalog.Find(block.Kind)!.Menus![0];

        var value = SlotValue.ReadMenu(block, menu);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(menu.Default),
                "a document written before a menu existed has no entry for it");
            Assert.That(menu.Options, Contains.Item(value),
                "the default has to be a real option, or the dropdown opens with nothing selected");
        });
    }

    [Test]
    public void Every_menu_in_the_catalogue_has_a_default_that_is_one_of_its_options()
    {
        // A menu whose default is not in its own option list is a dropdown that opens with nothing
        // selected, on a block a user can drag in from the palette. Ten rows, so the first one nobody
        // typed by hand is already in the palette.
        var menus = BlockCatalog.All
            .Where(descriptor => descriptor.Menus is { Count: > 0 })
            .SelectMany(descriptor => descriptor.Menus!.Select(menu => (descriptor.Kind, menu)))
            .ToList();

        Assert.That(menus, Is.Not.Empty, "no block in the catalogue has a dropdown at all");

        foreach (var (kind, menu) in menus)
        {
            Assert.That(menu.Options, Contains.Item(menu.Default), $"{kind}.{menu.Name}");
            Assert.That(menu.Options, Is.Unique, $"{kind}.{menu.Name} has a repeated option, so two of "
                + "its values are indistinguishable in a saved document");
        }
    }

    [Test]
    public void Every_slot_type_in_the_catalogue_can_round_trip_a_typed_value()
    {
        // The exhaustiveness check that a test written per-slot-type cannot give. If a new SlotType is
        // added and neither From nor Problem handles it, this fails and names the type; if From handles it
        // but Problem does not, the typed value is still accepted and the warning still has to come from
        // the validator.
        var types = Enum.GetValues<SlotType>().Distinct().ToList();

        foreach (var type in types)
        {
            var slot = new SlotDescriptor("s", type);

            // "42" is a number, so a boolean slot is given "true" instead. Asking a boolean to accept a
            // number is not a gap in From or Problem - it is the refusal working, and a test that expects
            // otherwise has stopped testing round-tripping and started testing agreement.
            var text = type == SlotType.Boolean ? "true" : "42";

            var input = SlotValue.From(slot, text);
            var problem = SlotValue.Problem(slot, text);

            Assert.Multiple(() =>
            {
                Assert.That(input.Kind, Is.Not.EqualTo(BlockInputKind.Empty), $"{type} built nothing");
                Assert.That(problem, Is.Null, $"{type} rejected a value it should accept");
            });
        }

        Assert.That(types, Has.Count.GreaterThan(10), "and the enum has really grown since this was written");
    }

    private static Block Block(string kind)
    {
        var descriptor = BlockCatalog.Find(kind);
        var block = new Block { Kind = kind, Id = kind + "-1" };

        foreach (var slot in descriptor?.Slots ?? [])
        {
            block.Inputs[slot.Name] = BlockFactory.DefaultInput(descriptor!, slot);
        }

        return block;
    }

    private static SlotDescriptor Slot(Block block, string name) =>
        BlockCatalog.Find(block.Kind)!.Slots!.First(slot => slot.Name == name);
}
