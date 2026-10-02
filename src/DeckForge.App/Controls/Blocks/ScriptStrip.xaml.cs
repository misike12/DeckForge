using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The list of scripts in the open target, and the procedures below it.
/// </summary>
/// <remarks>
/// <para>
/// Read-only until Phase 7, which is when adding, renaming, deleting and re-hatting arrived. The strip
/// exists from the first phase rather than later because the sample has eleven scripts: a canvas with
/// eleven scripts and no list of them is a wall, and the layout in Part 9.2 is only reviewable with the
/// strip in place.
/// </para>
/// <para>
/// Everything here is a handler rather than a command binding, because the rows live inside data
/// templates and each one holds a script or a procedure rather than the editor. The row is named by
/// <c>Tag</c> for the same reason the inspector's steppers are: a binding would have to reach up out of the
/// template to find the page's view model, and the reach is exactly what makes such a binding hard to
/// follow later.
/// </para>
/// </remarks>
public partial class ScriptStrip : UserControl
{
    public ScriptStrip() => InitializeComponent();

    /// <summary>The editor this strip is part of.</summary>
    /// <remarks>
    /// Resolved from the inherited <see cref="DataContext"/> rather than passed in, because the strip is
    /// declared inside the workspace's markup and has no constructor anyone could reach. The null check
    /// matters: a designer or a previewer may build this without a view model, and a NullReference here
    /// would be a crash in a preview rather than an empty list.
    /// </remarks>
    private VisualEditorViewModel? Editor => DataContext as VisualEditorViewModel;

    /// <summary>Commits a rename when the name box loses focus.</summary>
    /// <remarks>
    /// Lost focus rather than a key press, so a rename also completes when the user clicks straight to the
    /// next thing. The empty name is ignored rather than applied: the editor refuses it, and the refusal
    /// would put a sentence in the message line for a click that was only ever about to type.
    /// </remarks>
    private void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ScriptViewModel script } element
            && element is TextBox box
            && Editor is { } editor)
        {
            editor.RenameScript(script, box.Text);
        }
    }

    /// <summary>Commits a rename on Enter, and gives the focus back so the row does not stay in edit mode.</summary>
    private void Name_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not FrameworkElement { Tag: ScriptViewModel script, } element
            || element is not TextBox box
            || Editor is not { } editor)
        {
            return;
        }

        editor.RenameScript(script, box.Text);
        e.Handled = true;
        Keyboard.Focus(box);
    }

    /// <summary>
    /// Opens the row's menu: the hats a script may start with, and its delete.
    /// </summary>
    /// <remarks>
    /// Built in code rather than declared in markup, because the hats come from the catalogue. A markup
    /// submenu would be a second list of hats to keep in step with the first — and the first list is the
    /// one the emitter reads, so the two could disagree and the user would pick a hat the page accepts and
    /// the document does not have.
    /// </remarks>
    private void Hat_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScriptViewModel script } || Editor is not { } editor)
        {
            return;
        }

        var menu = new ContextMenu();

        var hats = new MenuItem { Header = "Starts with" };
        hats.Items.Add(new MenuItem
        {
            Header = script.HatLabel,
            IsCheckable = true,
            IsChecked = true,
            IsEnabled = false,
            // Disabled rather than absent: the current hat has to be visible or the user cannot tell where
            // they are. A greyed tick that says nothing would be worse than saying what it is.
            ToolTip = "What this script starts with today",
        });

        foreach (var hat in editor.HatChoices)
        {
            var choice = hat;
            var item = new MenuItem { Header = choice.Label, ToolTip = choice.Summary };

            if (choice.Kind == script.Script.Hat.Kind)
            {
                item.IsEnabled = false;
            }
            else
            {
                item.Click += (_, _) => editor.SetScriptHat(script, choice.Kind);
            }

            hats.Items.Add(item);
        }

        menu.Items.Add(hats);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Delete script" });

        var remove = (MenuItem)menu.Items[^1];
        remove.Click += (_, _) => editor.DeleteScript(script);

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Shows a procedure in the My Blocks panel.</summary>
    private void Procedure_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcedureViewModel procedure } && Editor is { } editor)
        {
            editor.SelectProcedure(procedure);
            e.Handled = true;
        }
    }

    /// <summary>Adds a script with the first hat the target offers.</summary>
    /// <remarks>
    /// The hat is chosen afterwards from the row's menu rather than in a dialog before the script exists,
    /// so adding a script is one click and changing what starts it is one right-click — and the strip's
    /// Add button does not have to open anything at all.
    /// </remarks>
    private void AddScript_Click(object sender, RoutedEventArgs e) => Editor?.AddScriptCommand.Execute(null);

    /// <summary>Adds a procedure and opens it in the panel.</summary>
    private void AddProcedure_Click(object sender, RoutedEventArgs e) => Editor?.AddProcedureCommand.Execute(null);
}