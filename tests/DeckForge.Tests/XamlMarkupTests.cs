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
}
