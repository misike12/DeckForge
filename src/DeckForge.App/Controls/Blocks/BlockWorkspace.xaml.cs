using System.Windows.Controls;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The canvas: the scripts of the open target, side by side.
/// </summary>
/// <remarks>
/// <para>
/// One column per script, laid out left to right. Part 9.4 describes a <c>ScrollViewer</c> around a
/// zoom <c>ScaleTransform</c> around a <c>Canvas</c> holding one view per script; the transform and the
/// absolute positions are deliberately not here yet. Zoom and pan are Phase 10, and building them now
/// means a second layout that Phase 10 throws away — plus a coordinate system nothing is tested against,
/// since the drop resolver's rectangles arrive in Phase 4 along with the thing that has to agree with
/// them.
/// </para>
/// <para>
/// The dot grid is behind everything rather than on the canvas's own background, so it does not move
/// with a script. That is cosmetic and will be replaced when panning arrives; it is here because an
/// unbroken field of colour with no reference is genuinely hard to read.
/// </para>
/// </remarks>
public partial class BlockWorkspace : UserControl
{
    public BlockWorkspace() => InitializeComponent();
}