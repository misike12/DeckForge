using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

/// <summary>
/// The Visual page: a document, the scripts on the canvas, and the diagnostics they produce.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 has no persistence, so the document is <see cref="VisualSampleProject"/> and the page is a
/// viewer. The structure is already the one a real editor needs — a document, a target, scripts, a
/// palette, a selected block — because building it twice would mean the second one is the first with the
/// interesting parts removed.
/// </para>
/// <para>
/// The diagnostics are real, from <see cref="VisualValidator"/>, and shown from the first phase. That is
/// deliberate: a canvas that only looks right when nothing is wrong is a canvas nobody can trust, and
/// the sample has warnings in it on purpose — a cap block followed by more statements is exactly what a
/// user should be told about.
/// </para>
/// </remarks>
public sealed partial class VisualEditorViewModel : ObservableObject
{
    public VisualEditorViewModel()
    {
        Document = VisualSampleProject.Build();
        Validation = VisualSampleProject.ValidationContext;
        Palette = new PaletteViewModel();
        Build();
    }

    /// <summary>The document on screen. The sample until Phase 5 gives it a file.</summary>
    public VisualProject Document { get; private set; }

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

    /// <summary>A one-line summary for the header, which is where the design puts the honesty note.</summary>
    public string StatusText => Diagnostics.Count == 0
        ? "Sample document. Every shipping block, one script per category."
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
    }

    /// <summary>Selects a block for the inspector.</summary>
    /// <remarks>
    /// Wiring only. Selection is what the diagnostics pane's "click to select and centre the block"
    /// needs (Part 9.7) and what Phase 5's inspector and Phase 8's breakpoints both hang off, so the
    /// page owns it rather than each of them.
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
}