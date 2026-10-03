using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.Core.Visual.Commands;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The command palette: a search box over <see cref="VisualCommands"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A view model because the palette is the one place on this page where the *contents* are derived
/// rather than projected: typing re-ranks, and a collection rebuilt on every keystroke is what WPF's
/// <c>ItemsControl</c> is happiest with.
/// </para>
/// <para>
/// The commands themselves come from Core, and so does the ranking. This class holds a query and a list.
/// If it also held the rule for which command comes first, the shortcut sheet and the key handler would
/// be reading a different ordering from the palette the user is looking at.
/// </para>
/// </remarks>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private string _query = string.Empty;
    private VisualCommand? _selected;

    public CommandPaletteViewModel()
    {
        Results = [.. VisualCommands.Search(string.Empty)];
        _selected = Results.FirstOrDefault();
    }

    /// <summary>What the user has typed.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                Results = [.. VisualCommands.Search(value)];
                Selected = Results.FirstOrDefault();
                OnPropertyChanged(nameof(Footer));
            }
        }
    }

    /// <summary>The commands that match, best first.</summary>
    public ObservableCollection<VisualCommand> Results { get; private set; }

    /// <summary>The command Enter will run.</summary>
    public VisualCommand? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>The line under the list, saying how to use it rather than repeating it.</summary>
    public string Footer =>
        Results.Count == 0
            ? "Nothing matches. Fewer words find more."
            : "Enter runs the first one, the arrows move, Esc closes.";
}