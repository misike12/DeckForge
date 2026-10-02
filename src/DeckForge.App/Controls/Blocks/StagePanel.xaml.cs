using System.Windows.Controls;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// The stage panel: Part 8's answer to "what will my blocks actually do".
/// </summary>
/// <remarks>
/// <para>
/// A user control and not a page, for the same reason the palette and the rail are controls: the Visual
/// page already has four panels and a header, and the stage is a fifth region of the same canvas rather
/// than somewhere else to navigate to. A debugger behind a navigation item is a debugger nobody opens.
/// </para>
/// <para>
/// It holds no state. Every list is bound to a collection the view model rebuilt, the transport buttons
/// are the view model's own commands, and the honesty statement is a property. That is what lets the whole
/// of Part 8 be tested without a window, and it is also why the panel cannot show something stale: there
/// is nothing here to go stale.
/// </para>
/// </remarks>
public partial class StagePanel : UserControl
{
    public StagePanel()
    {
        InitializeComponent();
    }
}