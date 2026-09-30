using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.App.Controls.Blocks;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>One row of the category rail.</summary>
public sealed class CategoryRowViewModel
{
    public CategoryRowViewModel(BlockCategoryDescriptor descriptor)
    {
        Descriptor = descriptor;
        Count = BlockCatalog.InCategory(descriptor.Category).Count;
    }

    /// <summary>The catalogue's own row, so a new category needs nothing added here.</summary>
    public BlockCategoryDescriptor Descriptor { get; }

    /// <summary>The category's display name.</summary>
    public string Name => Descriptor.Name;

    /// <summary>A <c>SymbolRegular</c> member name, which the markup test checks exists.</summary>
    public string Glyph => Descriptor.Glyph;

    /// <summary>One line about what the category holds, shown under the rail's heading.</summary>
    public string Summary => Descriptor.Summary;

    /// <summary>How many blocks it has.</summary>
    public int Count { get; }

    /// <summary>The theme key for this category's colour.</summary>
    public string FillKey => BlockTheme.FillKey(Descriptor.Category);

    /// <summary>The theme key for the text on that colour.</summary>
    public string InkKey => BlockTheme.InkKey(Descriptor.Category);
}

/// <summary>
/// The category rail and the palette beside it.
/// </summary>
/// <remarks>
/// <para>
/// A hundred and fifty blocks in one flat list is a haystack, so Part 9.7 asks for search and Part 9.2 for
/// a rail. Both read <see cref="BlockCatalog"/>, which is in Core and tested there: matching a row's id
/// as well as its visible text means a user who knows <c>control.repeat</c> can type it, and the palette
/// is then a projection of one function rather than a second list somebody has to keep in step.
/// </para>
/// <para>
/// Every row is a real block, built by <see cref="BlockFactory.Preview"/> rather than described. Part
/// 9.2 asks for "a live miniature of every block" — which only means something if the miniature is drawn
/// by the same code that draws the block, or the palette is a promise the canvas does not keep.
/// </para>
/// </remarks>
public sealed partial class PaletteViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CategoryRowViewModel? _selectedCategory;

    public PaletteViewModel()
    {
        Categories = new ObservableCollection<CategoryRowViewModel>(BlockCatalog.Categories.Select(category => new CategoryRowViewModel(category)));

        _selectedCategory = Categories.FirstOrDefault();
        Rows = [];
        Refilter();
    }

    /// <summary>The rail, in rail order.</summary>
    public ObservableCollection<CategoryRowViewModel> Categories { get; }

    /// <summary>The palette's rows: one real block each.</summary>
    public ObservableCollection<BlockNodeViewModel> Rows { get; }

    /// <summary>Whether the palette has anything to show.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Whether there is anything to say about an empty palette.</summary>
    public bool IsEmpty => !HasRows;

    /// <summary>How many blocks the palette is showing, for its heading.</summary>
    public string CountText => $"{Rows.Count} block{(Rows.Count == 1 ? string.Empty : "s")}";

    /// <summary>What the palette says when it has nothing.</summary>
    /// <remarks>
    /// Two different sentences for two different problems: an empty category is fine and needs no
    /// action, while an empty search means the block the user wanted is not in the catalogue under that
    /// name. One message for both would be wrong half the time.
    /// </remarks>
    public string EmptyText => string.IsNullOrWhiteSpace(SearchText)
        ? "This category has no blocks."
        : $"Nothing matches \"{SearchText}\". Try part of the label, or an id such as control.repeat.";

    /// <summary>Whether the palette is showing every category rather than one.</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

partial void OnSearchTextChanged(string value) => Refilter();

    partial void OnSelectedCategoryChanged(CategoryRowViewModel? value)
    {
        // Refilter clears the selection itself when a search is running, and that must not come back
        // round as a second pass through here.
        if (!_clearingSelection)
        {
            Refilter();
        }
    }

    /// <summary>Re-reads the catalogue for the current category and search.</summary>
    /// <remarks>
    /// A search spans every category, because a user who types "notify" does not know or care which rail
    /// entry holds it, and search is one of the ways this is meant to beat Scratch's fixed rails. It also
    /// clears the rail's highlight, since leaving one selected would claim the results are still that
    /// category's.
    /// </remarks>
    private void Refilter()
    {
        if (IsSearching && SelectedCategory is not null)
        {
            _clearingSelection = true;
            SelectedCategory = null;
            _clearingSelection = false;
        }

        var descriptors = IsSearching
            ? BlockCatalog.Search(SearchText)
            : SelectedCategory is null
                ? []
                : BlockCatalog.InCategory(SelectedCategory.Descriptor.Category);

        Rows.Clear();
        foreach (var descriptor in descriptors)
        {
            Rows.Add(new BlockNodeViewModel(BlockFactory.Preview(descriptor)));
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsSearching));
    }

    private bool _clearingSelection;
}