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
/// </remarks>
public partial class StagePanel : UserControl
{
    public StagePanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Builds the menu for the stage and its trace, each time one opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A factory rather than a menu, and for the canvas menu's reason: Part 18.5's stage menu greys
    /// "Toggle breakpoint" while no block is selected, and a menu built once at construction would be greyed
    /// for the rest of the session.
    /// </para>
    /// <para>
    /// Supplied by the page because the menu's items are commands and the command table belongs to a view
    /// model this control does not have.
    /// </para>
    /// <para>
    /// The menu instance is shared by four elements, which is safe because
    /// <see cref="System.Windows.Controls.ContextMenuService"/> reassigns <c>PlacementTarget</c> on each
    /// open. <c>ContextMenu</c> is an ordinary dependency property and is <em>not</em> inherited, so a menu
    /// on the outer element would be invisible on the grid's children — and a panel or a wrap panel with no
    /// background is transparent to the mouse, so those four carry an explicit transparent background in
    /// the markup. WPF then does the opening and the positioning, which is the part this code cannot be
    /// trusted to re-implement.
    /// </para>
    /// </remarks>
    public Func<ContextMenu>? StageMenuFactory
    {
        get => _stageMenuFactory;
        set
        {
            _stageMenuFactory = value;

            _stageMenu ??= new ContextMenu();
            _stageMenu.ContextMenuOpening -= OnStageMenuOpening;
            _stageMenu.ContextMenuOpening += OnStageMenuOpening;

            Root.ContextMenu = _stageMenu;
            TransportRow.ContextMenu = _stageMenu;
            StatePanel.ContextMenu = _stageMenu;
            TraceScroll.ContextMenu = _stageMenu;
        }
    }

    private Func<ContextMenu>? _stageMenuFactory;
    private ContextMenu? _stageMenu;

    /// <summary>Refills the stage menu, so its greyed rows match the moment it opened.</summary>
    private void OnStageMenuOpening(object? sender, ContextMenuEventArgs args)
    {
        if (sender is not ContextMenu menu || StageMenuFactory?.Invoke() is not { } built)
        {
            return;
        }

        menu.Items.Clear();
        foreach (var item in built.Items)
        {
            menu.Items.Add(item);
        }
    }
}