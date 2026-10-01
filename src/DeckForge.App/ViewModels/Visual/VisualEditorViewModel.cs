using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The Visual page: a document, the editor that owns it, the scripts on the canvas, and the diagnostics
/// they produce.
/// </summary>
/// <remarks>
/// <para>
/// The page owns the <see cref="DocumentEditor"/>, and the canvas, the palette and the inspector all go
/// through it. That is not tidiness: a second path to the document is a change with nothing on the undo
/// stack, and an editor that can be bypassed is an editor whose history is a list of the edits somebody
/// remembered to make.
/// </para>
/// <para>
/// The sample document stands in for a file until Phase 5. Every rebuild projects the document that is
/// already there rather than calling the factory again, so a dropped block survives being re-projected;
/// Phase 3 could rebuild from scratch on every change precisely because nothing changed it.
/// </para>
/// </remarks>
public sealed partial class VisualEditorViewModel : ObservableObject
{
    private string _message = string.Empty;

    public VisualEditorViewModel()
    {
        Document = VisualSampleProject.Build();
        Validation = VisualSampleProject.ValidationContext;
        Palette = new PaletteViewModel();
        Editor = new DocumentEditor(Document);
        Editor.Changed += () => Build();

        Build();
    }

    /// <summary>The document on screen. The sample until Phase 5 gives it a file.</summary>
    public VisualProject Document { get; private set; }

    /// <summary>
    /// The only way the document is edited.
    /// </summary>
    public DocumentEditor Editor { get; }

    /// <summary>What the document refers to outside itself.</summary>
    public VisualValidationContext Validation { get; }

    /// <summary>The category rail and the palette.</summary>
    public PaletteViewModel Palette { get; }

    /// <summary>The target whose scripts are on the canvas.</summary>
    public VisualTarget Target => Document.Targets[0];

    /// <summary>The scripts, in document order.</summary>
    public ObservableCollection<ScriptViewModel> Scripts { get; private set; } = [];

    /// <summary>Everything wrong with the document, sorted worst first.</summary>
    public ObservableCollection<VisualDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>The block the inspector is showing.</summary>
    public BlockNodeViewModel? Selected { get; private set; }

    /// <summary>What the header says about the target.</summary>
    public string TargetName => $"{Target.Name} ({Target.Kind})";

    /// <summary>What the header says about the document.</summary>
    public string DocumentLine =>
        $"{Document.Targets.Count} target · {Scripts.Count} scripts · {Target.Blocks().Count()} blocks";

    /// <summary>Whether there is a block selected for the inspector.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Whether the document has nothing wrong with it.</summary>
    public bool IsClean => Diagnostics.All(diagnostic => diagnostic.Severity != VisualSeverity.Error);

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => Editor.CanUndo;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => Editor.CanRedo;

    /// <summary>What the next undo would revert, which is the tooltip on the button.</summary>
    public string UndoLabel => Editor.UndoLabel is { } label ? $"Undo {label}" : "Nothing to undo";

    /// <summary>What the next redo would re-apply.</summary>
    public string RedoLabel => Editor.RedoLabel is { } label ? $"Redo {label}" : "Nothing to redo";

    /// <summary>
    /// The last thing the editor refused, or empty.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="Report"/> and cleared by the next edit. A refusal is information rather than an
    /// error: dropping a boolean onto a stack gap is a thing a user does constantly while learning the
    /// shapes, and the design's own answer is that the indicator simply does not appear. Saying why once,
    /// quietly, teaches the shape rule faster than any amount of documentation.
    /// </remarks>
    public string Message
    {
        get => _message;
        private set
        {
            if (string.Equals(_message, value, StringComparison.Ordinal))
            {
                return;
            }

        _message = value;

        // Both names spelled out rather than relying on CallerMemberName inside a setter, because a
        // notification that fires under the wrong name is invisible: the binding silently does not
        // update, the message never appears, and nothing anywhere reports an error.
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
    }
    }

    /// <summary>Whether there is anything to say in the message line.</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>A one-line summary for the header, which is where the design puts the honesty note.</summary>
    public string StatusText => Diagnostics.Count == 0
        ? "Every shipping block, one script per category."
        : $"{Diagnostics.Count(d => d.Severity == VisualSeverity.Error)} error(s), "
          + $"{Diagnostics.Count(d => d.Severity == VisualSeverity.Warning)} warning(s), "
          + $"{Diagnostics.Count(d => d.Severity == VisualSeverity.Info)} note(s).";

    /// <summary>What a diagnostic means in words, for the pane's severity column.</summary>
    public static string SeverityText(VisualDiagnostic diagnostic) => diagnostic.Severity switch
    {
        VisualSeverity.Error => "Error",
        VisualSeverity.Warning => "Warning",
        _ => "Note",
    };

    /// <summary>Rebuilds the scripts and the diagnostics from the document.</summary>
    /// <remarks>
    /// Projecting the document that is already there rather than rebuilding the sample. The difference is
    /// invisible until something edits it, and then it is the difference between a drop surviving and
    /// disappearing — so it is worth the line of comment that stops somebody "simplifying" it back.
    /// </remarks>
    public void Build()
    {
        Scripts = new ObservableCollection<ScriptViewModel>(Target.Scripts.Select(script => new ScriptViewModel(script)));
        OnPropertyChanged(nameof(Scripts));
        OnPropertyChanged(nameof(TargetName));
        OnPropertyChanged(nameof(DocumentLine));

        Diagnostics = new ObservableCollection<VisualDiagnostic>(
            VisualValidator.Validate(Document, Validation)
                .OrderByDescending(diagnostic => diagnostic.Severity));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsClean));

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));

        // The selection holds a view model over a block, and a rebuild replaces every one of them. A
        // selection that pointed at the old object would keep the inspector showing a block that is no
        // longer the selected one, with no visible way for it to be wrong.
        Reselect();
    }

    /// <summary>Says why an edit was refused.</summary>
    public void Report(string problem) => Message = problem;

    /// <summary>Undoes one gesture.</summary>
    [RelayCommand]
    private void Undo()
    {
        if (Editor.Undo())
        {
            Message = string.Empty;
        }
    }

    /// <summary>Redoes one gesture.</summary>
    [RelayCommand]
    private void Redo()
    {
        if (Editor.Redo())
        {
            Message = string.Empty;
        }
    }

    /// <summary>Selects a block for the inspector.</summary>
    /// <remarks>
    /// Wiring only. Selection is what the diagnostics pane's "click to select and centre the block"
    /// needs (Part 9.7), what Phase 5's inspector hangs off, and what Phase 4's keyboard traversal
    /// moves, so the page owns it rather than each of them.
    /// </remarks>
    public void Select(BlockNodeViewModel? block)
    {
        if (ReferenceEquals(Selected, block))
        {
            return;
        }

        if (Selected is { } previous)
        {
            previous.IsSelected = false;
        }

        Selected = block;

        if (Selected is { } current)
        {
            current.IsSelected = true;
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Selects by block id, for a keyboard move or a diagnostic click.</summary>
    public void SelectById(string blockId) => Select(AllNodes().FirstOrDefault(node => node.Id == blockId));

    /// <summary>Every block view model on the canvas, in visual order.</summary>
    /// <remarks>
    /// Walked rather than kept in a list, because the canvas is the authority on what is on it and a
    /// second list would go stale on the first rebuild.
    /// </remarks>
    public IReadOnlyList<BlockNodeViewModel> AllNodes()
    {
        var nodes = new List<BlockNodeViewModel>();

        foreach (var script in Scripts)
        {
            nodes.Add(script.Hat);
            Walk(script.Body, nodes);
        }

        return nodes;
    }

    private static void Walk(IEnumerable<BlockNodeViewModel> blocks, List<BlockNodeViewModel> into)
    {
        foreach (var block in blocks)
        {
            into.Add(block);

            foreach (var body in block.Bodies)
            {
                Walk(body.Statements, into);
            }

            foreach (var slot in block.Slots)
            {
                if (slot.Child is { } child)
                {
                    Walk([child], into);
                }
            }
        }
    }

    /// <summary>
    /// Keeps the selection pointed at the same block after a rebuild.
    /// </summary>
    private void Reselect()
    {
        if (Selected is not { } previous)
        {
            return;
        }

        var replacement = AllNodes().FirstOrDefault(node => node.Id == previous.Id);
        if (replacement is null)
        {
            // Undoing the delete of the selected block, or of something containing it, leaves nothing to
            // point at. Dropping the selection is the honest answer; keeping the old object would show an
            // inspector for a block that is no longer in the document.
            Select(null);
            return;
        }

        Selected.IsSelected = false;
        Selected = replacement;
        Selected.IsSelected = true;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
    }
}
