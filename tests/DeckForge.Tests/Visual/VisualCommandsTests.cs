using DeckForge.Core.Visual.Commands;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The command table: the palette's list, the shortcut sheet's rows and the page's key handling, all
/// read from one place.
/// </summary>
/// <remarks>
/// The thing being protected is the *sheet*. A shortcut sheet that lists a gesture nothing does is worse
/// than no sheet, because it is documentation a user trusts, and nothing else in the suite would notice:
/// the keys are handled in a page's <c>PreviewKeyDown</c> and the sheet is a list of strings, and nothing
/// ties the two together. The table is the tie.
/// </remarks>
[TestFixture]
public sealed class VisualCommandsTests
{
    [Test]
    public void No_gesture_is_claimed_by_two_commands_in_the_same_scope()
    {
        var clashes = VisualCommands.All
            .Where(command => command.Gesture.Length > 0)
            .GroupBy(
                command => $"{command.Scope} {command.Gesture.ToUpperInvariant()}",
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(command => command.Id))}")
            .ToList();

        Assert.That(
            clashes,
            Is.Empty,
            "two commands answering to one chord is the bug a shortcut sheet makes permanent: the user "
            + "presses it and cannot tell which one they got");
    }

    [Test]
    public void Every_command_has_an_id_a_title_and_a_gesture_or_a_deliberate_blank_one()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualCommands.All.Select(command => command.Id), Is.Unique, "ids address commands");
            Assert.That(
                VisualCommands.All.Where(command => string.IsNullOrWhiteSpace(command.Title)),
                Is.Empty,
                "a command with no title is a row of nothing in the palette and the sheet");

            // One command has no gesture on purpose - Reset has no shortcut in Part 18.3 - and it is the
            // only one allowed to.
            Assert.That(
                VisualCommands.All.Where(command => command.Gesture.Length == 0).Select(command => command.Id),
                Is.EqualTo(new[] { VisualCommands.StageReset }));
        });
    }

    [Test]
    public void A_gesture_is_spelled_the_same_way_everywhere_it_is_written()
    {
        // "ctrl+k" and "Ctrl+K" are the same chord written two ways, and a lookup on one of them misses.
        var odd = VisualCommands.All
            .Where(command => command.Gesture.Length > 0)
            .Where(command => command.Gesture != command.Gesture.Trim()
                              || command.Gesture.Any(char.IsWhiteSpace) && !command.Gesture.Contains(" then "))
            .Select(command => command.Gesture)
            .ToList();

        Assert.That(odd, Is.Empty, "gestures are written consistently or the lookup misses them");
    }

    [Test]
    public void Every_stage_gesture_names_a_command_the_stage_can_actually_perform()
    {
        // Part 18.3's stage row: F5 run, F10 step over, F11 step into, F9 breakpoint, Shift+F5 stop.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["F5"] = VisualCommands.StageRun,
            ["F10"] = VisualCommands.StageStep,
            ["F11"] = VisualCommands.StageStepInto,
            ["F9"] = VisualCommands.ToggleBreakpoint,
            ["Shift+F5"] = VisualCommands.StageStop,

            // Not in Part 18.3, which is the point: the transport's Pause is Part 10.3 and §18.6 records
            // that it was not built. It went on F6 because that is the free key between Run and Breakpoint,
            // and a transport whose pause had no chord would be a feature only its own button could reach.
            ["F6"] = VisualCommands.StagePause,
        };

        foreach (var (gesture, id) in expected)
        {
            var found = VisualCommands.ForGesture(gesture, CommandScope.Stage);
            Assert.That(found?.Id, Is.EqualTo(id), $"{gesture} is listed in Part 18.3 as something else");
        }
    }

    [Test]
    public void Pause_and_Resume_are_one_command_because_the_transport_shows_one_control()
    {
        // Two commands with two gestures would have to claim two chords for one state, and
        // No_gesture_is_claimed_by_two_commands_in_the_same_scope forbids exactly that. So the table names
        // one thing, and the sheet has to say so in words a user can act on.
        var pause = VisualCommands.Find(VisualCommands.StagePause);

        Assert.Multiple(() =>
        {
            Assert.That(pause, Is.Not.Null);
            Assert.That(pause!.Title, Does.Contain("Pause").IgnoreCase);
            Assert.That(pause.Title, Does.Contain("resume").IgnoreCase,
                "the sheet is read by somebody deciding which key to press, and \"Pause\" alone would not "
                + "tell them how to get out of the pause they just asked for");
            Assert.That(pause.SearchTerms, Does.Contain("resume"),
                "and the palette finds it by the other half of its name, because the button reads Resume "
                + "at the moment they would be searching for it");
            Assert.That(pause.Gesture, Is.EqualTo("F6"));
        });
    }

    [Test]
    public void The_rubber_band_has_a_keyboard_sibling_because_a_pointer_only_feature_is_a_defect()
    {
        // Part 18.1 gives selecting several blocks a drag gesture and §18.3 gives it no key at all.
        // §17.2 item 6 promises every operation is reachable without a pointer, so the chord has to exist
        // and has to be discoverable - which means it has to be in this table, not only in a key handler.
        var selectAll = VisualCommands.Find(VisualCommands.SelectAll);

        Assert.Multiple(() =>
        {
            Assert.That(selectAll?.Gesture, Is.EqualTo("Ctrl+A"));
            Assert.That(
                VisualCommands.ForGesture("Ctrl+A", CommandScope.Canvas)?.Id,
                Is.EqualTo(VisualCommands.SelectAll));
            Assert.That(
                selectAll?.SearchTerms,
                Does.Contain("rubber band"),
                "a user who has just been told about the drag searches for the word the drag is called in "
                + "the docs, not for \"select all\"");
        });
    }

    [Test]
    public void A_menu_that_lists_a_scope_reads_the_table_rather_than_its_own_list()
    {
        // Part 18.5's three context menus are the table's third reader, beside the palette and the sheet.
        // A menu written out as literal items is a menu that drifts: it would print "Undo Ctrl+Z" beside a
        // handler that had since been rebound, and nothing in the build or this suite would notice - which
        // is the exact defect the sheet's tests exist to prevent.
        Assert.Multiple(() =>
        {
            Assert.That(
                VisualCommands.InScope(CommandScope.Stage).Select(command => command.Id),
                Is.EqualTo(new[]
                {
                    VisualCommands.StageRun,
                    VisualCommands.StageStep,
                    VisualCommands.StageStepInto,
                    VisualCommands.StagePause,
                    VisualCommands.ToggleBreakpoint,
                    VisualCommands.StageStop,
                    VisualCommands.StageReset,
                }),
                "which is Part 18.5's stage menu, minus the clear-trace, clock and network items §18.6 "
                + "records as deliberately unreachable");

            Assert.That(
                VisualCommands.InScope(CommandScope.Canvas).Select(command => command.Id),
                Does.Contain(VisualCommands.SelectAll),
                "so a canvas menu that lists its scope cannot go stale against the table");
            Assert.That(
                VisualCommands.InScope(CommandScope.Stage).Where(command => command.Scope != CommandScope.Stage),
                Is.Empty,
                "and a scope never leaks another scope's commands in");
        });
    }

    [Test]
    public void Every_command_a_menu_would_offer_has_a_gesture_the_sheet_can_print()
    {
        // A menu item shows its own chord beside its name. Reset is the one command with a blank gesture,
        // and the sheet already knows that; what must not happen is a second one, because the menu would
        // then print an empty column where a key should be and the two views would disagree about why.
        var blank = VisualCommands.InScope(CommandScope.Stage)
            .Concat(VisualCommands.InScope(CommandScope.Canvas))
            .Where(command => command.Gesture.Length == 0)
            .Select(command => command.Id)
            .ToList();

        Assert.That(blank, Is.EqualTo(new[] { VisualCommands.StageReset }));
    }

    [Test]
    public void A_gesture_is_found_in_its_own_scope_and_not_in_another()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualCommands.ForGesture("F5", CommandScope.Stage)?.Id, Is.EqualTo(VisualCommands.StageRun));
            Assert.That(
                VisualCommands.ForGesture("F5", CommandScope.Canvas),
                Is.Null,
                "a stage key does not exist in the canvas, and pretending it does is how a canvas ends up "
                + "running a script while the user is editing it");
            Assert.That(VisualCommands.ForGesture("Ctrl+Z", CommandScope.Canvas)?.Id, Is.EqualTo(VisualCommands.Undo));
        });
    }

    [Test]
    public void A_chord_that_continues_into_typing_still_finds_its_command()
    {
        // "Ctrl+K then a name" is one chord and then text, so an exact-match lookup would not find it.
        var found = VisualCommands.ForGesture("Ctrl+K then add", CommandScope.Canvas);

        Assert.That(found?.Id, Is.EqualTo(VisualCommands.OpenPalette));
    }

    [Test]
    public void An_unknown_gesture_finds_nothing_rather_than_the_nearest_thing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualCommands.ForGesture("Ctrl+Shift+Q", CommandScope.Canvas), Is.Null);
            Assert.That(VisualCommands.ForGesture("", CommandScope.Canvas), Is.Null,
                "an empty gesture is not a shortcut, and matching the empty command to it would make every "
                + "unbound key press Reset");
        });
    }

    [Test]
    public void An_empty_search_offers_every_command_in_table_order()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisualCommands.Search("   "), Is.EqualTo(VisualCommands.All));
            Assert.That(VisualCommands.Search(null), Is.EqualTo(VisualCommands.All));
        });
    }

    [Test]
    public void A_search_matches_the_three_tiers_a_palette_needs_and_ignores_case()
    {
        Assert.Multiple(() =>
        {
            // Prefix, and nothing else: nothing else has "step" anywhere in it.
            Assert.That(
                VisualCommands.Search("step").Select(command => command.Id),
                Is.EqualTo(new[] { VisualCommands.StageStep, VisualCommands.StageStepInto }),
                "a title that starts with the query, and only those");

            // Substring: "Add block" does not start with "bloc", but it contains it.
            Assert.That(
                VisualCommands.Search("bloc").Select(command => command.Id),
                Is.EquivalentTo(new[]
                {
                    VisualCommands.AddBlock,
                    VisualCommands.FocusPaletteSearch,
                    VisualCommands.Delete,
                    VisualCommands.Duplicate,
                    VisualCommands.Copy,
                    VisualCommands.Cut,
                    VisualCommands.Paste,
                    VisualCommands.ExportBlock,
                    VisualCommands.ImportBlock,
                    VisualCommands.SelectAll,
                    VisualCommands.StageStep,
                    VisualCommands.StageStepInto,
                }),
                "and a title that merely contains it. \"Search blocks\" is here and \"Stop\" is not, which "
                + "is the difference between matching a word and matching a letter. Every clipboard command "
                + "is here too, which is the point of the substring tier: somebody who types \"bloc\" "
                + "wanting the clipboard is not made to remember which of the five says so in its title. "
                + "\"Select every block\" is here for the same reason and by the same accident - it has "
                + "nothing to do with the clipboard, and its keyword \"rubber band\" is what a user who "
                + "wants the selection actually types");

            // Keyword: no command is *called* cheatsheet.
            Assert.That(
                VisualCommands.Search("CheatSheet").Select(command => command.Id),
                Is.EqualTo(new[] { VisualCommands.ShowShortcuts }),
                "and a word only a keyword carries, typed with capitals, because the comparison is Ordinal");
        });
    }

    [Test]
    public void Every_word_of_a_multi_word_query_has_to_match_something()
    {
        // ORing the words would offer "Save" when the user typed "zoom save", which is a command neither
        // word named.
        Assert.That(
            VisualCommands.Search("zoom nonexistentword"),
            Is.Empty,
            "a palette that ORs its words hands back a command the user did not ask for");
    }

    [Test]
    public void A_search_finds_a_command_by_a_word_only_its_keywords_carry()
    {
        Assert.That(
            VisualCommands.Search("cheatsheet").Select(command => command.Id),
            Does.Contain(VisualCommands.ShowShortcuts),
            "\"shortcut sheet\" does not contain \"cheatsheet\"; the keyword does, which is what keywords "
            + "are for");
    }

    [Test]
    public void A_command_that_needs_a_selection_says_so()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                VisualCommands.Find(VisualCommands.Delete)?.NeedsSelection,
                Is.True,
                "delete with nothing selected is the gesture a user presses by accident");
            Assert.That(VisualCommands.Find(VisualCommands.StageRun)?.NeedsSelection, Is.False);
            Assert.That(VisualCommands.Find("canvas.nothing")?.NeedsSelection, Is.Null);
        });
    }

    [Test]
    public void Every_command_the_canvas_offers_is_reachable_from_the_palette()
    {
        // A palette that cannot find a command the keys do is a palette with a hole in it, and the hole
        // is invisible until a user without a mouse hits it.
        var canvas = VisualCommands.All
            .Where(command => command.Scope is CommandScope.Canvas or CommandScope.Palette)
            .Select(command => command.Id)
            .ToList();

        foreach (var id in canvas)
        {
            Assert.That(
                VisualCommands.Search(VisualCommands.Find(id)!.Title).Select(command => command.Id),
                Does.Contain(id),
                $"{id} is in the table but its own title does not find it");
        }
    }
}