using System.Reflection;
using System.Text.RegularExpressions;
using DeckForge.Core.Visual.Commands;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Every command in the table reaches a handler, including the three context menus' worth.
/// </summary>
/// <remarks>
/// <para>
/// A source scan, which is normally the wrong kind of test. This one is right for the same reason
/// <c>XamlEventWiringTests</c> is: what it protects is a table and a <c>switch</c> in a different file, and
/// nothing else in the build or the suite can see the join between them. <c>VisualCommandsTests</c> proves
/// the table is internally consistent; it cannot prove the application acts on it.
/// </para>
/// <para>
/// The failure it catches is a specific and quiet one. <c>Dispatch</c>'s <c>default</c> branch reports
/// "'canvas.undo' is in the shortcut sheet but nothing runs it yet" — so an undispatched command produces a
/// sentence in the message line rather than a crash, and a right-click on a menu item that appears to do
/// nothing. Adding a command to the table and forgetting the page is exactly the mistake Part 18.5's menus
/// make three times more likely than it was before this row existed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VisualCommandDispatchTests
{
    /// <summary>Every command's id, and the name of the constant that spells it.</summary>
    private static IEnumerable<(string Id, string Constant)> Commands()
    {
        var constants = typeof(VisualCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (Name: field.Name, Value: (string)field.GetRawConstantValue()!))
            .ToDictionary(pair => pair.Value, pair => pair.Name, StringComparer.Ordinal);

        foreach (var command in VisualCommands.All)
        {
            Assert.That(
                constants.ContainsKey(command.Id),
                Is.True,
                $"{command.Id} has no named constant, so the page cannot name it in a switch and this test "
                + "cannot tell whether it is handled");

            yield return (command.Id, constants[command.Id]);
        }
    }

    [Test]
    public void Every_command_in_the_table_is_dispatched_by_the_visual_page()
    {
        var page = ReadPageSource();

        Assert.That(page, Is.Not.Null, "The Visual page's code-behind was not found.");

        var missing = Commands()
            .Where(pair => !Mentions(page!, pair.Constant))
            .Select(pair => $"{pair.Id} (VisualCommands.{pair.Constant})")
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "These commands are in the table, so the palette offers them, the shortcut sheet prints their "
            + "gestures and every context menu lists them - and the page's dispatcher reaches a default "
            + "branch that reports that nothing runs them. Nothing crashes and nothing says so until a user "
            + "presses the key:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Test]
    public void The_check_is_looking_at_a_dispatcher_and_not_an_empty_string()
    {
        // A scan that quietly matched nothing would make the test above pass for the wrong reason, which
        // is the failure mode this project has already recorded twice in this file's neighbourhood.
        var page = ReadPageSource();

        Assert.Multiple(() =>
        {
            Assert.That(page, Is.Not.Null);
            Assert.That(page, Does.Contain("private void Dispatch(string id)"),
                "the page's dispatcher has moved or been renamed; update this test rather than letting it "
                + "pass vacuously");
            Assert.That(
                page,
                Does.Contain("is in the shortcut sheet but nothing runs it yet"),
                "and the default branch that reports an undispatched command is still there, which is what "
                + "makes this a real gap rather than a typo");
        });
    }

    [Test]
    public void A_command_that_needs_a_selection_says_so_even_when_it_only_ever_declined_quietly()
    {
        // F9 with nothing selected has always done nothing; the table said otherwise until the stage menu
        // arrived and wanted to grey the row. A flag that lies is worse than no flag, because a menu trusts it.
        Assert.That(
            VisualCommands.Find(VisualCommands.ToggleBreakpoint)?.NeedsSelection,
            Is.True,
            "a breakpoint belongs to a block, and offering the menu row with nothing to set is a control "
            + "that looks live and is not");
    }

    /// <summary>
    /// Whether the page's source names a constant, by the shape a <c>switch</c> case actually takes.
    /// </summary>
    /// <param name="source">The page's code-behind.</param>
    /// <param name="constant">The constant's name, without the class.</param>
    /// <remarks>
    /// A whole-word match, so <c>StageStop</c> is not satisfied by <c>StageStopRecording</c> and
    /// <c>Paste</c> is not satisfied by <c>PasteBlock</c>. Without it the test would go on passing through a
    /// rename that quietly unhandled a command — which is the failure it exists to catch.
    /// </remarks>
    private static bool Mentions(string source, string constant) =>
        Regex.IsMatch(source, @"VisualCommands\." + Regex.Escape(constant) + @"\b");

    /// <summary>The Visual page's code-behind, found from the test assembly the way the other source scans do.</summary>
    private static string? ReadPageSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DeckForge.App");
            if (Directory.Exists(candidate))
            {
                var file = Path.Combine(candidate, "Pages", "VisualEditorPage.xaml.cs");
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
