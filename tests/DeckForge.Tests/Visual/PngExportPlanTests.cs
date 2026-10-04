using System.Globalization;
using System.Xml.Linq;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Part 25.3's PNG render: a raster of the canvas, drawn from the layout model and not from the window.
/// </summary>
/// <remarks>
/// <para>
/// §28.5 records this feature as "not built, by decision", because "a third-party encoder in Core would make
/// the export untestable". The split that answers that objection is the subject of this fixture: everything
/// a test can be asked about — the bounds, the scale, the padding, the caption, the colours, the order, and
/// the determinism — is in <see cref="PngExportPlan"/> in Core, and the WPF half is a rasteriser with no
/// decisions left in it. So these tests do not need a window, and neither would a test of the pixels.
/// </para>
/// <para>
/// The two properties worth the most attention are the ones that a screenshot could not have. "It works for a
/// script that is scrolled off screen" is a property of building from workspace units with no view in the
/// arithmetic, and "it is deterministic, so a test can compare hashes" is a property of the draw list being
/// ordered by position rather than by whatever order a dictionary yielded. Both are asserted here, and the
/// third is asserted against the vector export, which walks the same model: if the two renders ever stop
/// agreeing about where a block is, one of these tests says so.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PngExportPlanTests
{
    private static readonly CultureInfo PriorCulture = CultureInfo.CurrentCulture;

    [OneTimeSetUp]
    public void ForceInvariant() => Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

    // Restored, because the suite is sequential and the next fixture on this thread inherited the invariant
    // culture for the rest of the run - which is how a test somewhere else ends up asserting formatting that
    // only holds in one culture and nobody can say why.
    [OneTimeTearDown]
    public void RestoreCulture() => Thread.CurrentThread.CurrentCulture = PriorCulture;

    private static Block Log(string text, string id) => new()
    {
        Kind = "ui.log",
        Id = id,
        Inputs = { ["template"] = BlockInput.Of(text) },
    };

    private static Block Repeat(string id, params Block[] body) => new()
    {
        Kind = "control.repeat",
        Id = id,
        Inputs = { ["count"] = BlockInput.Of(3) },
        Bodies = { ["body"] = [.. body] },
    };

    private static VisualScript Script(string id, string name, params Block[] body)
    {
        var script = new VisualScript
        {
            Id = id,
            Name = name,
            Hat = new Block { Id = $"hat-{id}", Kind = "hat.action-runs" },
        };

        script.Body.AddRange(body);

        return script;
    }

    private static VisualProject Project(string targetName, params VisualScript[] scripts)
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget { Id = "t1", Name = targetName, Kind = TargetKind.Action };
        project.Targets.Add(target);
        target.Scripts.AddRange(scripts);

        return project;
    }

    /// <summary>A stack long enough that no window has shown all of it at once.</summary>
    private static IReadOnlyList<Block> DeepRun(int count) =>
        [.. Enumerable.Range(0, count).Select(index => Log($"line {index}", $"b{index}"))];

    private static PngExportOptions Unscaled => new() { Scale = 1 };

    /// <summary>
    /// The rectangles and labels the vector export wrote, parsed rather than searched for.
    /// </summary>
    /// <param name="svg">The document <see cref="SvgRenderer"/> produced.</param>
    private static (List<(double X, double Y)> Rects, List<(double X, double Y, string Text)> Texts) Drawn(
        string svg)
    {
        var document = XDocument.Parse(svg);

        var rects = document.Descendants()
            .Where(element => element.Name.LocalName == "rect")
            .Select(element => (
                X: Number(element.Attribute("x")!.Value),
                Y: Number(element.Attribute("y")!.Value)))
            .ToList();

        var texts = document.Descendants()
            .Where(element => element.Name.LocalName == "text")
            .Select(element => (
                X: Number(element.Attribute("x")!.Value),
                Y: Number(element.Attribute("y")!.Value),
                Text: element.Value))
            .ToList();

        return (rects, texts);
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    [Test]
    [Description("Part 25.3: 'it works for a script that is scrolled off screen'. Nothing in the plan reads the view.")]
    public void EveryBlockOfARunFarBelowTheOriginIsStillDrawnAndStillFits()
    {
        var run = DeepRun(40);
        var rects = StackLayout.LayoutRun(run);
        var (_, extentHeight) = CanvasView.ExtentOf(rects.Values);

        // Off screen by construction: the canvas is panned five thousand units down and a quarter of the way
        // out, so the whole run is above the viewport rather than in it. A screenshot would have exported the
        // window's background and nothing else.
        var view = new CanvasView { Zoom = CanvasView.MinZoom, PanX = 0, PanY = 5000 };
        var plan = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 1, View = view });

        var bottom = plan.Boxes.Max(box => box.Y + box.Height);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Boxes.Count(), Is.EqualTo(run.Count),
                "one rectangle per block, which is the whole difference between a render and a capture");

            Assert.That(
                plan.Boxes.Select(box => box.BlockId),
                Is.EquivalentTo(run.Select(block => block.Id)),
                "every block, including the ones nobody can see");

            Assert.That(plan.Height, Is.EqualTo((int)Math.Ceiling(extentHeight)),
                "the image is as tall as the run rather than as tall as the viewport");

            Assert.That(bottom, Is.LessThanOrEqualTo(plan.Height),
                "and the last block is inside it, which a cropped screenshot could not promise");
        });
    }

    [Test]
    [Description("The view is accepted and ignored on purpose, so scrolling and zooming must change nothing.")]
    public void ThePlanIsTheSameWhereverTheCanvasIsScrolledTo()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a"), Log("bright", "b")));

        var here = PngExportPlan.ForDocument(project, Unscaled);
        var elsewhere = PngExportPlan.ForDocument(project, new PngExportOptions
        {
            Scale = 1,
            View = new CanvasView { Zoom = 2.5, PanX = -9000, PanY = 1200 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(elsewhere.Fingerprint, Is.EqualTo(here.Fingerprint),
                "a plan that changed with the pan would be a picture of the window, which is what this design "
                + "refused to build");

            Assert.That(elsewhere.Width, Is.EqualTo(here.Width));
            Assert.That(elsewhere.Height, Is.EqualTo(here.Height));
            Assert.That(elsewhere.Describe(), Is.EqualTo(here.Describe()));
        });
    }

    [Test]
    [Description("Padding is the caller's margin, and it lands on every edge of the reported size.")]
    public void PaddingAddsTwoEdgesToEverySideOfTheReportedSize()
    {
        var run = DeepRun(3);
        var (_, extent) = CanvasView.ExtentOf(StackLayout.LayoutRun(run).Values);

        var flush = PngExportPlan.ForRun(run, Unscaled);
        var padded = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 1, Padding = 20 });

        Assert.Multiple(() =>
        {
            Assert.That(flush.Width, Is.EqualTo((int)Math.Ceiling(flush.Boxes.Max(box => box.X + box.Width))));
            Assert.That(flush.Height, Is.EqualTo((int)Math.Ceiling(extent)));

            Assert.That(padded.Width - flush.Width, Is.EqualTo(40), "twenty on the left and twenty on the right");
            Assert.That(padded.Height - flush.Height, Is.EqualTo(40), "twenty above and twenty below");
        });
    }

    [Test]
    [Description("2x by default, and the geometry is scaled by exactly the same number as the size.")]
    public void TheScaleMultipliesTheSizeAndTheGeometryTogether()
    {
        var run = DeepRun(4);
        var extent = CanvasView.ExtentOf(StackLayout.LayoutRun(run).Values);

        var once = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 1 });
        var twice = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 2 });
        var fourTimes = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 4 });

        Assert.Multiple(() =>
        {
            // The run's own extent, scaled and rounded up, rather than the 1x plan doubled: rounding up is
            // not multiplicative, so 250.4 units is 251 pixels whole and 501 at 2x, not 502. The geometry is
            // exact; the size is the ceiling of the same number.
            Assert.That(twice.Width, Is.EqualTo((int)Math.Ceiling(extent.Width * 2)));
            Assert.That(twice.Height, Is.EqualTo((int)Math.Ceiling(extent.Height * 2)));
            Assert.That(fourTimes.Width, Is.EqualTo((int)Math.Ceiling(extent.Width * 4)));

            Assert.That(twice.Boxes.Select(box => box.X), Is.EqualTo(once.Boxes.Select(box => box.X * 2)),
                "a plan whose size doubled but whose blocks did not would export a picture with the top left of "
                + "the script in it");

            Assert.That(twice.Boxes.Select(box => box.Height), Is.EqualTo(once.Boxes.Select(box => box.Height * 2)),
                "and their heights with them, or the picture would be a stack of the right blocks at the wrong "
                + "proportions");

            Assert.That(
                twice.Texts.Select(text => text.FontSize),
                Is.EqualTo(once.Texts.Select(text => text.FontSize * 2)),
                "and the labels with them, or the text would be half the size of the block it sits in");

            Assert.That(fourTimes.Scale, Is.EqualTo(4));
            Assert.That(new PngExportOptions().ClampedScale, Is.EqualTo(2),
                "and 2x is the default, because a raster is usually pasted somewhere bigger than the editor");
        });
    }

    [Test]
    [Description("A rasteriser is given whole pixels, so a size with a fraction in it goes up.")]
    public void AProportionateScaleIsRoundedUpBecauseARasteriserCannotBeGivenAFraction()
    {
        var run = DeepRun(3);
        var plan = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 0.5 });
        var exact = CanvasView.ExtentOf(StackLayout.LayoutRun(run).Values);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Height, Is.EqualTo((int)Math.Ceiling(exact.Height * 0.5)));
            Assert.That(plan.Width, Is.GreaterThanOrEqualTo(Math.Ceiling(exact.Width * 0.5) - 1));
            Assert.That(plan.Height, Is.GreaterThanOrEqualTo(exact.Height * 0.5),
                "never down, because rounding down would clip the bottom row of pixels off the last block");
        });
    }

    [Test]
    [Description("A scale of zero or of a hundred is clamped, and the plan says which scale it used.")]
    public void AScaleOutsideWhatARasterCanBeAskedForIsClampedRatherThanObeyed()
    {
        var run = DeepRun(2);

        var none = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 0 });
        var absurd = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 100 });
        var negativePadding = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 1, Padding = -50 });

        Assert.Multiple(() =>
        {
            Assert.That(none.Scale, Is.EqualTo(PngExportOptions.MinScale));
            Assert.That(absurd.Scale, Is.EqualTo(PngExportOptions.MaxScale));

            Assert.That(none.Width, Is.GreaterThan(0), "a zero-scale image is not an image, and the plan is not "
                + "going to hand one to a rasteriser");

            Assert.That(
                negativePadding.Width,
                Is.EqualTo(PngExportPlan.ForRun(run, Unscaled).Width),
                "a negative padding is read as no padding, because the alternative is cropping a block in half");
        });
    }

    [Test]
    [Description("Part 25.3: 'a caption line naming the target', and the image grows to hold it.")]
    public void TheCaptionNamesTheScriptAndTheTargetAndTheImageGrowsToHoldIt()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a")));

        var captioned = PngExportPlan.ForDocument(project, Unscaled);
        var plain = PngExportPlan.ForDocument(project, new PngExportOptions { Scale = 1, Caption = string.Empty });

        var caption = captioned.Texts.Single(text => text.BlockId is null);

        Assert.Multiple(() =>
        {
            Assert.That(caption.Text, Is.EqualTo("Turn the lamp on in Living room"),
                "the script first, because that is what the reader is looking for, and the target as context");

            Assert.That(
                captioned.Height - plain.Height,
                Is.EqualTo((int)PngExportPlan.CaptionLineHeight),
                "a caption is a row above the blocks, and the size has to say so");

            Assert.That(plain.Texts.Any(text => text.BlockId is null), Is.False,
                "an empty caption is a request for no caption rather than for a blank line");
        });
    }

    [Test]
    [Description("A caller who knows better than the default says so, and the words are the words.")]
    public void AForcedCaptionReplacesTheOneNamingTheTarget()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a")));

        var plan = PngExportPlan.ForDocument(project, new PngExportOptions
        {
            Scale = 1,
            Caption = "deckforge/demo.lamp",
        });

        Assert.That(
            plan.Texts.Where(text => text.BlockId is null).Select(text => text.Text),
            Is.EqualTo(new[] { "deckforge/demo.lamp" }));
    }

    [Test]
    [Description("A transparent export has nothing behind a caption, so the plan draws one.")]
    public void ACaptionIsDrawnOnAPlateSoItReadsOnAnyBackground()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a")));

        var plan = PngExportPlan.ForDocument(project, new PngExportOptions { Scale = 1 });
        var caption = plan.Texts.Single(text => text.BlockId is null);
        var plate = plan.Boxes.Single(box => box.BlockId is null);
        var blocks = plan.Boxes.Where(box => box.BlockId is not null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(plate.X, Is.EqualTo(caption.X), "the caption sits on the plate rather than beside it");

            Assert.That(
                plate.X + plate.Width,
                Is.EqualTo(blocks.Max(box => box.X + box.Width)),
                "and the plate is as wide as the column, so a caption that is wider than one block still lands on "
                + "something");

            Assert.That(
                plate.Y + plate.Height,
                Is.LessThanOrEqualTo(blocks.Min(box => box.Y)),
                "and it stops where the blocks begin, rather than sitting behind them");

            Assert.That(plate.FillTop, Is.EqualTo(PngExportPlan.CaptionPlateDark));
            Assert.That(plate.Stroke, Is.Null, "a plate is a background, not a block");
        });
    }

    [Test]
    [Description("Part 25.3: 'it is deterministic, so a test can compare hashes'.")]
    public void TheSameDocumentProducesTheSameFingerprintEveryTime()
    {
        var first = PngExportPlan.ForDocument(Document(), new PngExportOptions());
        var second = PngExportPlan.ForDocument(Document(), new PngExportOptions());

        Assert.Multiple(() =>
        {
            Assert.That(second.Fingerprint, Is.EqualTo(first.Fingerprint));
            Assert.That(second.Describe(), Is.EqualTo(first.Describe()));
            Assert.That(second.Fingerprint, Has.Length.EqualTo(64), "a SHA-256, so the hash says what it hashed");
        });

        VisualProject Document() => Project(
            "Living room",
            Script("s1", "Turn the lamp on", Log("on", "a"), Repeat("r1", Log("again", "b"), Log("once more", "c"))),
            Script("s2", "Turn it off", Log("off", "d")));
    }

    [Test]
    [Description("Determinism means nothing about the drawing comes from an object's identity or a hash order.")]
    public void APlanSurvivesAJsonRoundTripOfTheDocument()
    {
        var written = VisualProjectJson.Serialize(Document());
        var reread = VisualProjectJson.Deserialize(written);

        var before = PngExportPlan.ForDocument(Document(), new PngExportOptions());
        var after = PngExportPlan.ForDocument(reread, new PngExportOptions());

        Assert.Multiple(() =>
        {
            Assert.That(after.Fingerprint, Is.EqualTo(before.Fingerprint),
                "a plan that moved after a save and reload would mean the export depended on something the file "
                + "does not hold");

            Assert.That(after.Items, Has.Count.EqualTo(before.Items.Count));
        });

        VisualProject Document() => Project(
            "Living room",
            Script("s1", "Turn the lamp on", Log("on", "a"), Repeat("r1", Log("again", "b"), Log("once more", "c"))));
    }

    [Test]
    [Description("A Hungarian machine writes 1,5, which is not a number in a hash.")]
    public void TheFingerprintDoesNotMoveWhenTheLocaleDoes()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("1.5", "a"), Log("on", "b")));

        var invariant = PngExportPlan.ForDocument(project, new PngExportOptions());
        var previous = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("hu-HU");

            var hungarian = PngExportPlan.ForDocument(project, new PngExportOptions());

            Assert.Multiple(() =>
            {
                Assert.That(hungarian.Fingerprint, Is.EqualTo(invariant.Fingerprint));
                Assert.That(hungarian.Describe(), Does.Not.Contain(","),
                    "no coordinate carries a comma, because the fingerprint is hashed from this text and a "
                    + "comma is not a digit to half the world");
            });
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    [Description("The draw order is fixed by the plan, not by the order a dictionary yielded.")]
    public void EveryBlockIsDrawnInAStableOrder()
    {
        var project = Project(
            "Living room",
            Script("s1", "Turn the lamp on", Repeat("r1", Log("a", "a"), Log("b", "b")), Log("after", "c")));

        var plan = PngExportPlan.ForDocument(project, new PngExportOptions());

        var tops = plan.Boxes.Where(box => box.BlockId is not null).Select(box => box.Y).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tops, Is.Ordered,
                "sorted by position, which is both stable across runs and the right order to draw in: a "
                + "container is always above the blocks inside it");

            Assert.That(plan.Boxes.Count(box => box.BlockId == "r1"), Is.EqualTo(1),
                "a container is one rectangle, not one per level of nesting");
        });
    }

    [Test]
    [Description("The strongest form of 'the two renders cannot disagree': the same rectangles, the same words.")]
    public void EveryRectangleTheVectorExportDrawsIsDrawnHereInTheSamePlace()
    {
        var project = Document();

        var margin = 16d;
        var plan = PngExportPlan.ForDocument(project, new PngExportOptions { Scale = 1, Padding = margin });
        var (rects, texts) = Drawn(SvgRenderer.RenderDocument(project));

        // The vector export's own column captions are its script names, so they come out before the labels.
        var names = project.Targets.SelectMany(target => target.Scripts).Select(script => script.Name).ToList();
        var labels = plan.Texts.Where(text => text.BlockId is not null).ToList();
        var drawnLabels = texts.Where(text => !names.Contains(text.Text)).ToList();
        var boxes = plan.Boxes.Where(box => box.BlockId is not null).ToList();

        Assert.Multiple(() =>
        {
            // First, and to say the comparisons below are not two empty lists agreeing: the vector export
            // drew four rectangles and four labels, and this plan drew four of each.
            Assert.That(rects.Count, Is.EqualTo(boxes.Count));
            Assert.That(drawnLabels.Count, Is.EqualTo(labels.Count));
            Assert.That(boxes.Count, Is.EqualTo(4));

            // At the same scale and the same 16 units of margin the two agree on x exactly: the vector export
            // offsets each column by its own margin and so does this plan, from the same layout and the same
            // gutter.
            Assert.That(boxes.Select(box => box.X), Is.EqualTo(rects.Select(rect => rect.X)));
            Assert.That(labels.Select(text => text.X), Is.EqualTo(drawnLabels.Select(text => text.X)));

            // Vertically they differ in exactly one place: the vector export spends its margin on the
            // caption row rather than putting a margin above it, so this plan's drawing sits one margin
            // lower and no further apart than the vector export's.
            Assert.That(
                boxes.Select(box => box.Y),
                Is.EqualTo(rects.Select(rect => rect.Y + margin)));

            Assert.That(
                labels.Select(text => text.Y),
                Is.EqualTo(drawnLabels.Select(text => text.Y + margin)));

            Assert.That(
                labels.Select(text => text.Text),
                Is.EqualTo(drawnLabels.Select(text => text.Text)),
                "and the same words, because both read BlockExport.Label rather than each having its own");
        });

        VisualProject Document() => Project(
            "Living room",
            Script("s1", "Turn the lamp on", Log("on", "a"), Repeat("r1", Log("again", "b"))),
            Script("s2", "Turn it off", Log("off", "c")));
    }

    [Test]
    [Description("A single script adds its hat, and the body keeps the spacing the vector export gives it.")]
    public void AWholeScriptExportDrawsTheBodyWhereTheVectorExportDrawsItAndAddsTheHat()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a"), Log("bright", "b")));
        var script = project.Targets[0].Scripts[0];

        var plan = PngExportPlan.ForScript(project, script, new PngExportOptions
        {
            Scale = 1,
            Padding = 16,
            Caption = string.Empty,
        });

        var (rects, _) = Drawn(SvgRenderer.Render(project, script));
        var boxes = plan.Boxes.ToList();
        var hat = boxes[0];

        Assert.Multiple(() =>
        {
            Assert.That(plan.Boxes.Count(), Is.EqualTo(rects.Count + 1),
                "one more rectangle than the vector export draws, and it is the hat");

            Assert.That((hat.X, hat.Y), Is.EqualTo((16d, 16d)),
                "the hat is at the origin of the drawing, under the margin");

            Assert.That(
                boxes.Skip(1).Select(box => box.X - boxes[1].X),
                Is.EqualTo(rects.Select(rect => rect.X - rects[0].X)),
                "and the body is laid out as the vector export lays it out, x for x");

            Assert.That(
                boxes.Skip(1).Select(box => box.Y - boxes[1].Y),
                Is.EqualTo(rects.Select(rect => rect.Y - rects[0].Y)),
                "and y for y below the hat's height, so the two exports are the same stack with one block "
                + "added above it - a picture of a script with its hat cut off is a picture of a stack");
        });
    }

    [Test]
    [Description("The colours are the measured palette's, so an export cannot be a colour nobody checked.")]
    public void ABlockIsDrawnInTheColoursTheMeasuredPaletteGives()
    {
        var tokens = BlockThemePalette.For(BlockCategory.Ui, dark: true);
        var plan = PngExportPlan.ForRun([Log("on", "a")], new PngExportOptions { Scale = 1 });

        var box = plan.Boxes.Single(item => item.BlockId == "a");
        var label = plan.Texts.Single(item => item.BlockId == "a");

        Assert.Multiple(() =>
        {
            Assert.That(box.FillTop, Is.EqualTo(tokens.FillTop));
            Assert.That(box.FillBottom, Is.EqualTo(tokens.FillBottom));
            Assert.That(box.Stroke, Is.EqualTo(tokens.Stroke));
            Assert.That(label.Ink, Is.EqualTo(tokens.Ink));
        });
    }

    [Test]
    [Description("The light palette is a choice, not an accident of a default.")]
    public void ALightExportUsesTheLightPaletteAndAPlateYouCanRead()
    {
        var tokens = BlockThemePalette.For(BlockCategory.Ui, dark: false);

        var plan = PngExportPlan.ForDocument(
            Project("Living room", Script("s1", "Turn the lamp on", Log("on", "a"))),
            new PngExportOptions { Scale = 1, Dark = false });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Boxes.First(box => box.BlockId == "a").FillTop, Is.EqualTo(tokens.FillTop));
            Assert.That(
                plan.Boxes.Single(box => box.BlockId is null).FillTop,
                Is.EqualTo(PngExportPlan.CaptionPlateLight),
                "a light caption on a light plate would be invisible wherever the export is pasted");
        });
    }

    [Test]
    [Description("§27.2: an export carries labels and the user's literals, and nothing else.")]
    public void TheExportCarriesLabelsAndLiteralsAndNothingElse()
    {
        var project = Project("Living room", Script("s1", "Turn the lamp on", Log("visible", "a")));

        var description = PngExportPlan.ForDocument(project, new PngExportOptions()).Describe();

        Assert.Multiple(() =>
        {
            Assert.That(description, Does.Contain("visible"), "the user's own text, which is the point of a picture");
            Assert.That(description, Does.Not.Contain("_integration"), "no host surfaces");
            Assert.That(description, Does.Not.Contain("Serilog"), "and no SDK types");
        });
    }

    [Test]
    [Description("A run has no name and no target, so it gets neither in the picture either.")]
    public void ARunExportDrawsTheRunAndNothingElse()
    {
        var run = DeepRun(2);

        var plan = PngExportPlan.ForRun(run, new PngExportOptions { Scale = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Boxes.Count(), Is.EqualTo(2));
            Assert.That(plan.Boxes.Any(box => box.BlockId is null), Is.False, "no caption plate");
            Assert.That(plan.Texts.Any(text => text.BlockId is null), Is.False, "and no caption");
        });
    }

    [Test]
    [Description("A document with nothing in it still names a size, because zero pixels is not an image.")]
    public void AnEmptyDocumentStillNamesASizeARasteriserCanBeGiven()
    {
        var plan = PngExportPlan.ForDocument(
            new VisualProject { DocumentId = VisualProject.NewDocumentId() },
            new PngExportOptions { Scale = 2 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Width, Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.Height, Is.GreaterThanOrEqualTo(1));
            Assert.That(plan.Items, Is.Empty);
        });
    }

    [Test]
    [Description("A literal can hold anything, and the hashed form has to survive it.")]
    public void ALabelWithMarkupAndNewlinesCannotForgeExtraLinesIntoTheFingerprint()
    {
        var awkward = PngExportPlan.ForRun([Log("a & b\n<c> \"quoted\"", "a")], new PngExportOptions { Scale = 1 });
        var different = PngExportPlan.ForRun([Log("a & b \n<c> \"quoted\"", "a")], new PngExportOptions { Scale = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(
                awkward.Describe().Split('\n', StringSplitOptions.RemoveEmptyEntries),
                Has.Length.EqualTo(awkward.Items.Count + 1),
                "one line per instruction, whatever the user typed");

            Assert.That(awkward.Describe(), Does.Contain("&amp;"), "and it is escaped through the same helper "
                + "the vector export escapes a label with");

            Assert.That(awkward.Fingerprint, Is.Not.EqualTo(different.Fingerprint),
                "two labels a newline apart are two drawings, and a form that let a newline split one line "
                + "into two would hash them the same");
        });
    }

    [Test]
    [Description("A block with a body is drawn as itself, and its statements as themselves.")]
    public void AContainerIsOneRectangleAndItsStatementsAreTheirOwn()
    {
        var plan = PngExportPlan.ForRun([Repeat("r1", Log("first", "a"), Log("second", "b"))], Unscaled);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Boxes.Count(), Is.EqualTo(3));

            Assert.That(
                plan.Boxes.Select(box => box.BlockId),
                Is.EqualTo(new[] { "r1", "a", "b" }),
                "the container first, because it sits above its own statements");

            Assert.That(plan.Texts.Select(text => text.BlockId), Is.EqualTo(new[] { "r1", "a", "b" }));
        });
    }
}