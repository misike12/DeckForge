using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Event wiring in markup, which the compiler does not check and the build does not catch.
/// </summary>
/// <remarks>
/// <para>
/// <c>ViewRequested="Minimap_ViewRequested"</c> compiled, every test passed, and the Visual page threw
/// during load: <c>Cannot bind to the target method because its signature is not compatible with that of
/// the delegate type</c>. The event had been declared as a plain
/// <see cref="System.Windows.RoutedEventHandler"/> while the handler asked for a subclass of
/// <see cref="System.Windows.RoutedEventArgs"/> carrying the view the click produced - and a plain
/// handler can only ever be handed the base type. The shell caught the exception, showed its recovery
/// banner and went back to Home, so the symptom was a page that would not open rather than anything that
/// looked like a mistake in an event.
/// </para>
/// <para>
/// The build cannot see this: an event wired in markup is bound at load, by reflection, on the UI thread.
/// These tests check the part of it that is visible from here - the source of the control and of the
/// markup - and no more than that.
/// </para>
/// </remarks>
[TestFixture]
public sealed class XamlEventWiringTests
{
    /// <summary>
    /// The events whose attribute names are known, so a check for "is this handler there" does not read
    /// <c>Theme="Dark"</c> as a handler named Dark.
    /// </summary>
    /// <remarks>
    /// XAML has no way to tell an event attribute from a dependency-property attribute: both are
    /// <c>Name="value"</c>, and only the target's type says which is which. So the list is the honest
    /// bound - these are the events the project's markup actually uses, and a new one has to be added
    /// rather than guessed at.
    /// </remarks>
    private static readonly string[] KnownEvents =
    [
        "Click", "DoubleClick", "SelectionChanged", "TextChanged", "ValueChanged", "Checked", "Unchecked",
        "KeyDown", "KeyUp", "PreviewKeyDown", "PreviewKeyUp", "TextInput", "GotFocus", "LostFocus",
        "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonDown", "MouseMove", "MouseWheel",
        "MouseDoubleClick", "PreviewMouseLeftButtonDown", "PreviewMouseWheel", "PreviewMouseMove",
        "DragEnter", "DragOver", "DragLeave", "Drop", "Loaded", "Unloaded", "SizeChanged", "ScrollChanged",
        "Opened", "Closed", "SelectionChanged", "IsVisibleChanged", "ToolTipOpening",
    ];

    private static IEnumerable<string> Files(string extension)
    {
        var appDirectory = FindDirectory("DeckForge.App");
        if (appDirectory is null)
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(appDirectory, extension, SearchOption.AllDirectories))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
    public void A_handler_markup_wires_is_declared_with_the_args_type_it_asks_for()
    {
        Assert.That(Files("*.xaml").Any(), Is.True,
            "the App source tree was not found from " + AppContext.BaseDirectory
            + ", so this test would have asserted nothing at all");
        // The rule, and it is narrower than "every custom event needs a custom delegate": an event may
        // perfectly well be a plain RoutedEventHandler that a handler receives as RoutedEventArgs and then
        // casts - four of this project's events do exactly that. The failure is only when markup names a
        // handler that asks for a subclass, because a RoutedEventHandler cannot be bound to one at all.
        var offenders = new List<string>();

        foreach (var markup in Files("*.xaml"))
        {
            var text = File.ReadAllText(markup);
            var codeBehind = Path.ChangeExtension(markup, ".xaml.cs");

            if (!File.Exists(codeBehind))
            {
                continue;
            }

            var code = File.ReadAllText(codeBehind);

            foreach (Match wiring in Regex.Matches(
                         text,
                         @"(?<event>[A-Za-z0-9_]+)=""(?<handler>[A-Za-z_][A-Za-z0-9_]*)"""))
            {
                var handler = wiring.Groups["handler"].Value;
                var declaration = Regex.Match(
                    code,
                    @"\b" + Regex.Escape(handler) + @"\s*\([^)]*?,\s*(?<args>[A-Za-z0-9_.]+)\s+[A-Za-z_][A-Za-z0-9_]*\s*\)");

                if (!declaration.Success)
                {
                    continue;
                }

                var args = declaration.Groups["args"].Value.Split('.').Last();

                if (args == "RoutedEventArgs" || args == "EventArgs")
                {
                    continue;
                }

                var eventName = wiring.Groups["event"].Value;
                var registered = Regex.Match(
                    string.Join("\n", Files("*.cs").Select(File.ReadAllText)),
                    @"RegisterRoutedEvent\(\s*""" + Regex.Escape(eventName) + @"""[\s\S]{0,300}?typeof\((?<delegate>[A-Za-z0-9_.]+)\)");

                if (registered.Success
                    && registered.Groups["delegate"].Value.Split('.').Last() == "RoutedEventHandler")
                {
                    offenders.Add(
                        $"{Path.GetFileName(markup)}: {eventName}=\"{handler}\" asks for {args}, but {eventName} "
                        + "is a RoutedEventHandler and can only be handed RoutedEventArgs");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "this binds at load, by reflection, on the UI thread: the build passes, the tests pass, and the "
            + "page throws the first time anybody navigates to it");
    }

    [Test]
    public void Every_known_event_wired_in_markup_has_a_handler_in_the_code_behind()
    {
        Assert.That(Files("*.xaml").Any(), Is.True,
            "the App source tree was not found from " + AppContext.BaseDirectory
            + ", so this test would have asserted nothing at all");
        var missing = new List<string>();

        foreach (var markup in Files("*.xaml"))
        {
            var codeBehind = Path.ChangeExtension(markup, ".xaml.cs");
            if (!File.Exists(codeBehind))
            {
                continue;
            }

            var code = File.ReadAllText(codeBehind);

            foreach (var eventName in KnownEvents)
            {
                foreach (Match wiring in Regex.Matches(
                             File.ReadAllText(markup),
                             @"\b" + Regex.Escape(eventName) + @"=""(?<handler>[A-Za-z_][A-Za-z0-9_]*)"""))
                {
                    var handler = wiring.Groups["handler"].Value;

                    if (!Regex.IsMatch(code, @"\b" + Regex.Escape(handler) + @"\s*\("))
                    {
                        missing.Add(
                            $"{Path.GetFileName(markup)}: {eventName}=\"{handler}\" has no such method in "
                            + Path.GetFileName(codeBehind));
                    }
                }
            }
        }

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void The_minimaps_view_request_is_raised_and_declared_with_the_same_args_type()
    {
        // Named for the defect it came from. Kept narrow on purpose: the general tests above are source
        // scans and can be wrong about intent, but this is the exact thing that made the page unloadable.
        var file = Path.Combine(
            FindDirectory("DeckForge.App") ?? string.Empty,
            "Controls",
            "Blocks",
            "CanvasMinimap.xaml.cs");

        Assert.That(File.Exists(file), Is.True, file + " was not found.");

        var text = File.ReadAllText(file);

        Assert.Multiple(() =>
        {
            Assert.That(
                Regex.Match(text, @"RegisterRoutedEvent\(\s*""ViewRequested""[\s\S]{0,300}?typeof\((?<delegate>[A-Za-z0-9_.]+)\)")
                    .Groups["delegate"].Value,
                Is.Not.EqualTo("RoutedEventHandler"),
                "the handler asks for the view the click produced, and a plain RoutedEventHandler cannot carry it");
            Assert.That(text, Does.Contain("RaiseEvent(new ViewRequestedEventArgs("));
            Assert.That(text, Does.Contain("MinimapViewRequestedEventHandler"),
                "the delegate the event is registered with has to accept ViewRequestedEventArgs");
        });
    }
}
