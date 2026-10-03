using System.Collections.ObjectModel;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>One script on the canvas: a hat and the stack beneath it.</summary>
/// <remarks>
/// The position is read from the document and never written back in Phase 3. Part 6.1 makes it part of
/// the document precisely so that arranging scripts survives a reload, and that promise is only credible
/// if the canvas shows the stored numbers rather than ones it invented.
/// </remarks>
public sealed class ScriptViewModel : ColumnViewModel
{
    public ScriptViewModel(VisualScript script)
        : base(script.Body)
    {
        Script = script;
        Hat = new BlockNodeViewModel(script.Hat);
    }

    /// <summary>The document node this draws.</summary>
    public VisualScript Script { get; }

    /// <summary>The block that starts the script.</summary>
    public BlockNodeViewModel Hat { get; }

    /// <summary>The script's name, shown in the strip.</summary>
    public string Name => Script.Name;

    /// <summary>What the script starts with, in words, for the strip's second line.</summary>
    public string HatLabel =>
        BlockCatalog.Find(Script.Hat.Kind) is { } descriptor
            ? BlockLabel.PreviewText(descriptor, Services.BlockText.Current)
            : Script.Hat.Kind;

    public override BodyRef Address => BodyRef.ScriptBody(Script.Hat.Id);

    public override string Header => Script.Name;

    public override string AccentKey => Hat.FillKey;

    public override bool HasHat => true;

    /// <summary>Where the script sits on the canvas, in workspace units.</summary>
    public double X => Script.X;

    /// <summary>Where the script sits on the canvas, in workspace units.</summary>
    public double Y => Script.Y;

    /// <summary>Whether the script is switched off.</summary>
    public bool IsDisabled => Script.Disabled;

    public override int BlockCount => Script.Blocks().Count();
}