using System.Collections.ObjectModel;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// One column on the canvas: a hat and a body, or a procedure's body with a name above it.
/// </summary>
/// <remarks>
/// <para>
/// Phase 7 made a procedure something a user can edit, and the moment it is editable it has to be
/// somewhere to be edited. Giving it its own kind of column would have meant a second canvas template and
/// a second hit-test path; this base class means the canvas has exactly one column template, one list of
/// columns, and one rule for where a block's body is — with <see cref="HasHat"/> being the whole
/// difference between the two.
/// </para>
/// <para>
/// The name is <c>Header</c> rather than <c>Name</c> because a procedure's heading is not a block label:
/// it names a declaration, and binding it to something called Name next to a block's own name is how two
/// different things end up sharing one binding path.
/// </para>
/// </remarks>
public abstract class ColumnViewModel
{
    protected ColumnViewModel(IReadOnlyList<Block> body)
    {
        StatementBlocks = body;
        Body = new ObservableCollection<BlockNodeViewModel>(body.Select(statement => new BlockNodeViewModel(statement)));
    }

    /// <summary>
    /// The document statements behind <see cref="Body"/>, for the parts of the canvas that hand the
    /// resolver a body rather than a list of view models.
    /// </summary>
    /// <remarks>
    /// Exposed rather than reconstructed. The resolver needs the document's own blocks — it measures
    /// rectangles by id and indexes into the body it is given — and rebuilding that list from the view
    /// models would be a second projection of the same thing to keep in step with the first.
    /// </remarks>
    public IReadOnlyList<Block> StatementBlocks { get; }

    /// <summary>How this column's body is addressed by a drop, a move or an undo entry.</summary>
    public abstract BodyRef Address { get; }

    /// <summary>What the column's heading says.</summary>
    public abstract string Header { get; }

    /// <summary>The theme key of the accent bar beside the heading.</summary>
    public abstract string AccentKey { get; }

    /// <summary>Whether there is a hat tile above the body.</summary>
    public abstract bool HasHat { get; }

    /// <summary>The statements under the heading, in order.</summary>
    public ObservableCollection<BlockNodeViewModel> Body { get; }

    /// <summary>How many blocks the column holds, the body and everything nested in it.</summary>
    public abstract int BlockCount { get; }
}