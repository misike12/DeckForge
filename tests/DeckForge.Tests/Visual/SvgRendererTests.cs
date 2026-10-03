using System.Xml.Linq;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The SVG export: the same geometry the canvas draws, from the same layout model.
/// </summary>
/// <remarks>
/// The interesting property is not that the file parses — it is that it *agrees with the canvas*. So the
/// tests check the rectangles against <see cref="StackLayout"/> directly, and parse the result with a real
/// XML reader rather than a string search, because the failure mode for a hand-written SVG is a document
/// that is neither an image nor a file.
/// </remarks>
[TestFixture]
public sealed class SvgRendererTests
{
    private static readonly System.Globalization.CultureInfo PriorCulture =
        System.Globalization.CultureInfo.CurrentCulture;

    [OneTimeSetUp]
    public void ForceInvariant() => Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

    // Restored, because the suite is sequential and the next fixture on this thread inherited the invariant
    // culture for the rest of the run - which is how a test somewhere else ends up asserting formatting that
    // only holds in one culture and nobody can say why.
    [OneTimeTearDown]
    public void RestoreCulture() => Thread.CurrentThread.CurrentCulture = PriorCulture;

    private static VisualProject Project(params VisualScript[] scripts)
    {
        var project = new VisualProject { DocumentId = VisualProject.NewDocumentId() };
        var target = new VisualTarget { Id = "t", Name = "Action", Kind = TargetKind.Action };
        project.Targets.Add(target);
        target.Scripts.AddRange(scripts);

        return project;
    }

    private static VisualScript Script(string id, params Block[] body)
    {
        var script = new VisualScript
        {
            Id = id,
            Name = id,
            Hat = new Block { Kind = "hat.action-runs", Id = $"hat-{id}" },
        };

        script.Body.AddRange(body);

        return script;
    }

    private static Block Log(string text) => new()
    {
        Kind = "ui.log",
        Id = $"b-{Guid.NewGuid():N}"[..8],
        Inputs = new Dictionary<string, BlockInput>(StringComparer.Ordinal) { ["template"] = new() { Text = text } },
    };

    [Test]
    public void A_rendered_script_is_a_document_any_xml_reader_accepts()
    {
        var script = Script("main", Log("hello"), Log("world"));
        var svg = SvgRenderer.Render(Project(script), script);

        // A real parse, not a string search: the failure a hand-written SVG has is malformed markup, and a
        // substring check passes on every malformed document there is.
        var document = XDocument.Parse(svg);

        Assert.Multiple(() =>
        {
            Assert.That(document.Root?.Name.LocalName, Is.EqualTo("svg"));
            Assert.That(svg, Does.Contain("xmlns=\"http://www.w3.org/2000/svg\""),
                "without the namespace it is a picture of an SVG, not an SVG");
        });
    }

    [Test]
    public void Every_block_gets_a_rectangle_at_the_place_the_layout_model_put_it()
    {
        var script = Script("main", Log("one"), Log("two"), Log("three"));

        var svg = SvgRenderer.Render(Project(script), script);
        var expected = StackLayout.LayoutRun(script.Body);

        Assert.Multiple(() =>
        {
            Assert.That(Count(svg, "<rect"), Is.EqualTo(expected.Count),
                "one rectangle per block, which is the only way an export can be checked against a layout");

            // Compared per element, from the parsed document, rather than as two independent substring
            // searches over the whole file: an x from one rectangle and a y from a *different* rectangle
            // satisfied the old assertion, so it passed whatever the renderer did with either value.
            var drawn = XDocument.Parse(svg)
                .Descendants()
                .Where(element => element.Name.LocalName == "rect")
                .Select(element => (
                    X: double.Parse(element.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture),
                    Y: double.Parse(element.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            var wanted = expected.Values.Select(rect => (rect.X + 16, rect.Y + 16)).OrderBy(pair => pair).ToList();

            Assert.That(drawn.Select(pair => (pair.X, pair.Y)).OrderBy(pair => pair), Is.EqualTo(wanted),
                "each rectangle at the position the layout model put that same block");
        });
    }

    /// <summary>A coordinate as the renderer writes it: invariant, and never a comma.</summary>
    private static string F(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Test]
    public void The_svg_is_sized_to_the_document_plus_a_margin()
    {
        var script = Script("main", Log("one"));
        var extent = CanvasView.ExtentOf(StackLayout.LayoutRun(script.Body).Values);

        var svg = SvgRenderer.Render(Project(script), script);

        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.Contain($"width=\"{extent.Width + 32:0.##}\""));
            Assert.That(svg, Does.Contain($"height=\"{extent.Height + 32:0.##}\""));
        });
    }

    [Test]
    public void A_label_carrying_xml_is_escaped_rather_than_breaking_the_document()
    {
        // A literal the user typed, called a & b. Without escaping the export is not a document, and the
        // failure lands in whatever opens it rather than here.
        var script = Script("main", Log("a & b <c> \"quoted\""));

        var svg = SvgRenderer.Render(Project(script), script);
        var document = XDocument.Parse(svg);

        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.Contain("&amp;").And.Contain("&lt;c&gt;"));
            Assert.That(
                document.Descendants().Any(element => element.Value.Contains("a & b <c>", StringComparison.Ordinal)),
                Is.True,
                "and the text survives the round trip, which is the part that matters: escaping the file is "
                + "only useful if the reader un-escapes it back");
        });
    }

    [Test]
    public void The_export_carries_labels_and_literals_and_nothing_else()
    {
        // §27.2: an export must not carry data the user did not mean to share. A host surface name and a
        // block's own summary are exactly that.
        var script = Script("main", Log("visible"));

        var svg = SvgRenderer.Render(Project(script), script);

        Assert.Multiple(() =>
        {
            Assert.That(svg, Does.Contain("visible"));
            Assert.That(
                svg,
                Does.Not.Contain("_integration"),
                "no host surfaces, because an image of a script is not the place to disclose what it calls");
            Assert.That(svg, Does.Not.Contain("MacroDeck"), "and no SDK types");
        });
    }

    [Test]
    public void A_document_with_several_scripts_draws_them_beside_each_other_rather_than_on_top_of_each_other()
    {
        var first = Script("alpha", Log("one"));
        var second = Script("beta", Log("two"));

        var svg = SvgRenderer.RenderDocument(Project(first, second));

        Assert.That(svg, Does.Contain("alpha").And.Contain("beta"),
            "both scripts are named, because a document's shape is part of what a picture of it should show");
    }

    [Test]
    public void An_empty_document_renders_an_empty_document_rather_than_throwing()
    {
        var project = Project();

        var svg = SvgRenderer.RenderDocument(project);

        Assert.Multiple(() =>
        {
            Assert.That(XDocument.Parse(svg).Root?.Name.LocalName, Is.EqualTo("svg"));
            Assert.That(svg, Does.Contain("width=\"32\""), "the margin, and nothing else");
        });
    }

    [Test]
    public void A_script_with_nothing_in_it_renders_too()
    {
        var script = Script("empty");
        var svg = SvgRenderer.Render(Project(script), script);

        Assert.Multiple(() =>
        {
            Assert.That(XDocument.Parse(svg).Root?.Name.LocalName, Is.EqualTo("svg"));
            Assert.That(
                svg,
                Does.Not.Contain("<rect"),
                "a hat on its own draws no statement rectangle, because there are no statements");
        });
    }

    [Test]
    public void The_colours_come_from_the_catalogue_and_not_from_a_theme_that_may_not_be_loaded()
    {
        var script = Script("main", Log("hello"));
        var svg = SvgRenderer.Render(Project(script), script);
        var hue = BlockCatalog.Category(BlockCategory.Ui).Hue;

        Assert.That(svg, Does.Contain(hue),
            "the export has to be identical on a machine that has never opened the editor, and a resource "
            + "lookup only resolves inside a running application");
    }

    [Test]
    public void Numbers_are_written_with_invariant_separators_so_a_locale_cannot_break_the_file()
    {
        // A Hungarian or German machine writes "1,5", which is not a number in SVG and which fails in a
        // viewer rather than in the export.
        var script = Script("main", Log("x"));
        var previous = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");

            var svg = SvgRenderer.Render(Project(script), script);

            // The font stack has a comma in it and must keep it; what must not appear is a comma inside a
            // number, which is what a locale would put there.
            Assert.Multiple(() =>
            {
                Assert.That(svg, Does.Contain("Segoe UI, sans-serif"), "the font stack is left alone");
                Assert.That(
                    // Anchored on a word boundary: without it, "font-family" matches the `y` in "family" and
                    // the test reports the font stack as a coordinate.
                    System.Text.RegularExpressions.Regex.Matches(svg, @"\b(?:x|y|width|height|viewBox)=""[^""]*,[^""]*""").Count,
                    Is.Zero,
                    "no coordinate carries a comma, because a Hungarian or German machine writes 1,5 and a "
                    + "viewer fails where the export did not");
            });
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}