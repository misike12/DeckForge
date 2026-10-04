using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The block tile, as UI automation sees it.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.8's "every tile exposes an automation peer with its label and shape, so screen readers can
/// announce <c>repeat 10, C block, 3 statements inside</c>". Two things make that true, and they are
/// separate: the sentence itself, which is built in Core by <c>BlockAnnouncement</c> and bound to
/// <c>AutomationProperties.Name</c> in the tile's markup; and this peer, which is what actually hands that
/// sentence to the accessibility and UI-automation stacks.
/// </para>
/// <para>
/// The peer is required rather than optional. WPF's default peer for a <see cref="UserControl"/> derives
/// its name from the type, so without an override a screen reader says "user control" once per block and a
/// UI-automation test can only address a block by its position on screen — which changes the moment anything
/// is inserted above it. <see cref="AutomationProperties.AutomationId"/> fixes the second half: the block's
/// own id, so a test can find "the block called b12" rather than "the fourth tile".
/// </para>
/// <para>
/// The selection-item pattern is implemented because a block *is* a selectable item — Part 18.1's click
/// selects it, and this is that click from outside the process. A peer that reported
/// <c>AutomationControlType.Custom</c> and nothing else would be findable by name and still be a dead end
/// for anything driving the canvas.
/// </para>
/// </remarks>
public sealed class BlockTileAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
{
    /// <summary>Wraps one tile.</summary>
    /// <param name="owner">The tile.</param>
    public BlockTileAutomationPeer(BlockTile owner)
        : base(owner) => _tile = owner;

    private readonly BlockTile _tile;

    /// <summary>
    /// The sentence this peer announces.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="AutomationProperties.NameProperty"/> rather than recomputed, because the tile
    /// binds it from the view model and the two must not be able to disagree: a peer that computed its own
    /// version would announce a block's position from a walk of a different collection than the one the
    /// canvas is showing.
    /// </remarks>
    protected override string GetNameCore()
    {
        if (_tile.GetValue(AutomationProperties.NameProperty) is string named && named.Length > 0)
        {
            return named;
        }

        // A palette row and any tile that has not been announced yet. The tooltip is the next best thing
        // the element has, and an empty name makes the tile invisible to a screen reader rather than
        // merely unnamed.
        if (_tile.ToolTip is string tip && tip.Length > 0)
        {
            return tip;
        }

        return base.GetNameCore();
    }

    /// <summary>
    /// <see cref="AutomationControlType.Custom"/>, because a block is none of the standard types.
    /// </summary>
    /// <remarks>
    /// Naming it <c>Button</c> would give the tile an invoke pattern and a screen reader would say "button",
    /// which is a claim about what a block is. <c>Text</c> would say "text". <c>Custom</c> says "something
    /// this application defined", which is the truth, and the name and the selection pattern are what make
    /// it usable.
    /// </remarks>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

    /// <summary>Blocks are focusable, and Part 9.8's focus order is document order.</summary>
    protected override bool IsKeyboardFocusableCore() => _tile.Focusable;

    /// <summary>Whether the keyboard is on this block right now.</summary>
    protected override bool HasKeyboardFocusCore() => _tile.IsKeyboardFocused;

    /// <summary>What this peer is, for a UI-automation test that prints the tree.</summary>
    /// <remarks>
    /// "BlockTile" rather than the default, which is the *namespace-qualified* type name. A test asserting
    /// on it gets <c>DeckForge.App.Controls.Blocks.BlockTile</c>, and a rename of the namespace breaks the
    /// assertion without breaking anything a user does.
    /// </remarks>
    protected override string GetClassNameCore() => nameof(BlockTile);

    /// <summary>
    /// The selection pattern, and nothing else.
    /// </summary>
    /// <remarks>
    /// <see cref="PatternInterface.SelectionItem"/> rather than a container's
    /// <see cref="PatternInterface.Selection"/>. A tile is an item that can be in or out of a selection; it
    /// is not a thing that owns other tiles' selection state, and claiming the container pattern would make
    /// a client ask this tile how many blocks are selected — which it genuinely cannot know.
    /// </remarks>
    /// <param name="patternInterface">The pattern being asked for.</param>
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

    /// <summary>Whether this block is selected.</summary>
    public bool IsSelected => Node?.IsSelected == true;

    /// <summary>
    /// What owns the selection this tile is part of, which here is nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null, deliberately. The property exists because the selection-item pattern declares it, and it means
    /// "the peer that owns this item's selection". The canvas is not that: there is no peer here holding a
    /// <c>Selection</c> pattern and able to report <c>GetSelection</c>, so any answer would be a peer
    /// pretending to be a container it is not — and a client that believed it would then ask the canvas how
    /// many blocks are selected and get an empty list while the rings are plainly drawn on nine tiles.
    /// </para>
    /// <para>
    /// What a client actually needs from a tile works and is the four verbs below: <c>IsSelected</c>,
    /// <c>Select</c>, <c>AddToSelection</c> and <c>RemoveFromSelection</c>. Part 9.8 asks for a peer that
    /// lets a screen reader announce a block, and that is the name and the position; the selection pattern
    /// is here because a block is a selectable item and a client driving the canvas has to be able to say
    /// so.
    /// </para>
    /// </remarks>
    IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => null;

    /// <summary>
    /// Selects this block, dropping whatever else was selected.
    /// </summary>
    /// <remarks>
    /// Routed through the tile rather than poked at the editor: the tile already knows how a click becomes a
    /// selection, and a second route into the editor is a second thing that has to be kept in step.
    /// </remarks>
    public void Select() => _tile.RequestSelection();

    /// <summary>Adds this block to the selection.</summary>
    public void AddToSelection() => _tile.RequestAddToSelection();

    /// <summary>Takes this block out of the selection.</summary>
    public void RemoveFromSelection() => _tile.RequestRemoveFromSelection();

    /// <summary>
    /// The block this peer is about, or null before a data context has arrived.
    /// </summary>
    /// <remarks>
    /// Asked of the tile rather than held, because the tile's data context is replaced on every rebuild and
    /// a peer that cached a block would announce a block that had been deleted.
    /// </remarks>
    private BlockNodeViewModel? Node => _tile.DataContext as BlockNodeViewModel;
}
