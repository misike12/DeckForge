using System.Collections.ObjectModel;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>One script on the canvas: a hat and the stack beneath it.</summary>
/// <remarks>
/// The position is read from the document and never written back in Phase 3. Part 6.1 makes it part of
/// the document precisely so that arranging scripts survives a reload, and that promise is only credible
/// if the canvas shows the stored numbers rather than ones it invented.
/// </remarks>
public sealed class ScriptViewModel
{
    public ScriptViewModel(VisualScript script)
    {
        Script = script;
        Hat = new BlockNodeViewModel(script.Hat);
        Body = new ObservableCollection<BlockNodeViewModel>(
            script.Body.Select(statement => new BlockNodeViewModel(statement)));
    }

    /// <summary>The document node this draws.</summary>
    public VisualScript Script { get; }

    /// <summary>The block that starts the script.</summary>
    public BlockNodeViewModel Hat { get; }

    /// <summary>The statements under the hat, in order.</summary>
    public ObservableCollection<BlockNodeViewModel> Body { get; }

    /// <summary>The script's name, shown in the strip.</summary>
    public string Name => Script.Name;

    /// <summary>Where the script sits on the canvas, in workspace units.</summary>
    public double X => Script.X;

    /// <summary>Where the script sits on the canvas, in workspace units.</summary>
    public double Y => Script.Y;

    /// <summary>Whether the script is switched off.</summary>
    public bool IsDisabled => Script.Disabled;

    /// <summary>How many statements the script holds, hats and bodies both counted in.</summary>
    public int BlockCount => Script.Blocks().Count();
}