using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The Ctrl+digit page shortcuts, pinned to the markup and the code that reads it.
/// </summary>
/// <remarks>
/// <para>
/// The shortcut list was built by flattening the sidebar's items through
/// <c>OfType&lt;System.Collections.IEnumerable&gt;()</c> and a <c>SelectMany</c>, on the assumption
/// that each entry was a nested list of items. Every entry is a <c>NavigationViewItem</c>, which is
/// not <c>IEnumerable</c>, so the cast silently discarded all of them and the list ended up holding
/// only the footer. Ctrl+1 through Ctrl+9 were dead, and the one digit that did reach
/// <c>NavigateTo</c> was a footer tag, so a shortcut could open Settings when the user asked for
/// page one.
/// </para>
/// <para>
/// Nothing failed: the build was clean, every other test passed, and the sidebar looked perfect. Only
/// pressing the key could show it, and nothing presses keys in a test. So the two things that made
/// it invisible are checked here instead - the tags have to exist and be unique, and the collection
/// has to keep using the cast that actually matches the collection's contents.
/// </para>
/// </remarks>
[TestFixture]
public sealed class NavigationShortcutTests
{
    private static readonly XNamespace Ui = "http://schemas.lepo.co/wpfui/2022/xaml";

    private static string? AppFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DeckForge.App", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static List<XElement> NavigationItems()
    {
        var path = AppFile("MainWindow.xaml")
            ?? throw new InvalidOperationException("MainWindow.xaml could not be located.");
        var document = XDocument.Load(path);

        return document.Descendants()
            .Where(e => e.Name.LocalName == "NavigationViewItem")
            .ToList();
    }

    [Test]
    public void Every_sidebar_item_carries_a_tag_for_the_shortcut_to_navigate_to()
    {
        var items = NavigationItems();
        Assert.That(items, Is.Not.Empty, "No NavigationViewItem was found in the sidebar.");

        var untagged = items
            .Where(e => string.IsNullOrWhiteSpace((string?)e.Attribute("Tag")))
            .Select(e => (string?)e.Attribute("Content") ?? e.Name.LocalName)
            .ToList();

        Assert.That(
            untagged,
            Is.Empty,
            "These sidebar items have no Tag, so a Ctrl+digit shortcut cannot navigate to them: "
            + string.Join(", ", untagged));
    }

    [Test]
    public void The_tags_are_unique_so_a_shortcut_can_only_mean_one_page()
    {
        var duplicates = NavigationItems()
            .Select(e => (string?)e.Attribute("Tag"))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .GroupBy(tag => tag!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(
            duplicates,
            Is.Empty,
            "Two sidebar items share a Tag, so the number that navigates to one also highlights "
            + "and navigates to the other: " + string.Join(", ", duplicates));
    }

    [Test]
    public void The_shortcut_list_is_not_built_by_casting_the_items_to_a_collection()
    {
        var path = AppFile("MainWindow.xaml.cs")
            ?? throw new InvalidOperationException("MainWindow.xaml.cs could not be located.");
        var source = File.ReadAllText(path);

        // The shape that silently emptied the list: treating a NavigationViewItem as a nested
        // collection. NavigationViewItem is a control, not an IEnumerable, so this always yields
        // nothing and never throws - which is why it survived a clean build and a passing suite.
        var broken = Regex.Matches(
            source,
            @"OfType\s*<\s*System\.Collections\.IEnumerable\s*>",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        Assert.That(
            broken.Count,
            Is.Zero,
            "The sidebar items are being flattened through OfType<System.Collections.IEnumerable> again. "
            + "NavigationViewItem is not IEnumerable, so this filters out every page and the Ctrl+digit "
            + "shortcuts silently stop working. Use OfType<Wpf.Ui.Controls.NavigationViewItem>().");

        // And the cast that does match, so the shortcut list and the sidebar cannot drift apart.
        Assert.That(
            source,
            Does.Contain("OfType<Wpf.Ui.Controls.NavigationViewItem>()"),
            "The sidebar items are no longer collected as NavigationViewItem, so the shortcut tags "
            + "cannot be read from them.");
    }
}
