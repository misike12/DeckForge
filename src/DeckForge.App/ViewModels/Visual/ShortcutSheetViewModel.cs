using System.Collections.ObjectModel;
using System.Windows.Controls;
using DeckForge.Core.Visual.Commands;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The shortcut sheet: <see cref="VisualCommands"/> grouped by scope, in the order the table declares.
/// </summary>
/// <remarks>
/// A projection and nothing more, which is the whole point. The sheet is the one piece of this page that
/// is pure documentation, so it must be impossible for it to drift from what the keys do: there is no
/// text here to fall out of date, because every row comes from the table the key handler dispatches on.
/// </remarks>
public sealed class ShortcutSheetViewModel
{
    /// <summary>One scope's commands, with its heading.</summary>
    /// <param name="Scope">The scope.</param>
    /// <param name="Commands">Its commands, in table order.</param>
    public sealed record Section(CommandScope Scope, IReadOnlyList<VisualCommand> Commands)
    {
        /// <summary>What the section is called.</summary>
        public string Heading => Scope switch
        {
            CommandScope.Palette => "Palette",
            CommandScope.Canvas => "Canvas",
            CommandScope.Stage => "Stage",
            _ => "Anywhere",
        };

        /// <summary>One line about what this scope's keys do, for the section's footnote.</summary>
        public string Note => Scope switch
        {
            CommandScope.Palette => "The palette takes the keyboard while its search box has focus.",
            CommandScope.Canvas => "A focused text field wins over every single-key shortcut; modified shortcuts still fire.",
            CommandScope.Stage => "The stage keys run the simulated host. Nothing here touches a real deck.",
            _ => "These work whatever has focus, including a text field.",
        };
    }

    /// <summary>Every section, in reading order.</summary>
    public ObservableCollection<Section> Sections { get; } = [];

    public ShortcutSheetViewModel()
    {
        foreach (var scope in new[]
                 {
                     CommandScope.Palette,
                     CommandScope.Canvas,
                     CommandScope.Stage,
                     CommandScope.Application,
                 })
        {
            var commands = VisualCommands.All.Where(command => command.Scope == scope).ToList();
            if (commands.Count > 0)
            {
                Sections.Add(new Section(scope, commands));
            }
        }
    }

    /// <summary>
    /// The one line the sheet leads with.
    /// </summary>
    /// <remarks>
    /// The scope rule, because it is the rule a user cannot see and breaks most often: a single-key
    /// shortcut that fires while a text field has focus is how "press Delete to remove this block" turns
    /// into "press Delete to lose the sentence I was typing".
    /// </remarks>
    public string Preamble =>
        "Everything on this page works from the keyboard. Where two things want the same key, the thing "
        + "you are typing in wins — except a shortcut with Ctrl or a function key, which always fires.";
}