using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.Core.Visual;
using DeckForge.Core.Workspace;

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
    private readonly WorkspaceManager _workspaces;
    private string? _canvasWorkspace;
    private string _message = string.Empty;

    public VisualEditorViewModel(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;

        // A workspace with a canvas opens that canvas; one without keeps the sample, because a canvas is
        // not worth hiding until there is somewhere to put it. The sample is the empty state P3 needed and
        // it is also what a user sees before they have picked a workspace to draw in.
        var saved = LoadFromWorkspace();
        Document = saved ?? VisualSampleProject.Build();
        // Empty, not the sample's. The sample's context carries its own two parameter names and one host
        // variable, and every real canvas was being validated against them: binding a declared parameter
        // produced "that is not a name anything declares", and a name that happened to be `text` or
        // `count` never warned even when nothing declared it. Empty is what the context is for - an editor
        // that has not resolved the workspace's names shows a canvas without a wall of false positives.
        Validation = VisualValidationContext.Empty;
        // The palette's recency and pins belong to the workspace, so it is handed the one behind the
        // canvas. A sample document with no workspace gets a palette that remembers nothing, which is what
        // a palette is supposed to do when there is nowhere to remember it.
        Palette = new PaletteViewModel(workspaces.Current);
Editor = new DocumentEditor(Document);
        Inspector = new InspectorViewModel(this);
        Stage = new StageViewModel(this);
        Editor.Changed += () => Build();

        Build();
    }

    /// <summary>The document on screen: the workspace's canvas, or the sample when there is none.</summary>
    public VisualProject Document { get; private set; }

    /// <summary>
    /// The only way the document is edited.
    /// </summary>
    public DocumentEditor Editor { get; }

    /// <summary>What the document refers to outside itself.</summary>
    public VisualValidationContext Validation { get; }

    /// <summary>The category rail and the palette.</summary>
    public PaletteViewModel Palette { get; private set; }

    /// <summary>
    /// The inspector: the editors for the selected block's slots and dropdowns.
    /// </summary>
    /// <remarks>
    /// Owned by this view model rather than by the page, because it has to be rebuilt on every edit as well
    /// as on every selection, and the page sees neither. The canvas rebuilds on both and so does the
    /// diagnostics list; a panel wired to selection alone shows the values the document had before the last
    /// drag.
    /// </remarks>
    public InspectorViewModel Inspector { get; }

/// <summary>The target whose scripts are on the canvas.</summary>
    public VisualTarget Target => Document.Targets[0];

    /// <summary>The canvas's zoom, pan and minimap, once the page has asked for them.</summary>
    /// <remarks>
    /// Settable and nullable rather than built here, because the view model needs the workspace *control*
    /// as its host and the control cannot reach a view model that does not exist yet. The page creates it on
    /// first use, which is also why the canvas panel's bindings are null until it does.
    /// <para>
    /// A backing field and a notification rather than an auto-property, because the minimap binds to this
    /// and a silent assignment leaves it bound to nothing: the page then draws rectangles into a view model
    /// nothing is showing, and the thumbnail says the canvas is empty over a canvas full of blocks.
    /// </para>
    /// </remarks>
    public CanvasViewModel? Canvas
    {
        get => _canvas;
        set
        {
            if (ReferenceEquals(_canvas, value))
            {
                return;
            }

            _canvas = value;
            OnPropertyChanged();
        }
    }

    private CanvasViewModel? _canvas;

    /// <summary>
    /// The stage: the simulated host, the interpreter and everything they show.
    /// </summary>
    /// <remarks>
    /// Owned here rather than by the page, for the same reason the inspector is: the stage has to be told
    /// when the document changed, and the page does not know the document changed except by watching this
    /// view model.
    /// </remarks>
    public StageViewModel Stage { get; }

    /// <summary>
    /// The block ids the interpreter stops on.
    /// </summary>
    /// <remarks>
    /// Owned here and handed to the interpreter by reference, so the tile the user clicked and the run that
    /// pauses are reading the same set. A copy on each side is a breakpoint that shows as set on a tile and
    /// does nothing when the script reaches it.
    /// </remarks>
    public HashSet<string> Breakpoints { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The variable names the watch table shows.
    /// </summary>
    /// <remarks>
    /// Held by the editor rather than the stage so a run does not lose it: the stage is rebuilt whenever
    /// the document changes, and "I watched that variable and then edited a block" losing the watch is not
    /// a thing anyone would design on purpose.
    /// </remarks>
    public HashSet<string> Watched { get; } = new(StringComparer.Ordinal);

    /// <summary>Puts the breakpoint state onto every tile the canvas is showing.</summary>
    /// <remarks>
    /// Called after a rebuild as well as after a click. Tiles are new objects on every edit, so a
    /// breakpoint that was only stamped where it was set would quietly disappear the next time the user
    /// typed a character.
    /// </remarks>
    public void StampBreakpoints()
    {
        foreach (var node in AllNodes())
        {
            node.IsBreakpoint = Breakpoints.Contains(node.Id);
        }
    }

    /// <summary>
    /// Moves the "the stage is here" mark to one block, or clears it.
    /// </summary>
    /// <remarks>
    /// Called from the page whenever the stage publishes a new current block. It is on the editor rather
    /// than on the stage because the tiles are the editor's to project.
    /// </remarks>
    /// <param name="blockId">The block, or null or empty for none.</param>
    public void StampCurrent(string? blockId)
    {
        foreach (var node in AllNodes())
        {
            node.IsCurrent = !string.IsNullOrEmpty(blockId)
                && string.Equals(node.Id, blockId, StringComparison.Ordinal);
        }
    }

    /// <summary>The scripts, in document order.</summary>
    public ObservableCollection<ScriptViewModel> Scripts { get; private set; } = [];

    /// <summary>
    /// Every column the canvas draws: the scripts first, then the procedures.
    /// </summary>
    /// <remarks>
    /// One list rather than two so the canvas has one items source and one template, and so the hit test,
    /// the keyboard traversal and the drop resolver all enumerate the same thing in the same order. A
    /// procedure is a body of blocks like any other, and the moment it needs its own list is the moment
    /// every one of those has to learn about it too.
    /// </remarks>
    public ObservableCollection<ColumnViewModel> Columns { get; private set; } = [];

    /// <summary>The procedures, for the strip and the My Blocks panel.</summary>
    public ObservableCollection<ProcedureViewModel> Procedures { get; private set; } = [];


    /// <summary>One entry in the hat picker.</summary>
    /// <param name="Kind">The hat's catalogue kind.</param>
    /// <param name="Label">What the picker says.</param>
    /// <param name="Summary">What the row says underneath, for the user deciding between them.</param>
    public sealed record HatChoice(string Kind, string Label, string Summary);

    /// <summary>The procedure the My Blocks panel is showing, or null.</summary>
    public ProcedureViewModel? SelectedProcedure { get; private set; }

    /// <summary>Whether there is a procedure selected to edit.</summary>
    public bool HasSelectedProcedure => SelectedProcedure is not null;

    /// <summary>
    /// Whether the strip should say there are no procedures yet.
    /// </summary>
    /// <remarks>
    /// A real empty state rather than an empty list, for the reason Part 9.3 gives: a list with nothing in
    /// it and no heading leaves the reader deciding whether "My Blocks" is empty, broken, or not a feature
    /// this build has.
    /// </remarks>
    public bool HasNoProcedures => Procedures.Count == 0;

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

    /// <summary>
    /// The hats a script in this target may start with, read from the catalogue.
    /// </summary>
    /// <remarks>
    /// Part 9.2's "hats per target": the picker offers what the catalogue actually has, so a hat row added
    /// there appears here without anybody remembering this page. Filtering out the procedure definitions is
    /// not cosmetic — one of them in this list produces a script that starts with a declaration and emits
    /// nothing at all.
    /// </remarks>
    public IReadOnlyList<HatChoice> HatChoices => AvailableHats();

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

    // ---- the file -----------------------------------------------------------------------------------

    /// <summary>Whether there is a workspace to save into.</summary>
    /// <remarks>
    /// Part 9.1's rule that the Save button is disabled when no plugin is open. It is disabled rather than
    /// hidden, so the place the command will be stays visible and the button can say why it is off.
    /// </remarks>
    public bool HasWorkspace => Workspace is not null;

    /// <summary>Whether there is a workspace whose canvas this document belongs to.</summary>
    public WorkspaceContext? Workspace => _workspaces.Current;

    /// <summary>Whether the document differs from the file behind it.</summary>
    /// <remarks>
    /// <para>
    /// Compared by serialising the document and comparing text against what is on disk, rather than by a
    /// flag set whenever an edit happens. A flag is wrong in two ways that both matter: it survives an undo
    /// that took the document back to the saved state, and it is set by edits that were then reverted, so
    /// the title says unsaved when nothing is.
    /// </para>
    /// <para>
    /// The cost is a serialisation per rebuild. The document is a few hundred blocks and the rebuild
    /// already walks all of them to project view models, so this is not the expensive part of the frame.
    /// </para>
    /// </remarks>
    public bool IsDirty => Workspace is not { } workspace || VisualStore.IsDirty(workspace, Document);

    /// <summary>What the header says about the file, and whether it holds unsaved work.</summary>
    public string FileLine => Workspace is not { } workspace
        ? "No workspace open — this canvas is not saved anywhere."
        : IsDirty
            ? $"Unsaved changes · {Path.GetFileName(VisualStore.PathFor(workspace))}"
            : $"Saved · {Path.GetFileName(VisualStore.PathFor(workspace))}";

    /// <summary>Whether there is anything to save.</summary>
    /// <summary>Whether Save should do anything, and why not when it should not.</summary>
    /// <remarks>
    /// <see cref="IsCanvasReadOnly"/> is checked before <see cref="IsDirty"/> because a read-only canvas is
    /// always "dirty" - it is the sample standing in for a file this build cannot read - and comparing the
    /// two would leave the button enabled on exactly the canvas where pressing it destroys something.
    /// </remarks>
    public bool CanSave => HasWorkspace && !IsCanvasReadOnly && IsDirty;

    /// <summary>Saves the canvas to the workspace's sidecar.</summary>
    /// <remarks>
    /// <para>
    /// Nothing is validated before the write. A document that does not compile is still the document the
    /// user built, and refusing to save it would leave them editing in memory with no file behind them —
    /// which is the situation the backup exists for and also cannot help with, because there is no file yet.
    /// The diagnostics pane says what is wrong; the Save button's job is to keep the work.
    /// </para>
    /// <para>
    /// Afterwards the undo history is dropped. The history refers to the document that was on screen when
    /// it was built, and after a save the user's next question is "what did I change since" — not "what
    /// could I have undone before I saved". Keeping a history across a save produces an undo that appears
    /// to work and then changes the file on disk behind the title's back.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void Save()
    {
        if (Workspace is not { } workspace)
        {
            Report("Open a plugin project first — the canvas saves into the workspace.");
            return;
        }

        var result = VisualStore.Save(workspace, Document);
        Report(result.Message);
        Editor.ClearHistory();
        Build();
    }

    /// <summary>Discards the canvas and reads the workspace's file back.</summary>
    [RelayCommand]
    private void Revert()
    {
        if (Workspace is not { } workspace)
        {
            Report("Open a plugin project first.");
            return;
        }

        var result = VisualStore.Load(workspace);

        if (result is { Ok: true, Project: { } project })
        {
            Document = project;
            Editor.ReplaceDocument(project);
            Message = string.Empty;
        }

        // The store's own sentence when it has one worth repeating — a recovered or refused read has
        // something to explain, and a plain read does not: "discarded the canvas on screen and read the
        // file back" is the answer the user asked for and the file name alone would not be.
        Report(result is { Ok: true, Recovered: false }
            ? $"Discarded the canvas on screen and read {Path.GetFileName(result.Path)} back."
            : result.Message);

        Build();
    }

    /// <summary>
    /// Shell hook: read the workspace's canvas when the workspace behind the canvas changes.
    /// </summary>
    /// <remarks>
    /// Part 9.1's rule: a plain navigation reloads nothing, because this view model is a singleton and its
    /// document is the one on screen; a genuine workspace switch does reload, because that is what raises
    /// <c>CurrentChanged</c>.
    /// </remarks>
    public void RefreshOnNavigate()
    {
        if (string.Equals(
                _canvasWorkspace,
                _workspaces.Current?.PluginProjectDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LoadFromOpenWorkspace();
    }

    /// <summary>Reads the open workspace's canvas into the page, or puts the sample back.</summary>
    private void LoadFromOpenWorkspace()
    {
        var loaded = LoadFromWorkspace();

        Document = loaded ?? VisualSampleProject.Build();
        Editor.ReplaceDocument(Document);
        _canvasWorkspace = Workspace?.PluginProjectDirectory;

        if (loaded is not null)
        {
            Message = string.Empty;
        }

        Build();
    }

    /// <summary>
    /// The open workspace's canvas, or null when there is nothing to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failure is not fatal and does not replace the document on screen. Refusing to draw anything
    /// because a file could not be read would hide the work that is already there, and the one case where
    /// the file is unreadable and there is no work yet — no canvas at all — is answered by the sample.
    /// </para>
    /// <para>
    /// Anything other than a plain read of the expected file is announced. That includes the recovered
    /// cases, and the window is what insisted on it: with a corrupt <c>canvas.json</c> and a good backup,
    /// the page quietly drew the previous save and said nothing, so the header read "Unsaved changes" —
    /// which is true, and tells the user nothing about the fact that what is on screen is not what is on
    /// disk. They would have concluded they had unsaved work rather than that the app had recovered.
    /// </para>
    /// </remarks>
    private VisualProject? LoadFromWorkspace()
    {
        _canvasWorkspace = _workspaces.Current?.PluginProjectDirectory;

        // The workspace's resx, before anything is built: block labels are read while the view models are
        // constructed, so a lookup installed afterwards would leave the canvas saying English until the
        // next unrelated rebuild. Part 21's translations come from the same file the Localization page
        // edits, and a workspace that has never been translated falls back to the catalogue's English.
        Services.BlockText.Use(_workspaces.Current?.LocalizationDirectory);

        // The palette's recents and pins belong to a workspace, and the page is a singleton that
        // outlives the workspace it was built for. Left alone, opening plugin A and then plugin B still
        // showed A's recents and wrote every star click to A's palette.json, so B's own pins could never
        // be seen or saved. Rebuilt whenever the workspace behind the canvas changes.
        Palette = new PaletteViewModel(_workspaces.Current);
        OnPropertyChanged(nameof(Palette));

        if (_workspaces.Current is not { } workspace)
        {
            return null;
        }

        var result = VisualStore.Load(workspace);

        _pendingLoadMessage = result.Recovered || !result.Ok ? result.Message : null;

        // A file this build cannot read is also a file this build must not write. The load says so in a
        // message; without this the page showed the sample document with Save enabled, and one press put the
        // sample where the newer file had been.
        _readOnlyCanvas = result.ReadOnly;

        return result is { Ok: true, Project: { } project } ? project : null;
    }

    private bool _readOnlyCanvas;

    /// <summary>
    /// Whether the canvas on screen came from a file this build refused to read.
    /// </summary>
    /// <remarks>
    /// True means the document being edited is the sample, not the user's work: the real file is newer than
    /// this build. Saving is refused outright rather than offered with a warning, because there is nothing
    /// here worth saving over it.
    /// </remarks>
public bool IsCanvasReadOnly => _readOnlyCanvas;

    private string? _pendingLoadMessage;

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
        Procedures = new ObservableCollection<ProcedureViewModel>(
            Document.Procedures.Select(procedure => new ProcedureViewModel(procedure)));

Columns = [.. Scripts.Cast<ColumnViewModel>(), .. Procedures];

        StampBreakpoints();
        Stage.Rebind();

        OnPropertyChanged(nameof(Scripts));
        OnPropertyChanged(nameof(Procedures));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(HasNoProcedures));
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

        // The file line is a sentence about the document, so it changes on every edit rather than on a
        // save. That is the point of showing the name: "Unsaved changes · canvas.json" appearing the
        // instant something moves is what tells a user the Save button is worth pressing.
        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(FileLine));

        if (_pendingLoadMessage is { } loadMessage)
        {
            _pendingLoadMessage = null;
            Report(loadMessage);
        }

        Inspector.Build(Selected);
        Inspector.BuildCode(Document);

        // The selection holds a view model over a block, and a rebuild replaces every one of them. A
        // selection that pointed at the old object would keep the inspector showing a block that is no
        // longer the selected one, with no visible way for it to be wrong.
        Reselect();

        // The My Blocks panel follows the selected procedure across a rebuild by id, for the same reason:
        // a panel pointing at the old column would edit a declaration that is no longer the one on screen.
        ReselectProcedure();
    }

    /// <summary>Keeps the selected procedure pointing at the same declaration after a rebuild.</summary>
    private void ReselectProcedure()
    {
        if (SelectedProcedure is { } previous)
        {
            var replacement = Procedures.FirstOrDefault(candidate =>
                string.Equals(candidate.Procedure.Id, previous.Procedure.Id, StringComparison.Ordinal));

            if (replacement is null)
            {
                // Undoing the add of the selected procedure leaves nothing to point at.
                SelectedProcedure = null;
                OnPropertyChanged(nameof(SelectedProcedure));
                OnPropertyChanged(nameof(HasSelectedProcedure));
                return;
            }

            SelectedProcedure = replacement;
            OnPropertyChanged(nameof(SelectedProcedure));
        }
    }

    /// <summary>Says why an edit was refused.</summary>
    public void Report(string problem) => Message = problem;

    // ---- scripts and procedures ----------------------------------------------------------------------

    /// <summary>
    /// The hats a script in this target may start with.
    /// </summary>
    /// <remarks>
    /// Everything hat-shaped in the catalogue, minus the procedure definitions — a procedure's hat is not
    /// a script's hat, and offering it here would produce a script that starts with a procedure body and
    /// generates nothing. The picker's list is short enough that filtering beats a second list to keep in
    /// step with the catalogue.
    /// </remarks>
    public IReadOnlyList<HatChoice> AvailableHats() =>
    [
        .. BlockCatalog.Categories
            .SelectMany(category => BlockCatalog.InCategory(category.Category))
            .Where(descriptor => descriptor.IsHat && !descriptor.Kind.StartsWith("proc.", StringComparison.Ordinal))
            .Select(descriptor => new HatChoice(
                descriptor.Kind,
                BlockLabel.PreviewText(descriptor, Services.BlockText.Current),
                descriptor.Summary)),
    ];

    /// <summary>
    /// Adds a script to the target, starting with the given hat.
    /// </summary>
    /// <remarks>
    /// Positioned below the last script rather than at the origin, because a new script at 0,0 lands on top
    /// of the one already there and the user sees nothing happen. The id is fresh from the document so it
    /// cannot collide with a block that is already there.
    /// </remarks>
    [RelayCommand]
    private void AddScript(string? hatKind = null)
    {
        var descriptor = BlockCatalog.Find(hatKind ?? AvailableHats()[0].Kind);
        if (descriptor is null)
        {
            Report("There is no starting block to add a script with.");
            return;
        }

        var script = new VisualScript
        {
            Id = "script-" + Guid.NewGuid().ToString("N")[..6],
            Name = NextScriptName(),
            Hat = BlockFactory.Create(descriptor, Document.NextBlockId),
            X = 40,
            Y = 24 + (Target.Scripts.Count * 460),
        };

        var result = Editor.AddScript(Target, script);
        if (!result.Applied)
        {
            Report(result.Problem ?? "The script could not be added.");
        }
    }

    /// <summary>
    /// Removes a script.
    /// </summary>
    /// <remarks>
    /// One refusal worth naming: the last script in a target cannot be deleted, because a target with no
    /// scripts emits an empty region and the action stops doing anything at all — and it does so
    /// silently, with no error anywhere. Saying so is better than producing a file that compiles and
    /// does nothing.
    /// </remarks>
    [RelayCommand]
    public void DeleteScript(ScriptViewModel? script)
    {
        if (script is null)
        {
            return;
        }

        if (Target.Scripts.Count <= 1)
        {
            Report("This is the target's only script. Add another one before deleting it — a target with no "
                + "scripts compiles to an action that does nothing.");
            return;
        }

        var result = Editor.DeleteScript(Target, script.Script);
        if (!result.Applied)
        {
            Report(result.Problem ?? "The script could not be deleted.");
        }
    }

    /// <summary>Renames a script, from the strip's name field.</summary>
    public void RenameScript(ScriptViewModel? script, string? name)
    {
        if (script is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Apply(Editor.EditScript(script.Script, name.Trim()));
    }

    /// <summary>
    /// Gives a script a different hat.
    /// </summary>
    /// <remarks>
    /// The body moves onto a new hat block rather than the old one changing its kind, because a hat's id
    /// is the address of its script body. See <see cref="EditScript"/> for why that is not a detail.
    /// </remarks>
    public void SetScriptHat(ScriptViewModel? script, string? hatKind)
    {
        if (script is null || BlockCatalog.Find(hatKind) is not { IsHat: true } descriptor)
        {
            return;
        }

        Apply(Editor.EditScript(script.Script, hat: BlockFactory.Create(descriptor, Document.NextBlockId)));
    }

    /// <summary>Adds a procedure, and shows it in the panel so it can be named.</summary>
    /// <remarks>
    /// Selected immediately rather than left for the user to find: a procedure with a body nobody has
    /// opened is a procedure nobody will fill in, and the strip's list alone does not say which one is
    /// new.
    /// </remarks>
    [RelayCommand]
    private void AddProcedure()
    {
        var name = NextProcedureName();
        var declaration = new ProcedureDeclaration
        {
            Id = Document.NextProcedureId(),
            Name = name,
        };

        var result = Editor.AddProcedure(declaration);
        if (!result.Applied)
        {
            Report(result.Problem ?? "The procedure could not be added.");
            return;
        }

        SelectedProcedure = Procedures.FirstOrDefault(candidate =>
            string.Equals(candidate.Procedure.Id, declaration.Id, StringComparison.Ordinal));
        OnPropertyChanged(nameof(SelectedProcedure));
        OnPropertyChanged(nameof(HasSelectedProcedure));
    }

    /// <summary>Removes a procedure and everything that calls it goes with it.</summary>
    /// <remarks>
    /// The calls are left alone rather than removed with it. The validator then reports each one as
    /// <c>vis-procedure-missing</c> with a sentence that says what to do, and a delete that also tidied
    /// the callers would silently rewrite code the user may have wanted to keep for an undo later.
    /// </remarks>
    [RelayCommand]
    public void DeleteProcedure(ProcedureViewModel? procedure)
    {
        if (procedure is null)
        {
            return;
        }

        var result = Editor.DeleteProcedure(procedure.Procedure);
        if (!result.Applied)
        {
            Report(result.Problem ?? "The procedure could not be deleted.");
        }
    }

    /// <summary>Renames the selected procedure.</summary>
    public void RenameProcedure(ProcedureViewModel? procedure, string? name)
    {
        if (procedure is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Apply(Editor.EditProcedure(procedure.Procedure, name.Trim()));
    }

/// <summary>Says whether the selected procedure hands back a value.</summary>
    /// <remarks>
    /// <para>
    /// Both directions are refused while a call would be left wrong, and the window is what showed why
    /// they have to be: un-ticking the flag on a procedure one call uses as a value was allowed, and the
    /// next thing the user saw was two red diagnostics they had just caused with a checkbox.
    /// </para>
    /// <para>
    /// So ticking is refused while a call discards the result, and un-ticking while a call is waiting for
    /// one. Either way the user is told which calls to change, because "the checkbox is off" is not an
    /// answer to "why".
    /// </para>
    /// </remarks>
    public void SetProcedureReturns(ProcedureViewModel? procedure, bool returns)
    {
        if (procedure is null)
        {
            return;
        }

        var name = procedure.Procedure.Name;
        var asValue = Document.Blocks().Count(block =>
            block.Kind == "proc.call-value" && block.InputText("name") == name);
        var asStatement = Document.Blocks().Count(block =>
            block.Kind is "proc.call" or "proc.call-with" && block.InputText("name") == name);

        if (returns && asStatement > 0)
        {
            Report($"\"{name}\" is called as a statement by {asStatement} call(s), which throw a returned "
                + "value away. Change those to a reporter call first.");
            return;
        }

        if (!returns && asValue > 0)
        {
            Report($"\"{name}\" is already used as a value by {asValue} call(s). Make those plain calls "
                + "before saying it returns nothing.");
            return;
        }

        Apply(Editor.EditProcedure(procedure.Procedure, procedure.Procedure.Name, returning: returns));
    }

    /// <summary>Adds a parameter to the selected procedure.</summary>
    [RelayCommand]
    public void AddParameter(ProcedureViewModel? procedure)
    {
        if (procedure is null)
        {
            return;
        }

        // From the panel's projection rather than from the declaration: the panel's rows are these objects,
        // and building the new list from the declaration would work by accident today and break the moment
        // the projection stopped sharing instances.
        var parameters = procedure.Parameters
            .Select(parameter => new ProcedureParameter { Name = parameter.Name, Type = parameter.Type })
            .ToList();

        parameters.Add(new ProcedureParameter { Name = NextParameterName(procedure), Type = "Any" });

        Apply(Editor.EditProcedure(procedure.Procedure, procedure.Procedure.Name, parameters));
    }

    /// <summary>Removes one parameter, and says so when a call still passes it.</summary>
    public void RemoveParameter(ProcedureViewModel? procedure, ProcedureParameter? parameter)
    {
        if (procedure is null || parameter is null)
        {
            return;
        }

        var parameters = procedure.Parameters
            .Where(candidate => !ReferenceEquals(candidate, parameter))
            .Select(candidate => new ProcedureParameter { Name = candidate.Name, Type = candidate.Type })
            .ToList();

        Apply(Editor.EditProcedure(procedure.Procedure, procedure.Procedure.Name, parameters));
    }

    /// <summary>Renames a parameter.</summary>
    public void RenameParameter(ProcedureViewModel? procedure, ProcedureParameter? parameter, string? name)
    {
        if (procedure is null || parameter is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var parameters = procedure.Parameters
            .Select(candidate => new ProcedureParameter
            {
                Name = ReferenceEquals(candidate, parameter) ? name.Trim() : candidate.Name,
                Type = candidate.Type,
            })
            .ToList();

        Apply(Editor.EditProcedure(procedure.Procedure, procedure.Procedure.Name, parameters));
    }

    /// <summary>Shows a procedure in the My Blocks panel.</summary>
    [RelayCommand]
    public void SelectProcedure(ProcedureViewModel? procedure)
    {
        SelectedProcedure = procedure;
        OnPropertyChanged(nameof(SelectedProcedure));
        OnPropertyChanged(nameof(HasSelectedProcedure));

        if (procedure is not null)
        {
            Message = string.Empty;
        }
    }

    /// <summary>The first name no script has: "script", then "script 2", "script 3".</summary>
    private string NextScriptName()
    {
        var taken = Target.Scripts.Select(script => script.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains("script"))
        {
            return "script";
        }

        for (var index = 2; ; index++)
        {
            var candidate = "script " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The first name no procedure has, counting up from the catalogue's own default.</summary>
    private string NextProcedureName()
    {
        var taken = Document.Procedures.Select(procedure => procedure.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains("myProcedure"))
        {
            return "myProcedure";
        }

        for (var index = 2; ; index++)
        {
            var candidate = "myProcedure" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The first parameter name this procedure is not already using.</summary>
    private string NextParameterName(ProcedureViewModel procedure)
    {
        var taken = procedure.Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[] { "text", "count", "value", "name", "item" })
        {
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return "arg" + (taken.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---- the keyboard --------------------------------------------------------------------------------

    /// <summary>
    /// What the keyboard is carrying, or null when it is carrying nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pick-up-and-drop mode that reuses <see cref="DropPlan"/> rather than reimplementing it, which is
    /// what makes "nothing requires a mouse" true rather than aspirational: the keyboard's Enter does
    /// exactly what the pointer's release does, because both end up in the same place.
    /// </para>
    /// <para>
    /// The cursor is a real <see cref="DropCandidate"/>, walked by <see cref="KeyboardMoves.StepZone"/>.
    /// It is shown on the canvas with the same indicator the pointer uses, so a keyboard user sees where
    /// the block will go in the same terms as a mouse user does.
    /// </para>
    /// </remarks>
    public DragPayload? Carrying { get; private set; }

    /// <summary>The zone the keyboard's block would land in right now, or null.</summary>
    public DropCandidate? CarriedZone { get; private set; }

    /// <summary>Whether a block is in hand.</summary>
    public bool IsCarrying => Carrying is not null;

    /// <summary>What the header says while a block is in hand, so the mode is visible.</summary>
    public string CarryingLine => Carrying is null
        ? string.Empty
        : $"Holding {BlockCatalog.Find(Carrying.Kind)?.Label ?? Carrying.Kind}. "
          + "Arrows choose where it goes, Enter drops it, Escape puts it back.";

    /// <summary>Whether the header should be showing the carrying line.</summary>
    public bool IsCarryingLine => Carrying is not null;

    /// <summary>Moves the selection one statement up or down, within its own body.</summary>
    public void StepSelection(bool up) =>
        SelectBlock(KeyboardMoves.Step(Document, Selected?.Block, up));

    /// <summary>Moves the selected block one place up or down in its body.</summary>
    public void MoveSelected(bool up) => Apply(KeyboardMoves.Nudge(Document, Selected!.Block, up));

    /// <summary>Deletes the selected block.</summary>
    public void DeleteSelected() => Apply(KeyboardMoves.Delete(Document, Selected!.Block));

    /// <summary>Duplicates the selected block and its run.</summary>
    public void DuplicateSelected() => Apply(KeyboardMoves.Duplicate(Document, Selected!.Block));

    /// <summary>Selects the next block, wrapping round.</summary>
    public void CycleSelection(bool forward) =>
        SelectBlock(KeyboardMoves.Cycle(Document, Selected?.Block, forward).FirstOrDefault());

    /// <summary>
    /// Picks the selected block up, or puts it down if it was already in hand.
    /// </summary>
    /// <remarks>
    /// One key for both, so a user does not have to remember which of Space and Enter lifts and which
    /// sets down. The cursor starts at the first zone the shape fits, which is the top of the script the
    /// block came from when there is one — the place a user who has just picked something up expects to
    /// be looking.
    /// </remarks>
    public void PickUpSelected()
    {
        if (Carrying is not null)
        {
            DropCarried();
            return;
        }

        if (Selected is not { } node)
        {
            return;
        }

        Carrying = node.IsPaletteRow
            ? DragPayload.FromPalette(node.Kind)
            : DragPayload.FromDocument(Editor, DocumentLists.Locate(Document, node.Block)!.Value, node.Block);

        CarriedZone = KeyboardMoves.StepZone(Document, null, Carrying.Kind);
        RaiseCarrying();
    }

    /// <summary>Moves the keyboard's cursor to the next or previous zone the block would fit.</summary>
    public void StepKeyboardDropZone(int step)
    {
        if (Carrying is not { } payload)
        {
            return;
        }

        CarriedZone = KeyboardMoves.StepZone(Document, CarriedZone, payload.Kind, step);
        RaiseCarrying();
    }

    /// <summary>Drops the carried block where the cursor is.</summary>
    /// <returns>Whether a drop happened, so the page can mark the key handled.</returns>
    public bool DropCarried()
    {
        if (Carrying is not { } payload)
        {
            return false;
        }

        if (CarriedZone is not { } zone)
        {
            // Nothing this shape fits. Keeping the block in hand is the right answer: the user can keep
            // looking rather than losing what they picked up because they pressed the wrong key.
            return true;
        }

        var result = DropPlan.Apply(Editor, Target, payload, zone, zone.X, zone.Y);
        if (!result.Applied && result.Problem is { } problem)
        {
            Report(problem);
        }

        CancelKeyboardDrag();
        return true;
    }

    /// <summary>Puts a carried block back where it was and forgets it.</summary>
    public bool CancelKeyboardDrag()
    {
        if (Carrying is null)
        {
            return false;
        }

        // The document was never touched while carrying - DropPlan runs at the drop - so putting it back
        // is forgetting. A carry that had already removed the block would need a real inverse here, and
        // this is the reason it does not.
        Carrying = null;
        CarriedZone = null;
        RaiseCarrying();
        return true;
    }

    private void RaiseCarrying()
    {
        OnPropertyChanged(nameof(Carrying));
        OnPropertyChanged(nameof(CarriedZone));
        OnPropertyChanged(nameof(IsCarrying));
        OnPropertyChanged(nameof(CarryingLine));
        OnPropertyChanged(nameof(IsCarryingLine));
    }

    /// <summary>Selects a document block, by id.</summary>
    private void SelectBlock(Block? block) =>
        Select(AllNodes().FirstOrDefault(node => node.Id == block?.Id));

    /// <summary>
    /// Reports the outcome of an editor call that returns a result rather than a command.
    /// </summary>
    /// <remarks>
    /// The same treatment as the command overload: a refusal is information and the message line is where
    /// information goes. An edit that changes nothing says nothing, because "you typed the name it already
    /// had" is not a problem and announcing it teaches the user to ignore the line.
    /// </remarks>
    private void Apply(DocumentEditResult result)
    {
        if (result.Applied)
        {
            Message = string.Empty;
            return;
        }

        if (result.Problem is { Length: > 0 } problem)
        {
            Report(problem);
        }
    }

    /// <summary>Applies a command from the keyboard, saying why if it was refused.</summary>
    private void Apply(DocumentCommand? command)
    {
        if (command is null)
        {
            return;
        }

        var result = Editor.Execute(command);
        if (!result.Applied && result.Problem is { } problem)
        {
            Report(problem);
        }
        else
        {
            Message = string.Empty;
        }
    }

    /// <summary>
    /// Selects the block a diagnostic is about, and centres it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 9.7's "click a diagnostic to select and centre the block". The block is named by id in the
    /// diagnostic, and a diagnostic with nothing to name is left alone rather than clearing the selection:
    /// a document-level problem is not about any particular block, and clicking it should not make the
    /// canvas look empty.
    /// </para>
    /// <para>
    /// A command rather than an event handler because the diagnostics live in an
    /// <c>ItemsControl</c> whose rows are buttons with no reference to this view model — the same reason the
    /// tile's selection is a routed event rather than a command binding.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void SelectDiagnostic(VisualDiagnostic? diagnostic)
    {
        if (diagnostic?.BlockId is { Length: > 0 } blockId)
        {
            SelectById(blockId);
        }
    }

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

        // The inspector rebuilds on selection as well as on edit. Wiring it only to the editor's Changed
        // leaves the panel showing the previous block's rows, which is invisible until a user selects a
        // second block and finds the first one's fields still on screen.
        Inspector.Build(Selected);
    }

    /// <summary>Selects by block id, for a keyboard move or a diagnostic click.</summary>
    public void SelectById(string blockId) => Select(AllNodes().FirstOrDefault(node => node.Id == blockId));

    /// <summary>
    /// Selects a block and focuses one of its slots, for a click on a hole.
    /// </summary>
    /// <remarks>
    /// Part 9.6's "selecting a block in the canvas selects it in the inspector and vice versa", for the
    /// case where the click named a *slot* rather than a block. Both happen, and they happen together:
    /// the block is what the inspector is about, and the slot is which row of it the user was aiming at.
    /// </remarks>
    public void SelectSlot(string blockId, string slotName)
    {
        SelectById(blockId);

        if (Selected?.Id == blockId)
        {
            Inspector.FocusSlot(slotName);
        }
    }

    /// <summary>Every block view model on the canvas, in visual order.</summary>
    /// <remarks>
    /// Walked rather than kept in a list, because the canvas is the authority on what is on it and a
    /// second list would go stale on the first rebuild.
    ///
    /// Procedures are walked here too, and that is the whole reason they are columns: the arrow keys walk
    /// this list, so a procedure body that was not in it would be a place on the canvas the keyboard
    /// cannot reach — which would make "nothing requires a mouse" false for exactly the blocks a user
    /// writes most.
    /// </remarks>
    public IReadOnlyList<BlockNodeViewModel> AllNodes()
    {
        var nodes = new List<BlockNodeViewModel>();

        foreach (var column in Columns)
        {
            if (column is ScriptViewModel script)
            {
                nodes.Add(script.Hat);
            }

            Walk(column.Body, nodes);
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

        // The replacement is a different object, so the inspector's rows are built over different view
        // models and every one of them has to be rebuilt rather than reused.
        Inspector.Build(Selected);
    }
}
