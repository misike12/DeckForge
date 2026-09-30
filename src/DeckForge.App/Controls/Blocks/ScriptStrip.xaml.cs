using System.Windows.Controls;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The list of scripts in the open target, read-only in Phase 3.
/// </summary>
/// <remarks>
/// Adding, renaming and deleting a script is Phase 7's work, so the strip only lists. It is here from
/// the first phase rather than later because the sample has eleven scripts: a canvas with eleven scripts
/// and no list of them is a wall, and the layout in Part 9.2 is only reviewable with the strip in place.
/// </remarks>
public partial class ScriptStrip : UserControl
{
    public ScriptStrip() => InitializeComponent();
}