using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
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
    public void No_binding_calls_a_method()
    {
        // A WPF binding resolves against properties. Binding to a method - Describe() - finds nothing,
        // resolves to nothing, and renders blank with no error anywhere. The block canvas did this, so
        // every statement row was an empty pill and the program looked empty however many blocks it
        // held, with nothing to point at.
        //
        // The parentheses are what makes this unambiguous: a plain {Binding Name} is resolved against
        // whatever the DataContext happens to be - the page view model on a page, a block inside an
        // item template - so it cannot be checked without loading the WPF types, which the test project
        // does not reference. A call is never a valid binding path in any DataContext.
        var calls = new List<string>();
        foreach (var file in MarkupFiles())
        {
            foreach (Match binding in Regex.Matches(
                File.ReadAllText(file),
                @"\{Binding\s+[A-Za-z_][A-Za-z0-9_.]*\s*\(\s*\)\s*\}",
                RegexOptions.Compiled))
            {
                calls.Add($"{Path.GetFileName(file)}: {binding.Value}");
            }
        }

        Assert.That(
            calls,
            Is.Empty,
            "These bindings call a method, so they resolve to nothing and render blank with no error. "
            + "Expose the value as a property and bind to that:" + Environment.NewLine
            + string.Join(Environment.NewLine, calls));
    }

    [Test]
    public void The_method_binding_check_is_actually_looking_at_bindings()
    {
        var files = MarkupFiles().ToList();
        var bindings = files.Sum(file => Regex.Matches(
            File.ReadAllText(file), @"\{Binding\s+[A-Za-z_][A-Za-z0-9_.]*\s*(\(\s*\))?\s*\}").Count);

        Assert.That(bindings, Is.GreaterThan(0), "No simple bindings were found; the check is vacuous.");
    }

    [Test]
    public void Every_block_and_node_type_can_describe_itself_for_the_canvas()
    {
        // The two lists the canvas renders: block statements and widget nodes. Each row binds a label,
        // so each type has to expose it as a property with a value. A type that forgets renders a blank
        // row, which is indistinguishable from an empty list.
        Assert.Multiple(() =>
        {
            foreach (var type in BlockAndNodeTypes())
            {
                var description = type.GetProperty("Description", BindingFlags.Public | BindingFlags.Instance);
                Assert.That(description, Is.Not.Null, $"{type.Name} has no public Description property.");
                Assert.That(description!.PropertyType, Is.EqualTo(typeof(string)));
            }
        });
    }

    [Test]
    public void Every_block_kind_has_a_non_empty_description()
    {
        // A description that is blank on a fresh instance is as invisible as a missing property, so the
        // value is checked rather than only its presence.
        var blank = new List<string>();
        foreach (var type in BlockAndNodeTypes())
        {
            var instance = Activator.CreateInstance(type) as DeckForge.Core.Blocks.BlockStatement;
            if (instance is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(instance.Description))
            {
                blank.Add(type.Name);
            }
        }

        Assert.That(blank, Is.Empty, "These render an empty row: " + string.Join(", ", blank));
    }

    /// <summary>The block statement types, and the widget node type, that the canvas rows bind to.</summary>
    private static IEnumerable<Type> BlockAndNodeTypes() =>
        typeof(DeckForge.Core.Blocks.BlockStatement).Assembly
            .GetTypes()
            .Where(type => typeof(DeckForge.Core.Blocks.BlockStatement).IsAssignableFrom(type) && !type.IsAbstract)
            .Append(typeof(DeckForge.CodeGen.Generation.DesignedNode))
            .OrderBy(type => type.Name);

    [Test]
    public void No_attribute_value_is_broken_across_source_lines()
    {
        // A newline inside a Text="..." attribute is not a way to wrap the string, and it does not
        // render as a line break either - it leaves a gap in the middle of the sentence. Five setting
        // descriptions read like "The wizard          also offers a 'remember this folder' checkbox",
        // with the words either side of the break pulled apart and nothing in the markup to explain
        // it. Wrapping is the TextBlock's job, via TextWrapping.
        var split = new List<string>();

        foreach (var file in MarkupFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // An unterminated quote at the end of the line is the whole signal: the value runs on
                // to the next line. Checking the value itself cannot find this, because by the time
                // the lines are joined the extra space is indistinguishable from a real one.
                if (Regex.IsMatch(lines[i], @"(?:Text|Content|ToolTip|Header)=""[^""]*$"))
                {
                    split.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.That(
            split,
            Is.Empty,
            "These attribute values are wrapped across source lines, which leaves a gap in the text "
            + "instead of wrapping it. Put each on one line and let TextWrapping do the work:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, split));
    }

    [Test]
    public void No_control_asks_for_a_row_or_column_its_grid_does_not_have()
    {
        // A Grid.Row or Grid.Column beyond the last definition does not fail - the Grid invents an
        // implicit row or column, sized to nothing when its siblings are stars. The control is then
        // laid out on top of the content that was already there. The Generate button on the config
        // flow editor asked for row 2 of a two-row page and sat across the step list, covering a
        // field, with nothing in the build or the output to say why.
        //
        // Walked as XML rather than with a pattern, so a nested grid's rows are counted against that
        // nested grid and not against the page's.
        var problems = new List<string>();
        var gridsChecked = 0;

        foreach (var file in MarkupFiles())
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                problems.Add($"{Path.GetFileName(file)}: could not be parsed as XML ({ex.Message})");
                continue;
            }

            foreach (var grid in document.Descendants().Where(e => e.Name.LocalName == "Grid"))
            {
                gridsChecked++;
                problems.AddRange(MissingCells(file, grid, "Row", "RowDefinition"));
                problems.AddRange(MissingCells(file, grid, "Column", "ColumnDefinition"));
            }
        }

        Assert.That(gridsChecked, Is.GreaterThan(0), "No grids were found to check.");

        Assert.That(
            problems,
            Is.Empty,
            "These controls are placed in a row or column their grid does not define. The Grid will "
            + "invent an implicit one of no size and lay the control over its neighbours:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Direct children of <paramref name="grid"/> whose <c>Grid.Row</c> or <c>Grid.Column</c> is past
    /// the last definition, described well enough to find in the file.
    /// </summary>
    private static IEnumerable<string> MissingCells(
        string file,
        XElement grid,
        string direction,
        string definitionName)
    {
        // The element is Grid.RowDefinitions, not RowDefinitions - it is an attached property, so
        // its local name carries the type prefix. Getting that wrong makes every grid look as though
        // it declared nothing, and the check passes for the wrong reason.
        var declared = grid.Elements()
            .Where(e => e.Name.LocalName == $"Grid.{direction}Definitions")
            .SelectMany(e => e.Elements().Where(c => c.Name.LocalName == definitionName))
            .Count();

        if (declared == 0)
        {
            // A grid with no explicit definitions puts everything in row 0 / column 0.
            yield break;
        }

        foreach (var child in grid.Elements())
        {
            var attribute = child.Attribute($"Grid.{direction}");
            if (attribute is null || !int.TryParse(attribute.Value, out var index) || index < declared)
            {
                continue;
            }

            var line = child.Attribute("Xaml.Line")?.Value ?? "?";
            var name = child.Attribute("Name")?.Value
                ?? child.Attribute("Content")?.Value
                ?? child.Name.LocalName;
            yield return $"{Path.GetFileName(file)}: {child.Name.LocalName} \"{name}\" asks for " +
                $"Grid.{direction}=\"{index}\" but the grid declares {declared}";
        }
    }

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
