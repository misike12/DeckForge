using System.Windows.Controls;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual.Commands;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Part 18.5's three context menus, built from Core's command table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The tile, the canvas and the stage</strong> — which are the three Part 18.5 names and the three
/// §18.6 records as missing. A fourth menu is specified too, in §18.1's pointer table rather than §18.5's:
/// "right click | palette block | add to selected script and open docs". It is not built here, because the
/// palette has no script to add to until the user has picked a drop zone, and a menu that offers
/// "Add to selected script" with nothing selected is one of the two ways this design says a menu should
/// not be written. That is a gap, not a decision, and it is named rather than hidden.
/// </para>
/// <para>
/// Every item is a row from <see cref="VisualCommands"/> rather than a literal. The palette and the
/// shortcut sheet already read that table, which is what makes the sheet honest — and a menu written out
/// as literal items is a fourth reader that would drift: it would print "Undo Ctrl+Z" beside a handler that
/// had since been rebound, and nothing in the build or the suite would notice. Reading the table is also
/// what keeps the *sheet* honest from the other side, because the gesture printed beside a menu item is the
/// same string the sheet prints and the key handler looks up.
/// </para>
/// <para>
/// Built in code rather than declared in markup, for the reason <c>ScriptStrip</c>'s hat menu is: the item
/// set is a filtered view of a table, and a markup menu would be a second list to keep in step with the
/// first.
/// </para>
/// </remarks>
internal static class VisualMenus
{
    /// <summary>
    /// The menu for a block tile.
    /// </summary>
    /// <param name="block">The block the menu was opened on.</param>
    /// <param name="vm">The editor the commands run against.</param>
    /// <param name="dispatch">Runs one command by id, the same dispatcher the keys and the palette use.</param>
    /// <remarks>
    /// <para>
    /// Only the canvas commands that act on a selection. Part 18.5's tile menu also lists add comment,
    /// disable, wrap, unwrap and extract-as-procedure; none of those is in the table, so putting them here
    /// would mean inventing gestures for five new commands and making the sheet advertise keys nobody has
    /// pressed. They are reachable instead from the inspector and the keyboard, and the omission is a
    /// known gap rather than a claim that the menu is complete.
    /// </para>
    /// <para>
    /// The block is named in a disabled first row and nothing here changes the selection — the caller does,
    /// before it calls. A builder that also selected would mean every right-click rebuilt the inspector,
    /// which is right exactly once and surprising the other time.
    /// </para>
    /// </remarks>
    public static ContextMenu ForTile(
        BlockNodeViewModel block,
        VisualEditorViewModel vm,
        Action<string> dispatch)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(vm);

        return Menu(
            [
                VisualCommands.Duplicate,
                VisualCommands.Copy,
                VisualCommands.Cut,
                VisualCommands.Paste,
                Gap(),
                VisualCommands.Delete,
                Gap(),
                VisualCommands.ExportBlock,
                Gap(),
                VisualCommands.OpenDocs,
            ],
            vm,
            dispatch,
            Header: block.Title,
            block: block,
            subtitle: "Right-click also selects this block, so the commands below act on it.");
    }

    /// <summary>
    /// The menu for empty canvas.
    /// </summary>
    /// <param name="vm">The editor the commands run against.</param>
    /// <param name="dispatch">Runs one command by id, the same dispatcher the keys and the palette use.</param>
    /// <remarks>
    /// Part 18.5's canvas menu — new script, select all, arrange, tidy, zoom to fit, export, add variable,
    /// add list, define procedure — has a button or a strip control for nearly every item already: the
    /// header carries the two exports, the strip carries new script and define procedure, and the zoom row
    /// carries zoom to fit. What is here is the canvas *command* set, which is the part with no button and
    /// which the palette can reach but a right-click could not.
    /// </remarks>
    public static ContextMenu ForCanvas(VisualEditorViewModel vm, Action<string> dispatch)
    {
        ArgumentNullException.ThrowIfNull(vm);

        return Menu(
            [
                VisualCommands.Undo,
                VisualCommands.Redo,
                Gap(),
                VisualCommands.Duplicate,
                VisualCommands.Copy,
                VisualCommands.Cut,
                VisualCommands.Paste,
                VisualCommands.Delete,
                Gap(),
                VisualCommands.SelectAll,
                VisualCommands.PickUpOrDrop,
                VisualCommands.Cancel,
                Gap(),
                VisualCommands.ExportBlock,
                VisualCommands.ImportBlock,
                Gap(),
                VisualCommands.OpenPalette,
            ],
            vm,
            dispatch,
            subtitle: "Every item here also has a key. The shortcut sheet lists them (Ctrl+/).");
    }

    /// <summary>
    /// The menu for the stage and its trace.
    /// </summary>
    /// <param name="stage">The stage the commands run against.</param>
    /// <param name="vm">The editor, for whether anything is selected.</param>
    /// <param name="dispatch">Runs one command by id, the same dispatcher the keys and the palette use.</param>
    /// <remarks>
    /// <para>
    /// Every stage command, in the table's own order, which is the order a transport is read in. Part
    /// 18.5's stage menu also lists clear trace, advance the clock, and allow real network. The first two
    /// are reset, which is two rows above and does the same job with a button beside it; the third is
    /// §18.6's deliberate absence and §27.1 explains why a control that turns the sandbox into the
    /// internet is a control nobody should press by accident.
    /// </para>
    /// <para>
    /// Pause reads "Pause or resume" because that is what the table calls the command, and the button and
    /// the F6 key both toggle. Printing "Pause" here and offering Resume next would be two names for one
    /// state, and the table's own rule against two commands sharing a gesture is the reason there is only
    /// one.
    /// </para>
    /// </remarks>
    public static ContextMenu ForStage(StageViewModel stage, VisualEditorViewModel vm, Action<string> dispatch)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(vm);

        return Menu(
            [.. VisualCommands.InScope(CommandScope.Stage).Select(command => command.Id)],
            vm,
            dispatch,
            subtitle: "The same commands the transport row offers, and the same keys.");
    }

    /// <summary>
    /// Turns a list of ids and gaps into a menu, refusing anything the table does not contain.
    /// </summary>
    /// <param name="ids">Command ids, with <see cref="Gap"/> for a separator.</param>
    /// <param name="vm">The editor, for the needs-selection rule.</param>
    /// <param name="dispatch">Runs one command by id.</param>
    /// <param name="Header">A disabled first row naming what was clicked, when there is one.</param>
    /// <param name="subtitle">A disabled last row saying where else these are available.</param>
    private static ContextMenu Menu(
        IReadOnlyList<string> ids,
        VisualEditorViewModel vm,
        Action<string> dispatch,
        string? Header = null,
        BlockNodeViewModel? block = null,
        string? subtitle = null)
    {
        var menu = new ContextMenu();

        if (Header is not null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Header,
                IsEnabled = false,

                // Disabled rather than absent, so a user can see which block they are about to act on. A
                // greyed row that says nothing is worse than saying what it is - and its tooltip is the
                // block's own summary, which is the one thing about it the menu cannot show.
                ToolTip = block is null ? null : block.Summary,
            });
            menu.Items.Add(new Separator());
        }

        foreach (var id in ids)
        {
            if (id == Gap())
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var command = VisualCommands.Find(id);
            if (command is null)
            {
                // Not thrown and not silently skipped. An id that has left the table is a wiring mistake, and
                // a menu that quietly omits a row is how the user finds out - by pressing a chord that does
                // nothing, having read this list.
                throw new InvalidOperationException(
                    $"'{id}' is offered by a context menu but is not in VisualCommands.");
            }

            var item = new MenuItem
            {
                Header = command.Title,
                InputGestureText = command.Gesture,

                // Greyed rather than hidden, because a command that needs a selection and has none is the
                // common case on a canvas menu, and a row that appears and disappears as the pointer moves
                // is harder to aim at than one that is always there and says why.
                IsEnabled = !command.NeedsSelection || vm.HasSelection,
                ToolTip = command.NeedsSelection && !vm.HasSelection
                    ? "Nothing is selected."
                    : Describe(command),
            };

            var run = command.Id;
            item.Click += (_, _) => dispatch(run);

            menu.Items.Add(item);
        }

        if (subtitle is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = subtitle, IsEnabled = false });
        }

        return menu;
    }

    /// <summary>What the tooltip says about one command, without inventing anything the table lacks.</summary>
    private static string Describe(VisualCommand command)
    {
        var gesture = command.Gesture.Length > 0 ? command.Gesture : "no shortcut";
        return $"{command.Title} — {gesture}. Search for it in the command palette (Ctrl+K).";
    }

    /// <summary>A separator, as a value rather than a null.</summary>
    /// <remarks>
    /// A null in the id list would read as "skip", which is the same thing but says nothing in review — and
    /// a menu's grouping is the one thing a reviewer cannot check by reading what it does.
    /// </remarks>
    private static string Gap() => "\u0000gap";
}
