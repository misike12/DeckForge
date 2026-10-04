using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// §28.2's "label contrast computed at 4.5:1 in a test", computed.
/// </summary>
/// <remarks>
/// <para>
/// The criterion said 4.5:1 and the old suite could only assert that every category kept an outline and
/// that the silhouettes differed - shapes, not numbers. "The number is a goal and not a result" was the
/// honest description of that, and it stayed that way for as long as the colours lived in the App's
/// theme where no test could read them.
/// </para>
/// <para>
/// These tests therefore measure <see cref="BlockThemePalette"/>, which is what <c>LiquidTheme</c> now
/// converts into brushes. Every category, both themes, and both ends of the fill gradient: the lower
/// stop is the one a label sits on in the bottom half of a tile, so a design that passes at the top and
/// fails at the bottom has not passed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BlockThemePaletteTests
{
    [Test]
    [Description("28.2 - the label on a block, against its own fill, at 4.5:1 in every category and theme.")]
    public void LabelContrastMeetsTheTextMinimumEverywhere()
    {
        var failures = new List<string>();

        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var dark in (bool[])[true, false])
            {
                var tokens = BlockThemePalette.For(category.Category, dark);
                var theme = dark ? "dark" : "light";

                foreach (var (stop, fill) in new[] { ("top", tokens.FillTop), ("bottom", tokens.FillBottom) })
                {
                    var ratio = ContrastRatio.Between(tokens.Ink, fill);
                    if (ratio < ContrastRatio.TextMinimum)
                    {
                        failures.Add(
                            $"{category.Category} {theme} {stop}: ink {tokens.Ink} on fill {fill} is {ratio:0.00}:1");
                    }
                }
            }
        }

        Assert.That(
            failures,
            Is.Empty,
            "a label has to read against its own block, and these do not:\n  " + string.Join("\n  ", failures));
    }

    [Test]
    [Description("28.2 - the outline is a boundary rather than text, so WCAG asks 3:1 of it.")]
    public void OutlineMeetsTheNonTextMinimumAgainstItsOwnFill()
    {
        var failures = new List<string>();

        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var dark in (bool[])[true, false])
            {
                var tokens = BlockThemePalette.For(category.Category, dark);

                // Against the *lighter* end of the fill, which is the harder direction: a dark outline on
                // a light fill and a light outline on a dark fill are both worst at the near end.
                var lighter = ContrastRatio.Luminance(tokens.FillTop) > ContrastRatio.Luminance(tokens.FillBottom)
                    ? tokens.FillTop
                    : tokens.FillBottom;

                var ratio = ContrastRatio.Between(tokens.Stroke, lighter);
                if (ratio < ContrastRatio.NonTextMinimum)
                {
                    failures.Add(
                        $"{category.Category} {(dark ? "dark" : "light")}: " +
                        $"stroke {tokens.Stroke} on fill {lighter} is {ratio:0.00}:1");
                }
            }
        }

        Assert.That(failures, Is.Empty, "block outlines disappear at some zoom levels:\n  " + string.Join("\n  ", failures));
    }

    [Test]
    [Description("The ink has to be on the opposite side of the fill from it, or no amount of tuning the fill helps.")]
    public void InkSitsOnTheFarSideOfTheFill()
    {
        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var dark in (bool[])[true, false])
            {
                var tokens = BlockThemePalette.For(category.Category, dark);
                var theme = dark ? "dark" : "light";

                foreach (var (stop, fill) in new[] { ("top", tokens.FillTop), ("bottom", tokens.FillBottom) })
                {
                    var inkIsBrighter = ContrastRatio.Luminance(tokens.Ink) > ContrastRatio.Luminance(fill);
                    Assert.That(
                        inkIsBrighter,
                        Is.EqualTo(dark),
                        $"{category.Category} {theme} {stop}: dark theme ink must be lighter than the fill "
                        + $"({tokens.Ink} on {fill}), and light theme ink darker");
                }
            }
        }
    }

    [Test]
    [Description("Both ends of the gradient have to differ, or it is a flat fill wearing a gradient brush.")]
    public void TheTwoGradientStopsDiffer()
    {
        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var dark in (bool[])[true, false])
            {
                var tokens = BlockThemePalette.For(category.Category, dark);
                Assert.That(
                    tokens.FillTop,
                    Is.Not.EqualTo(tokens.FillBottom),
                    $"{category.Category} {(dark ? "dark" : "light")} has one flat fill at both stops");
            }
        }
    }

    [Test]
    [Description("A known pair, so the ratio itself is checked and not only its callers.")]
    public void TheRatioIsTheWcagOne()
    {
        Assert.Multiple(() =>
        {
            // The two anchors every contrast checker is calibrated against.
            Assert.That(ContrastRatio.Between("#FFFFFF", "#000000"), Is.EqualTo(21).Within(0.01));
            Assert.That(ContrastRatio.Between("#FFFFFF", "#FFFFFF"), Is.EqualTo(1).Within(0.01));

            // Black on white and white on black are the same pair either way round, which is what makes
            // the ratio usable as a symmetric measure.
            Assert.That(
                ContrastRatio.Between("#000000", "#FFFFFF"),
                Is.EqualTo(ContrastRatio.Between("#FFFFFF", "#000000")).Within(0.0001));

            // #767676 on white is the canonical "just passes AA" grey, at about 4.54:1. If this drifts,
            // every threshold above is measuring something other than WCAG.
            Assert.That(ContrastRatio.Between("#767676", "#FFFFFF"), Is.InRange(4.5, 4.6));

            // The short form is accepted, since a category hue is allowed to be written either way.
            Assert.That(ContrastRatio.Between("#FFF", "#000"), Is.EqualTo(21).Within(0.01));
        });
    }

    [Test]
    [Description("An unparseable colour has to fail the threshold, not pass it.")]
    public void AnUnreadableColourFailsRatherThanPasses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContrastRatio.Between("not-a-colour", "#FFFFFF"), Is.EqualTo(0));
            Assert.That(ContrastRatio.Between("#FFFFFF", null!), Is.EqualTo(0));
            Assert.That(ContrastRatio.Between("#12345", "#FFFFFF"), Is.EqualTo(0));
            Assert.That(ContrastRatio.Luminance(""), Is.EqualTo(0));
        });
    }

    [Test]
    [Description("Using encoded sRGB as if it were linear is the classic way to report a comfortable number for a hard colour.")]
    public void LuminanceIsComputedOnLinearisedChannels()
    {
        // #808080 has a luminance of about 0.216 by the linear rule and exactly 0.502 by the naive one.
        // That gap is the whole reason the checker exists, so it is asserted rather than assumed.
        Assert.That(ContrastRatio.Luminance("#808080"), Is.EqualTo(0.2159).Within(0.001));
        Assert.That(ContrastRatio.Luminance("#FFFFFF"), Is.EqualTo(1).Within(0.0001));
        Assert.That(ContrastRatio.Luminance("#000000"), Is.EqualTo(0).Within(0.0001));
    }

    [Test]
    [Description("The App builds its brushes from these strings, so every one of them has to be a colour it can read.")]
    public void EveryTokenIsAColourTheThemeCanParse()
    {
        foreach (var category in BlockCatalog.Categories)
        {
            foreach (var dark in (bool[])[true, false])
            {
                var tokens = BlockThemePalette.For(category.Category, dark);
                var theme = dark ? "dark" : "light";

                foreach (var (name, value) in new[]
                {
                    ("FillTop", tokens.FillTop),
                    ("FillBottom", tokens.FillBottom),
                    ("Ink", tokens.Ink),
                    ("Stroke", tokens.Stroke),
                })
                {
                    Assert.That(
                        value,
                        Does.Match("^#[0-9A-F]{6}$"),
                        $"{category.Category} {theme} {name} is \"{value}\", which ColorConverter would refuse");
                }
            }
        }
    }
}
