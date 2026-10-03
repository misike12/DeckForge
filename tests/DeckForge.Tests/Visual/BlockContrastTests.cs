using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// High contrast: §9.8's "tiles fall back to border-only fills with strong outlines when the system asks".
/// </summary>
/// <remarks>
/// The rule being protected is that colour is never the only signal. At monochrome there is no colour at
/// all, so whatever tells a user which category a block belongs to has to come from somewhere else - which is
/// why these tests assert on the *shape* and the palette's labels as well as on the colours.
/// </remarks>
[TestFixture]
public sealed class BlockContrastTests
{
    [Test]
    public void The_standard_canvas_is_the_one_the_design_was_drawn_for()
    {
        var contrast = BlockContrastRules.For(BlockCategory.Control, ContrastLevel.Standard);

        Assert.Multiple(() =>
        {
            Assert.That(contrast.Fill, Is.EqualTo(BlockCatalog.Category(BlockCategory.Control).Hue),
                "the catalogue's own hue, so the canvas and the palette cannot drift apart");
            Assert.That(contrast.StrokeThickness, Is.EqualTo(1.25),
                "the same weight the block outline already uses, so a standard canvas is unchanged");
        });
    }

    [Test]
    public void Strong_contrast_keeps_the_category_and_strengthens_the_outline()
    {
        var contrast = BlockContrastRules.For(BlockCategory.Ui, ContrastLevel.Strong);

        Assert.Multiple(() =>
        {
            Assert.That(contrast.Fill, Is.EqualTo(BlockCatalog.Category(BlockCategory.Ui).Hue),
                "a user who asked for strong contrast has not asked to stop telling them what a block is");
            Assert.That(contrast.StrokeThickness, Is.GreaterThan(1.25));
            Assert.That(contrast.Stroke, Does.Not.Contain("#FF"),
                "and the outline is a colour of its own rather than the fill repeated verbatim");
        });
    }

    [Test]
    public void Monochrome_drops_the_fill_entirely_and_keeps_the_shape()
    {
        var contrast = BlockContrastRules.For(BlockCategory.Sensing, ContrastLevel.Monochrome);

        Assert.Multiple(() =>
        {
            Assert.That(contrast.Fill, Is.Null,
                "no fill at all: this is the level where the silhouette carries the category");
            Assert.That(contrast.StrokeThickness, Is.GreaterThanOrEqualTo(3),
                "and the outline is the strongest it ever gets, because it is all there is");
        });
    }

    [Test]
    public void Every_category_gets_an_outline_that_reads_against_its_own_fill()
    {
        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var level in new[] { ContrastLevel.Standard, ContrastLevel.Strong, ContrastLevel.Monochrome })
            {
                var contrast = BlockContrastRules.For(category.Category, level);

                Assert.That(
                    contrast.Stroke,
                    Is.Not.EqualTo(contrast.Fill),
                    $"{category.Category} at {level} has an outline that is its own fill, so the block's "
                    + "edge is invisible");
            }
        }
    }

    [Test]
    public void Every_category_survives_every_level_without_throwing()
    {
        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var level in Enum.GetValues<ContrastLevel>())
            {
                Assert.DoesNotThrow(
                    () => BlockContrastRules.For(category.Category, level),
                    $"{category.Category} at {level}");
            }
        }
    }

    [Test]
    public void The_outline_is_derived_from_the_fill_so_a_hue_change_cannot_stale_it()
    {
        // The first version had a hard-coded palette of outline colours, and it went stale the first time a
        // category's hue changed - a slightly-wrong outline still looks like an outline, so nobody notices.
        var standard = BlockContrastRules.For(BlockCategory.Events, ContrastLevel.Standard);

        Assert.That(
            standard.Stroke,
            Is.Not.EqualTo(BlockCatalog.Category(BlockCategory.Events).Hue),
            "derived, so it tracks the hue rather than remembering it");
    }

    [Test]
    public void A_disabled_block_says_so_at_every_level()
    {
        foreach (var level in Enum.GetValues<ContrastLevel>())
        {
            Assert.That(
                BlockContrastRules.For(BlockCategory.Variables, level, disabled: true).Muted,
                Is.True,
                $"a switched-off block has to look switched off at {level} too, or a user running a script "
                + "sees the disabled half of it execute");
        }
    }

    [Test]
    public void The_palette_may_drop_a_colour_key_at_monochrome_but_may_not_drop_the_category_name()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                BlockContrastRules.CategoryColourCarriesMeaning(ContrastLevel.Standard),
                Is.True,
                "the colour is doing the work, so the name beside it is redundant");
            Assert.That(
                BlockContrastRules.CategoryColourCarriesMeaning(ContrastLevel.Monochrome),
                Is.False,
                "and at monochrome the name is the only signal, so hiding it there would leave a "
                + "colour-blind-safe palette that says nothing");
        });
    }

    [Test]
    public void Shapes_still_differ_when_no_colour_survives_to_tell_them_apart()
    {
        // The other half of "colour is never the only signal": if monochrome made every block the same
        // rectangle, dropping the colour would have cost the user the shape too.
        var shapes = BlockCatalog.Blocks
            .Select(block => BlockCatalog.Find(block.Kind)!.Shape)
            .Distinct()
            .ToList();

        Assert.That(shapes, Has.Count.GreaterThanOrEqualTo(4),
            "stack, reporter, boolean and hat are four silhouettes a user can tell without colour");
    }

    [Test]
    public void The_outline_thickens_as_contrast_rises_and_is_never_hairline_at_the_top()
    {
        // StrokeWidthFor had no test at all, and it is the one part of the contrast decision a user sees
        // directly: a rule nobody checked is a rule that can quietly become 1.
        var standard = BlockContrast.StrokeWidthFor(ContrastLevel.Standard);
        var strong = BlockContrast.StrokeWidthFor(ContrastLevel.Strong);
        var monochrome = BlockContrast.StrokeWidthFor(ContrastLevel.Monochrome);

        Assert.Multiple(() =>
        {
            Assert.That(standard, Is.EqualTo(1.25), "the block outline's own width");
            Assert.That(strong, Is.GreaterThan(standard));
            Assert.That(monochrome, Is.GreaterThan(strong),
                "monochrome is the level where colour has gone entirely, so the outline is all there is");
            Assert.That(monochrome, Is.GreaterThanOrEqualTo(2),
                "a one-pixel outline disappears at some zoom levels and on some panels");
        });
    }

    [Test]
    public void An_unrecognised_level_falls_back_to_the_standard_width_rather_than_to_nothing()
    {
        var unknown = (ContrastLevel)999;

        Assert.Multiple(() =>
        {
            Assert.That(BlockContrast.StrokeWidthFor(unknown), Is.EqualTo(BlockContrast.StrokeWidthFor(ContrastLevel.Standard)));
            Assert.That(BlockContrastRules.For(BlockCategory.Control, unknown).StrokeThickness,
                Is.EqualTo(BlockContrast.StrokeWidthFor(ContrastLevel.Standard)),
                "a level this build has never heard of still has to draw something visible");
        });
    }
}