using System.Text.RegularExpressions;
using NUnit.Framework;
using Wpf.Ui.Controls;

namespace DeckForge.Tests;

/// <summary>
/// The WPF markup, checked against the real controls it names.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ui:SymbolIcon</c> names a member of an enum, and the compiler cannot check that. The build
/// succeeded, every test passed, and the application still failed to start: a
/// <c>Puzzle24</c> that does not exist made MainWindow's XAML throw during
/// <c>OnStartup</c>, before the window was shown.
/// </para>
/// <para>
/// That was invisible because the crash handler swallows the first three UI-thread exceptions, and
/// before the window exists there is nowhere to show the banner it would have used - so the process
/// sat in Task Manager, responsive, showing nothing. The handler now exits with the error, and this
/// test stops the mistake reaching it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XamlMarkupTests
{
    /// <summary>The App project's markup, which is the only part a test can reach.</summary>
    private static IEnumerable<string> MarkupFiles()
    {
        var appDirectory = FindDirectory("DeckForge.App");
        if (appDirectory is null)
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static string? FindDirectory(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", name);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Test]
    public void Every_symbol_icon_names_a_member_that_exists()
    {
        var known = Enum.GetNames<SymbolRegular>().ToHashSet(StringComparer.Ordinal);
        Assert.That(known, Is.Not.Empty, "WPF.UI's SymbolRegular enum could not be read.");

        var unknown = new List<string>();

        foreach (var file in MarkupFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(
                text,
                @"\{ui:SymbolIcon\s+([A-Za-z_][A-Za-z0-9_]*)\}",
                RegexOptions.Compiled))
            {
                var name = match.Groups[1].Value;
                if (!known.Contains(name))
                {
                    unknown.Add($"{Path.GetFileName(file)}: SymbolIcon {name}");
                }
            }
        }

        Assert.That(
            unknown,
            Is.Empty,
            "These icon names are not members of SymbolRegular, so the XAML throws when the window is "
            + "constructed and the application never shows a window:" + Environment.NewLine
            + string.Join(Environment.NewLine, unknown));
    }

    [Test]
    public void The_markup_that_names_an_icon_is_actually_being_checked()
    {
        // A regex that quietly matches nothing would make the test above pass for the wrong reason,
        // which is how the original problem stayed hidden.
        var files = MarkupFiles().ToList();
        Assert.That(files, Is.Not.Empty, "No markup was found to check.");

        var icons = files.Sum(file => Regex.Matches(
            File.ReadAllText(file), @"\{ui:SymbolIcon\s+([A-Za-z_][A-Za-z0-9_]*)\}").Count);

        Assert.That(icons, Is.GreaterThan(0), "No SymbolIcon usages were found; the check is vacuous.");
    }

    /// <summary>
    /// Total fixed column widths that still fit beside a minimum-width window, with the page margin
    /// and the navigation rail taken off. A row over this pushes its right-hand content off the edge
    /// with no way to scroll to it.
    /// </summary>
    private const int FixedColumnBudget = 900;

    [Test]
    public void No_row_of_columns_is_wider_than_a_small_window()
    {
        // Fixed columns out to 460px inside a 340px card (the action and event parameter rows), and
        // 1080px of page columns on the widget designer. All of it rendered off the right edge at
        // ordinary window sizes, and what overflowed was the part the user edits: parameter rows, form
        // fields, and the Generate button. Nothing in the app scrolls horizontally, so the overflow was
        // simply unreachable.
        //
        // Every row is checked, not just the page's own columns: a nested row is the common case, and
        // it is the one that overflows a narrow card while looking fine in the page-level numbers.
        var tooWide = new List<string>();

        foreach (var file in MarkupFiles())
        {
            foreach (var (columns, total) in FixedColumnRows(file))
            {
                if (total > FixedColumnBudget)
                {
                    tooWide.Add(
                        $"{Path.GetFileName(file)}: {total}px across {columns} fixed columns, over the "
                        + $"{FixedColumnBudget}px budget");
                }
            }
        }

        Assert.That(
            tooWide,
            Is.Empty,
            "These rows are wider than a small window, so their right-hand content is pushed off the "
            + "edge with nothing to scroll to it. Use proportional columns with MinWidth and let the "
            + "contents wrap:" + Environment.NewLine
            + string.Join(Environment.NewLine, tooWide));
    }

    [Test]
    public void The_column_budget_check_is_actually_looking_at_column_rows()
    {
        // Same reason as the icon check above: a pattern that matches nothing passes for the wrong reason.
        var rows = MarkupFiles().SelectMany(FixedColumnRows).ToList();

        Assert.That(rows, Is.Not.Empty, "No column definitions were found to check.");
        Assert.That(rows.Sum(row => row.Total), Is.GreaterThan(FixedColumnBudget),
            "Nothing came close to the budget, so the check would not notice a wide row.");
    }

    /// <summary>
    /// Every row of fixed-width columns declared in a markup file, with its total.
    /// </summary>
    /// <remarks>
    /// Star and auto widths are ignored: they absorb the space that is left, which is the property
    /// that makes a layout survive a narrow window. Only a run of fixed pixels can push content past
    /// the edge, and it does so regardless of how much room the row actually has.
    /// </remarks>
    private static IEnumerable<(int Columns, int Total)> FixedColumnRows(string file)
    {
        var text = File.ReadAllText(file);
        foreach (Match block in Regex.Matches(
            text,
            @"<Grid\.ColumnDefinitions>(?<cols>.*?)</Grid\.ColumnDefinitions>",
            RegexOptions.Singleline))
        {
            var total = 0;
            var count = 0;
            foreach (Match column in Regex.Matches(
                block.Groups["cols"].Value,
                @"<ColumnDefinition\s+Width=""(\d+(?:\.\d+)?)"""))
            {
                if (double.TryParse(
                        column.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var width))
                {
                    total += (int)width;
                    count++;
                }
            }

            if (count > 0)
            {
                yield return (count, total);
            }
        }
    }
}
