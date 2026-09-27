using DeckForge.Core.Capabilities;
using DeckForge.Core.Plugins;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the capability descriptors the gallery renders.
/// </summary>
/// <remarks>
/// The gallery card binds a descriptor's Glyph straight into a WPF.UI SymbolIcon. A glyph name that
/// is not in that library's enum is not a compile error and not a startup error - it renders as a
/// blank box on one card. So the names are checked against the real enum here, where a typo is a
/// failed test rather than an empty square nobody notices.
/// </remarks>
[TestFixture]
public sealed class CapabilityCatalogTests
{
    private static readonly HashSet<string> KnownGlyphs = LoadKnownGlyphs();

    private static HashSet<string> LoadKnownGlyphs()
    {
        // Wpf.Ui.Controls.SymbolRegular, read by name. The test project has no reference to the UI
        // assembly, so it is loaded reflectively and the check skipped if that is not possible.
        try
        {
            var app = System.Reflection.Assembly.Load("DeckForge.App");
            var symbol = app.GetType("Wpf.Ui.Controls.SymbolIconSource")
                ?.GetProperty("Symbol")?.PropertyType;

            return symbol is { IsEnum: true }
                ? [.. Enum.GetNames(symbol)]
                : [];
        }
        catch (System.IO.FileNotFoundException)
        {
            return [];
        }
    }

    [Test]
    public void Every_capability_has_a_distinct_glyph()
    {
        var glyphs = CapabilityCatalog.All.Select(c => c.Glyph).ToList();

        Assert.That(
            glyphs.Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(glyphs.Count),
            "Two capabilities share a glyph: "
            + string.Join(", ", glyphs.GroupBy(g => g, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)));
    }

    [Test]
    public void Every_glyph_is_one_the_icon_library_actually_defines()
    {
        Assume.That(KnownGlyphs, Is.Not.Empty, "The icon enum could not be read from this test host.");

        var unknown = CapabilityCatalog.All
            .Select(c => new { c.Id, c.Glyph })
            .Where(c => !KnownGlyphs.Contains(c.Glyph))
            .ToList();

        Assert.That(
            unknown,
            Is.Empty,
            "A descriptor names a glyph Wpf.Ui does not have, so the card renders blank: "
            + string.Join(", ", unknown.Select(c => $"{c.Id}={c.Glyph}")));
    }

    [Test]
    public void Every_capability_says_which_docs_page_documents_it()
    {
        var missing = CapabilityCatalog.All
            .Where(c => string.IsNullOrWhiteSpace(c.DocsPath))
            .Select(c => c.Id)
            .ToList();

        Assert.That(missing, Is.Empty, "No docs path: " + string.Join(", ", missing));
    }

    [Test]
    public void Every_capability_documents_its_own_permissions()
    {
        // A capability asks the user for a host permission, so it has to be able to say what for.
        // The host grants only what the manifest declares, which makes this a security-relevant
        // string, not a nicety.
        var undocumented = PermissionCatalog.All
            .Where(p => string.IsNullOrWhiteSpace(p.Explanation))
            .Select(p => p.Name)
            .ToList();

        Assert.That(
            undocumented,
            Is.Empty,
            "A known permission has no explanation: " + string.Join(", ", undocumented));
    }

    [Test]
    public void Every_permission_a_capability_asks_for_is_one_the_catalog_explains()
    {
        var unknown = CapabilityCatalog.All
            .SelectMany(c => c.Permissions.Select(p => (c.Id, Permission: p)))
            .Where(x => PermissionCatalog.ExplanationOf(x.Permission) is null)
            .ToList();

        Assert.That(
            unknown,
            Is.Empty,
            "A capability requests a permission nobody can explain, so the user sees a bare string: "
            + string.Join(", ", unknown.Select(x => $"{x.Id} -> {x.Permission}")));
    }

    [Test]
    public void Every_capability_says_what_it_does()
    {
        // The card shows this under the title. An empty one reads as a dead end.
        var blank = CapabilityCatalog.All
            .Where(c => string.IsNullOrWhiteSpace(c.Summary))
            .Select(c => c.Id)
            .ToList();

        Assert.That(blank, Is.Empty, "No summary: " + string.Join(", ", blank));
    }

    [Test]
    public void The_gallery_cannot_offer_a_capability_the_scaffolder_does_not_know()
    {
        // The two lists are maintained separately - one in Core, one in CodeGen - and the gallery's
        // "Add to plugin" button used to be enabled for all of them whatever the scaffolder did.
        var gallery = CapabilityCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var scaffoldable = CodeGen.Capabilities.CapabilityDefinitions.Ids.ToHashSet(StringComparer.Ordinal);

        var notScaffoldable = gallery.Except(scaffoldable).OrderBy(id => id, StringComparer.Ordinal).ToList();

        Assert.That(
            notScaffoldable,
            Is.Empty,
            "The gallery offers these but the scaffolder cannot add them: " + string.Join(", ", notScaffoldable));
    }
}
