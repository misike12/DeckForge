using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeckForge.App.ViewModels.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The palette's search box over real blocks, and the start of a drag out of it.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.2 asks for "a live miniature of every block", which only means something if the miniature is
/// drawn by the same code that draws the block. Each row here is the same <see cref="BlockTile"/> the
/// canvas uses, so a palette that draws blocks one way and the canvas another is a palette that lies
/// about what dropping will produce.
/// </para>
/// <para>
/// The press is handed to the workspace rather than handled here. Part 9.5 has two input paths, and this
/// is the one that crosses a control boundary: the ghost has to be drawn in the canvas's adorner layer,
/// because that is the only layer that can draw over both surfaces without either of them knowing the
/// other exists. So the palette reports the press and the workspace owns everything after it.
/// </para>
/// </remarks>
public partial class PaletteList : UserControl
{
    public PaletteList()
    {
        InitializeComponent();

        PreviewMouseLeftButtonDown += OnPreviewMouseDown;
    }

    /// <summary>Raised when a row is pressed, carrying the miniature and where the pointer was.</summary>
    /// <remarks>
    /// A plain CLR event rather than a routed one because the workspace is not an ancestor of the rows —
    /// they are in a sibling panel — so there is no route to bubble along.
    /// </remarks>
    public event Action<BlockNodeViewModel, Point>? RowPressed;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (RowPressed is not { } pressed
            || args.OriginalSource is not DependencyObject source
            || Find(source) is not { } node)
        {
            return;
        }

        pressed(node, args.GetPosition(this));
        args.Handled = true;
    }

    /// <summary>
    /// The palette row under an element, by walking up to the tile.
    /// </summary>
    /// <remarks>
    /// The original source is usually a <c>TextBlock</c> or a <c>Border</c> inside the row's template, not
    /// the tile itself, and the tile is the only element that knows which block it draws.
    /// </remarks>
    private static BlockNodeViewModel? Find(DependencyObject source)
    {
        for (var current = source; current is not null;)
        {
            if (current is BlockTile { DataContext: BlockNodeViewModel node })
            {
                return node;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
