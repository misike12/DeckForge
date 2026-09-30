using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The label split, and the fresh blocks built from a catalog row.
/// </summary>
/// <remarks>
/// <para>
/// The label is written once, as a template with <c>{slot}</c> holes, and read from two places: the
/// canvas draws a control in each hole, and search matches the whole string. A hole naming something the
/// row does not declare shows up on the block as the literal text <c>{count}</c> — so the tests here are
/// mostly "no row in the catalog does that", which is the cheapest possible guard against a whole class
/// of visible-but-subtle rendering defect.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockLabelTests
{
    [Test]
    public void Every_shipping_row_splits_into_runs_with_no_hole_left_dangling()
    {
        var dangling = new List<string>();

        foreach (var descriptor in BlockCatalog.Blocks)
        {
            foreach (var run in BlockLabel.Plan(descriptor))
            {
                if (run.Kind == BlockLabelRunKind.Text && run.Text.Contains('{'))
                {
                    dangling.Add($"{descriptor.Kind}: {run.Text}");
                }
            }
        }

        Assert.That(
            dangling,
            Is.Empty,
            "These labels name a slot or menu the row does not declare, so the block draws the raw "
            + "marker:" + Environment.NewLine + string.Join(Environment.NewLine, dangling));
    }

    [Test]
    public void Every_shipping_row_produces_at_least_one_run()
    {
        var empty = BlockCatalog.Blocks
            .Where(descriptor => BlockLabel.Plan(descriptor).Count == 0)
            .Select(descriptor => descriptor.Kind)
            .ToList();

        Assert.That(empty, Is.Empty, "These blocks would draw a tile with nothing on it: " + string.Join(", ", empty));
    }

    [Test]
    public void Words_between_two_holes_are_dropped_because_the_holes_supply_the_space()
    {
        // "join {a} and {b}" — the space around each hole belongs to the control that fills it, so the
        // canvas must not draw the words' whitespace as well or every slot comes out double-spaced.
        var descriptor = new BlockDescriptor(
            "test.join",
            BlockCategory.Operators,
            "join {a} {b}",
            "Joins two values.",
            BlockShape.Reporter,
            new SdkMapping("string.Empty", "plain C#"),
            [new SlotDescriptor("a", SlotType.Any), new SlotDescriptor("b", SlotType.Any)]);

        var runs = BlockLabel.Plan(descriptor);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(run => run.Kind).ToList(),
                Is.EqualTo(new[] { BlockLabelRunKind.Text, BlockLabelRunKind.Slot, BlockLabelRunKind.Slot }));
            Assert.That(runs[0].Text, Is.EqualTo("join"),
                "the words before the first hole are still words, and are drawn");
            Assert.That(runs.Skip(1).Select(run => run.Text).ToList(), Has.All.Empty,
                "the space between two holes is dropped: the controls supply their own padding, and "
                + "keeping it double-spaces every slot");
        });
    }

    [Test]
    public void A_label_that_starts_with_a_hole_keeps_the_words_that_follow_it()
    {
        // "{var}" is the whole label of var.get, and "define {name} returning a value" is half a hole.
        // Both must survive the split, or the block renders as an empty oval or loses its tail.
        var get = BlockCatalog.Find("var.get");
        var define = BlockCatalog.Find("proc.define-returning");

        Assert.Multiple(() =>
        {
            Assert.That(BlockLabel.PreviewText(get!), Is.EqualTo("var"));
            Assert.That(BlockLabel.PreviewText(define!), Is.EqualTo("define myProcedure returning a value"));
        });
    }

    [Test]
    public void A_menu_hole_shows_the_default_option()
    {
        var descriptor = BlockCatalog.Find("control.finish-failed");

        Assert.That(
            BlockLabel.PreviewText(descriptor!),
            Is.EqualTo("finish: failed ProviderError text"),
            "a hole with no slot is a menu, and it shows the option the block starts on");
    }

    [Test]
    public void A_placeholder_differs_by_type_so_an_empty_slot_is_readable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BlockLabel.Placeholder(new SlotDescriptor("n", SlotType.Number)), Is.EqualTo("0"));
            Assert.That(BlockLabel.Placeholder(new SlotDescriptor("c", SlotType.Boolean)), Is.EqualTo("<>"));
            Assert.That(BlockLabel.Placeholder(new SlotDescriptor("l", SlotType.List)), Is.EqualTo("list"));
            Assert.That(BlockLabel.Placeholder(new SlotDescriptor("t", SlotType.Text)), Is.EqualTo("text"));
        });
    }

    [Test]
    public void A_malformed_label_does_not_throw_while_the_palette_is_being_built()
    {
        var descriptor = new BlockDescriptor(
            "test.broken",
            BlockCategory.Control,
            "wait {seconds",
            "A label with no closing brace.",
            BlockShape.Stack,
            new SdkMapping("string.Empty", "plain C#"));

        var runs = BlockLabel.Plan(descriptor);

        Assert.That(runs, Is.Not.Empty, "the words before the hole are still worth drawing");
    }

    // ---- fresh blocks --------------------------------------------------------------------------------

    [Test]
    public void Every_shipping_row_can_be_built_with_every_slot_filled()
    {
        // The palette draws one of these per row. A slot left empty renders as a blank oval, so the
        // block looks like a label with a hole and no meaning — which is how a whole category of
        // thumbnails becomes unreadable without anything looking broken.
        var unfilled = new List<string>();
        var counter = 0;

        foreach (var descriptor in BlockCatalog.Blocks)
        {
            var block = BlockFactory.Create(descriptor, () => "b" + counter++);

            foreach (var slot in descriptor.Slots ?? [])
            {
                if (!block.Inputs.TryGetValue(slot.Name, out var input) || input.IsEmpty)
                {
                    unfilled.Add($"{descriptor.Kind}.{slot.Name}");
                }
            }

            foreach (var menu in descriptor.Menus ?? [])
            {
                if (block.Fields[menu.Name] != menu.Default)
                {
                    unfilled.Add($"{descriptor.Kind}.{menu.Name} (menu)");
                }
            }
        }

        Assert.That(
            unfilled,
            Is.Empty,
            "These rows build a block with a hole in it:" + Environment.NewLine + string.Join(Environment.NewLine, unfilled));
    }

    [Test]
    public void A_row_default_wins_over_the_type_default()
    {
        // "wait 1 seconds" is what the row means; a fresh block reading "wait 0 seconds" would be a
        // correct implementation of an unhelpful default.
        var wait = BlockCatalog.Find("control.wait-seconds");

        Assert.That(BlockFactory.Create(wait!, () => "b1").InputNumber("seconds"), Is.EqualTo(1));
    }

    [Test]
    public void A_variable_slot_starts_as_a_reference_not_a_value()
    {
        // A named source is something the document declares. Filling it with text would make the block
        // look filled in while emitting a reference to a variable called "text".
        var set = BlockCatalog.Find("var.set");

        Assert.Multiple(() =>
        {
            Assert.That(BlockFactory.Create(set!, () => "b1").InputText("var"), Is.EqualTo("var"));
            Assert.That(BlockFactory.Create(set!, () => "b2").InputBlock("var"), Is.Null);
        });
    }

    [Test]
    public void Two_rows_about_the_same_variable_start_on_the_same_name()
    {
        var set = BlockFactory.Create(BlockCatalog.Find("var.set")!, () => "b1");
        var get = BlockFactory.Create(BlockCatalog.Find("var.get")!, () => "b2");

        Assert.That(
            get.InputText("var"),
            Is.EqualTo(set.InputText("var")),
            "var.set and var.get must refer to one variable, or the sample declares two");
    }

    [Test]
    public void A_preview_never_uses_a_document_block_id()
    {
        var preview = BlockFactory.Preview(BlockCatalog.Find("control.repeat")!);

        Assert.That(preview.Id, Does.Not.StartWith("b"), "the palette and the document share one id space");
    }
}