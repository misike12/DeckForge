using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.App.Controls.Blocks;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// One block as the canvas draws it.
/// </summary>
/// <remarks>
/// <para>
/// A view model over one <see cref="Block"/> and its catalogue row, with no state of its own worth the
/// name. That is deliberate and is the same decision Part 6.2.1 took for the model: a hundred and fifty
/// blocks cannot each own a view model, and the catalogue already says what every one of them is. The
/// only thing added here is the label split, which is data rather than behaviour and lives in Core
/// (<see cref="BlockLabel"/>) so it can be tested without a window.
/// </para>
/// <para>
/// Nothing is editable yet. Phase 3 is rendering; editing arrives in Phase 5 with undo, and a field that
/// mutates the document before undo exists loses work.
/// </para>
/// </remarks>
public sealed partial class BlockNodeViewModel : ObservableObject
{
    public BlockNodeViewModel(Block block)
    {
        Block = block;
        Descriptor = BlockCatalog.Find(block.Kind);

        Shape = Descriptor?.Shape ?? BlockShape.Placeholder;
        Category = Descriptor?.Category ?? BlockCategory.Control;
        Kind = block.Kind;
        Id = block.Id;

        Title = Descriptor is null ? block.Kind : BlockLabel.PreviewText(Descriptor, Services.BlockText.Current);
        Summary = Descriptor?.Summary ?? "A block this build does not know.";
        Sdk = Descriptor?.Sdk.VerifiedAgainst ?? string.Empty;
        DocsPath = Descriptor?.DocsPath;

        // Slots and menus first: the label's pieces are looked up by name in both, and a label built
        // before them resolves every hole to nothing.
        Slots = BuildSlots();
        Menus = BuildMenus();
        LabelParts = BuildLabelParts();
        Bodies = BuildBodies();
    }

    /// <summary>Whether the block is the selected one.</summary>
    /// <remarks>
    /// Raised rather than set by the tile: the tile knows a click happened and the page knows which
    /// block is selected, and a tile that reported the click itself would have to reach past its own
    /// DataContext to find out. Part 9.7's "click a diagnostic to select the block" and Part 9.5's
    /// "focus a tile" both hang off this one flag.
    /// </remarks>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The document node this draws.</summary>
    public Block Block { get; }

    /// <summary>
    /// Whether this row is a palette miniature rather than a block in the document.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="BlockFactory.Preview"/> callers, and the only thing that separates the two.
    /// Dragging from a palette row means "make a new block of this kind"; dragging from the canvas means
    /// "move this one", and the second needs an id to find. Without this the drag controller has to be
    /// told which surface it is on, and the palette rows and the canvas tiles are otherwise the same type
    /// on purpose — a palette that draws blocks one way and the canvas another is a palette that lies
    /// about what dropping will produce.
    /// </remarks>
    public bool IsPaletteRow { get; init; }

    /// <summary>Its catalogue row, or null for a kind from a newer build.</summary>
    public BlockDescriptor? Descriptor { get; }

    /// <summary>Which silhouette to draw.</summary>
    public BlockShape Shape { get; }

    /// <summary>Which palette category's colours to draw it in.</summary>
    public BlockCategory Category { get; }

    /// <summary>The catalogue id.</summary>
    public string Kind { get; }

    /// <summary>The block's stable id, which a diagnostic and a breakpoint both name.</summary>
    public string Id { get; }

    /// <summary>The label with its markers removed, for tooltips and for search results.</summary>
    public string Title { get; }

    /// <summary>One line about what the block does.</summary>
    public string Summary { get; }

    /// <summary>The SDK member the block's mapping was verified against, or empty.</summary>
    public string Sdk { get; }

    /// <summary>The offline docs page for this block, or null.</summary>
    public string? DocsPath { get; }

    /// <summary>Whether the catalogue has a row for this kind.</summary>
    public bool IsKnown => Descriptor is not null;

    /// <summary>Whether the block is switched off, which the emitter and the interpreter both honour.</summary>
    public bool IsDisabled => Block.Disabled;

    /// <summary>
    /// Whether this block stops the interpreter.
    /// </summary>
    /// <remarks>
    /// Written by the editor rather than read from the interpreter, so the tile is a plain projection and
    /// the stage owns the set. A tile that queried the interpreter itself would need a reference to it, and
    /// the canvas would stop being a view of the document.
    /// </remarks>
    [ObservableProperty]
    private bool _isBreakpoint;

    /// <summary>
    /// Whether the stage is standing on this block right now.
    /// </summary>
    /// <remarks>
    /// Part 10.3's pulsing tile, as state rather than as an animation. A WPF pulse needs a
    /// <c>Storyboard</c> per tile and a timer to drive it; a flag the tile already watches costs one
    /// comparison in <c>OnRender</c> and cannot get out of step with the interpreter, which is the only
    /// thing a pulse has to be in step with.
    /// </remarks>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>Whether the palette row's block is pinned, for the star beside it.</summary>
    /// <remarks>
    /// Written by the palette when the memory changes rather than read from it per row, because a row that
    /// queried the memory on every paint would be asking a question sixty times a second and a
    ///  notification would still be needed for the other half of the problem.
    /// </remarks>
    [ObservableProperty]
    private bool _isFavourite;

    /// <summary>Whether the block carries a comment.</summary>
    public bool HasComment => !string.IsNullOrWhiteSpace(Block.Comment);

    /// <summary>The comment, if there is one.</summary>
    public string Comment => Block.Comment ?? string.Empty;

    /// <summary>Whether the block wraps anything.</summary>
    public bool HasBodies => Bodies.Count > 0;

    /// <summary>The label's pieces, in order: words, holes and menus.</summary>
    public IReadOnlyList<LabelPartViewModel> LabelParts { get; }

    /// <summary>The block's slots, in catalogue order.</summary>
    public ObservableCollection<SlotViewModel> Slots { get; }

    /// <summary>The block's inline dropdowns.</summary>
    public ObservableCollection<MenuViewModel> Menus { get; }

    /// <summary>The block's wrapped bodies.</summary>
    public ObservableCollection<BodyViewModel> Bodies { get; }

    // ---- theme keys ----------------------------------------------------------------------------------

    /// <summary>The theme key for this block's fill.</summary>
    public string FillKey => BlockTheme.FillKey(Category);

    /// <summary>The theme key for this block's border.</summary>
    public string StrokeKey => BlockTheme.StrokeKey(Category);

    /// <summary>The theme key for the text on this block.</summary>
    public string InkKey => BlockTheme.InkKey(Category);

    // ---- layout the tile uses -----------------------------------------------------------------------

    /// <summary>How tall the label's row is.</summary>
    /// <remarks>
    /// A constant, matching <see cref="BlockOutline.HeaderHeight"/>, because that is where a container's
    /// mouth starts. A header that grew with the tile would move the mouth and the two would stop
    /// agreeing — the tile would be drawn one way and laid out another.
    /// </remarks>
    public double HeaderHeight => BlockOutline.HeaderHeight;

    /// <summary>The label's inset inside the tile.</summary>
    /// <remarks>
    /// The top inset is the notch's depth, so the words clear the notch rather than sitting on it, and
    /// the sides are <see cref="BlockMetrics.TilePaddingX"/>.
    /// </remarks>
    public Thickness HeaderMargin => new(
        BlockMetrics.TilePaddingX,
        BlockOutline.NotchDepth,
        BlockMetrics.TilePaddingX,
        0);

    /// <summary>Where the wrapped bodies sit inside the tile.</summary>
    /// <remarks>
    /// The left inset is the mouth's own indent, so a statement in a body lines up with the tab above
    /// it, and the bottom inset is the footer bar plus the tab. Zero for a block with no bodies, or a
    /// plain statement would carry twenty-one pixels of nothing underneath.
    /// </remarks>
    public Thickness BodyMargin => HasBodies
        ? new Thickness(BlockOutline.ArmWidth, 0, 0, BlockOutline.FooterHeight + BlockOutline.NotchDepth)
        : new Thickness(0, 0, 0, 0);

    // ---- building ------------------------------------------------------------------------------------

    private IReadOnlyList<LabelPartViewModel> BuildLabelParts()
    {
        if (Descriptor is null)
        {
            return [new LabelPartViewModel(BlockLabelRunKind.Text, Kind, null, null)];
        }

        var parts = new List<LabelPartViewModel>();

        foreach (var run in BlockLabel.Plan(Descriptor, Services.BlockText.Current))
        {
            switch (run.Kind)
            {
                case BlockLabelRunKind.Slot:
                    parts.Add(new LabelPartViewModel(
                        run.Kind,
                        string.Empty,
                        Slots.FirstOrDefault(slot => slot.Name == run.Name),
                        null));
                    break;

                case BlockLabelRunKind.Menu:
                    parts.Add(new LabelPartViewModel(
                        run.Kind,
                        string.Empty,
                        null,
                        Menus.FirstOrDefault(menu => menu.Name == run.Name)));
                    break;

                default:
                    parts.Add(new LabelPartViewModel(run.Kind, run.Text, null, null));
                    break;
            }
        }

        return parts;
    }

    private ObservableCollection<SlotViewModel> BuildSlots()
    {
        var slots = new ObservableCollection<SlotViewModel>();

        foreach (var descriptor in Descriptor?.Slots ?? [])
        {
            slots.Add(new SlotViewModel(
                this,
                descriptor,
                Block.Inputs.TryGetValue(descriptor.Name, out var input) ? input : null));
        }

        return slots;
    }

    private ObservableCollection<MenuViewModel> BuildMenus()
    {
        var menus = new ObservableCollection<MenuViewModel>();

        foreach (var descriptor in Descriptor?.Menus ?? [])
        {
            menus.Add(new MenuViewModel(descriptor, Block.Field(descriptor.Name), Descriptor, Services.BlockText.Current));
        }

        return menus;
    }

    private ObservableCollection<BodyViewModel> BuildBodies()
    {
        var bodies = new ObservableCollection<BodyViewModel>();

        foreach (var descriptor in Descriptor?.Bodies ?? [])
        {
            bodies.Add(new BodyViewModel(descriptor.Name, descriptor.Label, Block.TryBody(descriptor.Name)));
        }

        return bodies;
    }
}

/// <summary>One piece of a block's label, ready to draw.</summary>
/// <remarks>
/// The three cases of <see cref="BlockLabelRun"/> flattened into one list so the tile can be a single
/// <c>ItemsControl</c> with a template that picks per item. Splitting them into three collections and
/// three panels is how a label ends up with its words, its holes and its menus on different rows.
/// </remarks>
public sealed class LabelPartViewModel
{
    public LabelPartViewModel(BlockLabelRunKind kind, string text, SlotViewModel? slot, MenuViewModel? menu)
    {
        Kind = kind;
        Text = text;
        Slot = slot;
        Menu = menu;
    }

    /// <summary>Which of the three this is.</summary>
    public BlockLabelRunKind Kind { get; }

    /// <summary>The words, when this is words.</summary>
    public string Text { get; }

    /// <summary>The hole, when this is a hole.</summary>
    public SlotViewModel? Slot { get; }

    /// <summary>The dropdown, when this is a dropdown.</summary>
    public MenuViewModel? Menu { get; }
}