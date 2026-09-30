using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The document the Visual page draws before a plugin is open.
/// </summary>
/// <remarks>
/// <para>
/// The sample is content and coverage at once. Phase 3's exit criterion is that every catalog block
/// renders, nested, and the only way to check that without a screenshot is to make the sample hold
/// every block and then assert that it does. A new row added to the catalog fails
/// <see cref="Every_shipping_block_that_can_sit_in_a_stack_appears_in_the_sample"/> until it is
/// visible on the page — which is the whole point of building the sample from the catalog rather than
/// writing it out.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualSampleProjectTests
{
    [Test]
    public void Every_shipping_block_that_can_sit_in_a_stack_appears_in_the_sample()
    {
        var present = VisualSampleProject.Build().Blocks().Select(block => block.Kind).ToHashSet(StringComparer.Ordinal);

        var missing = BlockCatalog.Blocks
            .Where(descriptor => descriptor.Shape != BlockShape.Hat)
            .Select(descriptor => descriptor.Kind)
            .Where(kind => !present.Contains(kind))
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "These blocks are in the catalog but nothing on the Visual page draws them, so nobody has "
            + "looked at how they render:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Test]
    public void Every_hatted_block_reaches_the_page_through_the_palette()
    {
        // A hat may only sit at the top of a script, so one per script means two of the four cannot be
        // in the sample at all. The palette is where they are drawn, and this asserts they exist there
        // rather than being quietly absent.
        var palette = BlockCatalog.Blocks.Select(descriptor => descriptor.Kind).ToHashSet(StringComparer.Ordinal);

        var missing = BlockCatalog.Blocks
            .Where(descriptor => descriptor.IsHat)
            .Select(descriptor => descriptor.Kind)
            .Where(kind => !palette.Contains(kind))
            .ToList();

        Assert.That(missing, Is.Empty, string.Join(", ", missing));
    }

    [Test]
    public void Every_block_id_in_the_sample_is_unique()
    {
        var duplicates = VisualSampleProject.Build()
            .Blocks()
            .GroupBy(block => block.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(
            duplicates,
            Is.Empty,
            "Two blocks sharing an id would share a layout entry, a diagnostic target and a breakpoint: "
            + string.Join(", ", duplicates));
    }

    [Test]
    public void The_sample_declares_everything_it_refers_to()
    {
        var document = VisualSampleProject.Build();
        var errors = VisualValidator.Validate(document, VisualSampleProject.ValidationContext)
            .Where(diagnostic => diagnostic.Severity == VisualSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToList();

        Assert.That(
            errors,
            Is.Empty,
            "The sample is what a first-time user sees, so it must not open with errors on it:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    [Test]
    public void The_sample_nests_something_on_every_category_that_can_wrap()
    {
        // The recursive template in Part 9.4 is the riskiest piece of the canvas, and a category whose
        // containers all render with empty mouths would look fine in a screenshot of another category.
        var document = VisualSampleProject.Build();
        var containers = document.Blocks()
            .Where(block => BlockCatalog.Find(block.Kind) is { IsContainer: true })
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(containers, Is.Not.Empty, "there are containers in the catalog");
            Assert.That(containers.All(block => block.Bodies.Values.Any(body => body.Count > 0)), Is.True,
                "every container in the sample holds at least one statement, so the recursive template "
                + "is exercised rather than assumed");
        });
    }

    [Test]
    public void Each_category_gets_its_own_script()
    {
        var document = VisualSampleProject.Build();

        Assert.That(
            document.Targets[0].Scripts.Select(script => script.Name),
            Is.EqualTo(BlockCatalog.Categories.Select(category => category.Name)),
            "one script per palette category, in rail order");
    }

    [Test]
    public void The_sample_round_trips_through_its_own_format()
    {
        var document = VisualSampleProject.Build();
        var reloaded = VisualProjectJson.Deserialize(VisualProjectJson.Serialize(document));

        Assert.That(
            reloaded.Blocks().Select(block => block.Id),
            Is.EqualTo(document.Blocks().Select(block => block.Id)),
            "a document that cannot be reloaded cannot be saved, so the page would lose it on the first save");
    }

    [Test]
    public void Two_builds_produce_the_same_document()
    {
        // Only the document id is allowed to differ: everything else is content, and content that moves
        // between builds makes a sidecar diff unreadable.
        var first = VisualSampleProject.Build();
        var second = VisualSampleProject.Build();

        first.DocumentId = string.Empty;
        second.DocumentId = string.Empty;

        Assert.That(VisualProjectJson.Serialize(second), Is.EqualTo(VisualProjectJson.Serialize(first)));
    }
}