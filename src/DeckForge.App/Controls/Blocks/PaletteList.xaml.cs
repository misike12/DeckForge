using System.Windows.Controls;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The palette: a search box over one real block per catalogue row.
/// </summary>
/// <remarks>
/// Read-only in Phase 3. Dragging out of the palette is Phase 4, and the header says so rather than
/// offering a drag that does nothing — a control that looks draggable and is not is worse than one that
/// plainly is not yet.
/// </remarks>
public partial class PaletteList : UserControl
{
    public PaletteList() => InitializeComponent();
}