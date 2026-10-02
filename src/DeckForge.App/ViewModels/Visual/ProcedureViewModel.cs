using System.Collections.ObjectModel;
using DeckForge.App.Controls.Blocks;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// One procedure's body, as a column on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.2 draws a procedure in the script strip as a peer of the scripts, and that is what it is: a
/// named body of blocks the user edits with the same gestures as any other. It is a column rather than
/// something inside the inspector because the body is a stack of blocks, and a stack of blocks edited
/// through a form is a worse editor than the one three inches to the left.
/// </para>
/// <para>
/// No hat, so <see cref="HasHat"/> is false and the column draws its heading where a hat tile would be.
/// The heading is the declaration's name and its accent is the Procedures colour, which is what ties the
/// column to the palette row a user would find its call blocks under.
/// </para>
/// </remarks>
public sealed class ProcedureViewModel : ColumnViewModel
{
    public ProcedureViewModel(ProcedureDeclaration procedure)
        : base(procedure.Body)
    {
        Procedure = procedure;

        // The document holds plain lists, and a plain list raises no change notification — so a panel
        // bound straight to it would show the parameters a procedure had when the panel was built, and a
        // row added by the editor would exist in the document and nowhere on screen. Projected instead, the
        // same way the column's body is.
        Parameters = new ObservableCollection<ProcedureParameter>(procedure.Parameters);
    }

    /// <summary>The declaration this draws.</summary>
    public ProcedureDeclaration Procedure { get; }

    /// <summary>
    /// The parameters, projected over the declaration's own objects.
    /// </summary>
    /// <remarks>
    /// The same instances, not copies, so the panel's rows can be matched back to the declaration by
    /// reference when the editor rewrites the list — which it does, replacing the list rather than editing
    /// it in place so that every row's removal and re-addition is one undo entry.
    /// </remarks>
    public ObservableCollection<ProcedureParameter> Parameters { get; }

    public override BodyRef Address => BodyRef.ProcedureBody(Procedure.Id);

    public override string Header => Procedure.Name;

    /// <summary>
    /// The palette colour for a procedure, so a procedure column and a call block read as the same thing.
    /// </summary>
    public override string AccentKey => BlockTheme.FillKey(BlockCategory.Procedures);

    public override bool HasHat => false;

    public override int BlockCount => Procedure.Body.Count;

    /// <summary>How the heading says what the procedure is, in the strip and on the canvas alike.</summary>
    public string Signature =>
        Parameters.Count == 0
            ? Procedure.Returns ? $"{Procedure.Name} →" : $"{Procedure.Name} ()"
            : Procedure.Returns
                ? $"{Procedure.Name} ({string.Join(", ", Parameters.Select(p => p.Name))}) →"
                : $"{Procedure.Name} ({string.Join(", ", Parameters.Select(p => p.Name))})";

    /// <summary>What the strip says under the name, which the signature alone does not say.</summary>
    public string Summary => Procedure.Returns
        ? $"{Procedure.Body.Count} block(s), returns a value"
        : $"{Procedure.Body.Count} block(s)";

    /// <summary>
    /// Whether a call block naming this procedure would be legal as written.
    /// </summary>
    /// <remarks>
    /// Read here rather than taken from the diagnostics pane because the strip has to be right about it
    /// even when a diagnostic is collapsed, filtered or scrolled away — a row that shows a call as legal
    /// and then says it is not is worse than showing nothing.
    /// </remarks>
    public bool IsCallableAsBareCall => Parameters.Count == 0;
}