using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckForge.CodeGen.Generation;
using DeckForge.Core.Visual;

namespace DeckForge.App.ViewModels.Visual;

// Part 9.6's inspector. The rows read through to the document and write through the editor; nothing here
// holds a value of its own, so the panel cannot disagree with the canvas about what a block contains.

/// <summary>
/// The inspector's editors for one selected block: its slots, its dropdowns, and its shapes.
/// </summary>
/// <remarks>
/// <para>
/// Part 9.6 lists ten editor types by slot type, and the design's own words for the rest of the panel are
/// "mirrors the same fields with labels, summaries, a docs deep link, and a one-block C# preview". This is
/// that panel's model: the rows, the values, and the commands that write them back.
/// </para>
/// <para>
/// Every write goes through <see cref="DocumentEditor"/>, and none of them holds a reference to the
/// document. A row's value is read from the block when it is built and re-read after every edit, so the
/// inspector cannot disagree with the canvas about what is in a block — which is the failure mode of an
/// editor that keeps its own copy and writes it back on a button.
/// </para>
/// </remarks>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly VisualEditorViewModel _editor;
    private string _comment = string.Empty;

    public InspectorViewModel(VisualEditorViewModel editor) => _editor = editor;

    /// <summary>
    /// The page's view model, so a row can say something in the message line.
    /// </summary>
    /// <remarks>
    /// Exposed rather than passing the editor's message method in as a delegate, because a row that
    /// refuses — clearing a boolean, say — has to tell the user why somewhere they are already looking.
    /// Two rows each holding their own copy of "how to report" is two places to keep in step with the
    /// message line's own rules.
    /// </remarks>
    internal VisualEditorViewModel Editor => _editor;

    /// <summary>What the panel says when nothing is selected.</summary>
    public string EmptyText => "Nothing selected. Click a block to see what it does.";

    /// <summary>The editors for the selected block's slots, in catalogue order.</summary>
    public ObservableCollection<SlotEditor> Slots { get; private set; } = [];

    /// <summary>
    /// The selected block's comment, edited as text.
    /// </summary>
    /// <remarks>
    /// A property rather than a row object, because a comment is one field for the whole block rather
    /// than one of several, and a row type with exactly one instance is a row type that will grow a second
    /// instance for something else later. It writes through <see cref="SetComment"/>, so the comment is on
    /// the undo stack like every other edit.
    /// </remarks>
    public string Comment
    {
        get => _comment;
        set
        {
            if (!SetProperty(ref _comment, value))
            {
                return;
            }

            SetComment(value);
            OnPropertyChanged(nameof(HasComment));
        }
    }

    /// <summary>Whether the selected block carries a comment.</summary>
    public bool HasComment => !string.IsNullOrWhiteSpace(_comment);

    /// <summary>What the comment row says above the box.</summary>
    public string CommentHelp =>
        "A note for whoever reads this canvas next, including you. It is saved with the document and is "
        + "carried into the generated code as a comment.";

    /// <summary>
    /// Writes the comment through the editor, unless it has not actually changed.
    /// </summary>
    /// <remarks>
    /// The equality check is not a micro-optimisation. <c>Build</c> runs on every edit and every
    /// selection, and a setter that always wrote would put an undo entry on the stack for looking at a
    /// block — so Ctrl+Z after clicking around would undo the click.
    /// </remarks>
    private void SetComment(string? value)
    {
        if (Selected?.Block is not { } block)
        {
            return;
        }

        var after = string.IsNullOrWhiteSpace(value) ? null : value;
        if (string.Equals(block.Comment, after, StringComparison.Ordinal))
        {
            return;
        }

        _editor.Editor.Execute(new SetComment(block.Id, after));
    }
    /// <summary>The editors for the selected block's inline dropdowns.</summary>
    public ObservableCollection<MenuEditor> Menus { get; private set; } = [];

    /// <summary>The one-block C# preview, or empty when there is no selection.</summary>
    public string Preview { get; private set; } = string.Empty;

    /// <summary>
    /// The slot the user clicked, or null.
    /// </summary>
    /// <remarks>
    /// Kept so the view can scroll one row into view and give it the focus ring. Part 9.6 wants "selecting
    /// a block in the canvas selects it in the inspector and vice versa", and clicking a hole on the canvas
    /// is also a click on its block — so without this the inspector highlights every row of the block and
    /// none of them in particular.
    /// </remarks>
    public SlotEditor? FocusedSlot { get; private set; }

    /// <summary>Whether there is a preview to show.</summary>
    public bool HasPreview => Preview.Length > 0;

    /// <summary>
    /// The target's scripts as C#, for Part 9.7's generated pane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted through <see cref="VisualProgramWriter"/> on a real writer instance rather than by calling
    /// <see cref="VisualEmitter"/> and reading strings back out of a temp directory. The pane is labelled
    /// "this is what Save writes", and a preview assembled by hand is exactly the thing that would make
    /// that label false — the emitter's own splices and host guards are most of what it produces, and they
    /// are the parts a hand-written preview would omit.
    /// </para>
    /// <para>
    /// Empty when nothing has compiled, rather than showing the last thing that did. A stale preview
    /// labelled as current is the one failure mode this pane cannot have: a user who has just dragged a
    /// block would see old code and believe it.
    /// </para>
    /// </remarks>
    public string GeneratedCode { get; private set; } = string.Empty;

    /// <summary>Whether the pane has anything to show.</summary>
    public bool HasGeneratedCode => GeneratedCode.Length > 0;

    /// <summary>What the pane says when it has nothing, which is different from a failure.</summary>
    public string GeneratedEmptyText =>
        "Nothing to write yet. Open a target's scripts, or press Save to write this document's code.";

    /// <summary>Rebuilds the generated-code pane for a document.</summary>
    public void BuildCode(VisualProject project)
    {
        GeneratedCode = Compile(project);
        OnPropertyChanged(nameof(GeneratedCode));
        OnPropertyChanged(nameof(HasGeneratedCode));
        OnPropertyChanged(nameof(GeneratedEmptyText));
    }

    /// <summary>
    /// The C# a target's scripts compile to, in memory.
    /// </summary>
    /// <remarks>
    /// A throwaway directory, because the writer is built around writing files — it splices into an
    /// existing action file, creates one if it is absent, and does both through real file I/O. Rewriting
    /// that to a string would be a second emitter with a second set of bugs, and the pane's value is that it
    /// is the same code path Save uses.
    /// </remarks>
    private static string Compile(VisualProject project)
    {
        try
        {
            var builder = new System.Text.StringBuilder();
            var markers = "// <macrodeck-blocks>";

            foreach (var target in project.Targets)
            {
                var (code, problems) = VisualEmitter.CompileTarget(target, project);

                builder.AppendLine(markers);
                builder.AppendLine($"// {target.Name} ({target.Kind}) — {target.TargetFile}");
                builder.AppendLine(code.TrimEnd());

                // The compile problems come along rather than being left for the diagnostics pane alone,
                // because the pane's own claim is that this is what Save writes, and a preview that hides
                // the reason a target could not be written is not that.
                foreach (var problem in problems)
                {
                    builder.AppendLine($"// {problem.BlockId}: {problem.Message}");
                }

                builder.AppendLine("// </macrodeck-blocks>");
                builder.AppendLine();
            }

            return builder.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The pane is a convenience. Anything worse than a file error is a bug worth surfacing, but a
            // temp directory that cannot be written is not a reason to take the canvas down.
            return $"// The generated code could not be previewed: {ex.Message}";
        }
    }

    /// <summary>
    /// The one-block C# preview for the selected block.
    /// </summary>
    /// <remarks>
    /// Quoted from the catalogue row rather than from the emitter, and the view says so. Emitting one block
    /// properly needs a whole action around it — a method signature, a body, the host check — and a preview
    /// that claimed to be what Save writes and was not would be worse than one that admits it is the row's
    /// own text.
    /// </remarks>
    /// <summary>
    /// The block the inspector is showing, which is the page's current selection.
    /// </summary>
    /// <remarks>
    /// Held here rather than read back out of the page because the comment setter needs it and the page
    /// does not pass one: a setter that had to be handed the block to comment would be a setter called
    /// from two places with different arguments. Exposed rather than left as a bare field because the
    /// panel is the only thing that should know which block it is editing.
    /// </remarks>
    public BlockNodeViewModel? Selected { get; private set; }
    public string SelectedPreview { get; private set; } = string.Empty;

    /// <summary>
    /// Rebuilds the panel for a selection, or empties it when there is none.
    /// </summary>
    /// <remarks>
    /// Called after every edit as well as after every selection, because the canvas rebuilds on both and a
    /// panel holding the previous selection's rows would show values the document no longer has.
    /// </remarks>
    public void Build(BlockNodeViewModel? selected)
    {
        Selected = selected;
        _comment = selected?.Comment ?? string.Empty;
        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(CommentHelp));

        var wanted = (selected?.Slots ?? []).ToList();

        // Rows are kept when the set of slots has not changed, and only replaced when it has. This runs
        // after every keystroke in a slot field, and replacing the rows destroyed the TextBox being typed
        // into - so the field lost focus at the end of the first character and the symptom was a box that
        // ate every letter. `previous` was computed here and thrown away, which is what it was for.
        var sameSlots = wanted.Count == Slots.Count
            && wanted.Select(slot => slot.Name).SequenceEqual(Slots.Select(row => row.Name), StringComparer.Ordinal);

        if (!sameSlots)
        {
            Slots = new ObservableCollection<SlotEditor>(
                wanted.Select(slot =>
                {
                    var row = new SlotEditor(this, slot);

                    // The link back, so a canvas control can reach the row for the slot it is standing in.
                    // A row that outlives a rebuild must be re-pointed, or it would write through to the
                    // block that has just been replaced.
                    slot.Editor = row;
                    return row;
                }));
        }
        else
        {
            foreach (var slot in wanted)
            {
                if (Slots.FirstOrDefault(row => row.Name == slot.Name) is { } row)
                {
                    row.Retarget(slot);
                }
            }
        }

        var wantedMenus = (selected?.Menus ?? []).ToList();
        var sameMenus = wantedMenus.Count == Menus.Count
            && wantedMenus.Select(menu => menu.Name).SequenceEqual(Menus.Select(row => row.Name), StringComparer.Ordinal);

        if (!sameMenus || selected is null)
        {
            Menus = new ObservableCollection<MenuEditor>(
                wantedMenus.Select(menu => new MenuEditor(this, menu, selected!)));
        }
        else
        {
            foreach (var menu in wantedMenus)
            {
                if (Menus.FirstOrDefault(row => row.Name == menu.Name) is { } row)
                {
                    row.Retarget(menu);
                }
            }
        }

        OnPropertyChanged(nameof(IsPlain));

        Preview = selected is null ? string.Empty : PreviewBlock(selected);
        SelectedPreview = Preview;

        OnPropertyChanged(nameof(SelectedPreview));

        // A rebuild replaces every row, so a row that was focused is remembered by name and focused again.
        // The symptom of not doing this is specific and confusing: the field drops focus at the character
        // after the one typed, which reads as a field that eats every second letter — and the fix is not
        // to rebuild less often, because the canvas rebuilds on every edit regardless, it is to put the
        // focus back on the row that had it.
        FocusedSlot = Focus is { } name ? Slots.FirstOrDefault(slot => slot.Name == name) : null;

        OnPropertyChanged(nameof(FocusedSlot));
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(Menus));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(EmptyText));

    }

    /// <summary>
    /// The name of the slot the view should focus, or null.
    /// </summary>
    /// <remarks>
    /// A name rather than a row, because a row does not survive a rebuild and the view has to say what it
    /// wants after the fact. Set by <see cref="FocusSlot"/> and cleared by a selection change.
    /// </remarks>
    public string? Focus { get; private set; }

    /// <summary>Asks for one row to be brought into view and focused.</summary>
    public void FocusSlot(string name)
    {
        Focus = name;
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(FocusedSlot));
    }

    /// <summary>
    /// The C# a single block emits, for the preview.
    /// </summary>
    /// <remarks>
    /// Emitted through the real emitter rather than assembled from the row, because the point of a preview
    /// is to show what will actually be written. A preview assembled from the descriptor's own template
    /// would agree with the descriptor and could disagree with the emitter, which is the one thing a
    /// preview exists to catch.
    /// </remarks>
    private static string PreviewBlock(BlockNodeViewModel node)
    {
        var descriptor = node.Descriptor;

        if (descriptor is null)
        {
            return $"{node.Kind} — a kind this build does not know, so it cannot be emitted.";
        }

        var expression = descriptor.Sdk.Expression;

        // Every row with an empty expression is a hat or a modifier, and neither emits anything on its own —
        // a hat is the method signature and a modifier changes how the emitter walks. Saying so is more
        // useful than an empty panel, which a user cannot tell apart from a rendering failure, and it
        // points at the reason rather than at the symptom.
        if (string.IsNullOrWhiteSpace(expression))
        {
            return descriptor.IsHat
                ? "A hat: it becomes the method signature, and emits nothing of its own."
                : "A modifier: it changes how the statements after it are emitted.";
        }

        // Multi-line expressions are joined rather than shown as-is, because a two-line loop in a
        // two-hundred-pixel column is unreadable and the point is the shape of the call, not its bracing.
        var preview = string.Join(
            " · ",
            expression.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        return preview;
    }

    /// <summary>Whether the selected block has nothing to fill in.</summary>
    /// <remarks>
    /// Said explicitly rather than left blank. A panel that shows only headings under a block with no
    /// fields reads as a panel that failed to load, and a user cannot tell that from one that has.
    /// </remarks>
    public bool IsPlain => Slots.Count == 0 && Menus.Count == 0;

    /// <summary>Applies one command and re-reads the panel.</summary>
    internal void Apply(DocumentCommand command, string? problem = null)
    {
        var result = _editor.Editor.Execute(command);

        if (!result.Applied && result.Problem is { } refused)
        {
            _editor.Report(refused);
            return;
        }

        // Success clears the refusal line rather than leaving whatever was last said there, because a
        // message that stays after the thing it complained about has been fixed is a message about a
        // problem that no longer exists.
        _editor.Report(problem ?? string.Empty);

        OnPropertyChanged(nameof(IsPlain));
    }
}

/// <summary>One slot's editor row.</summary>
/// <remarks>
/// Holds no value of its own: <see cref="Value"/> reads through to the block every time it is asked, and
/// <see cref="Commit"/> is the only thing that writes. An editor that cached the value it last wrote would
/// be a second source of truth, and undo would then have to update it too.
/// </remarks>
public sealed partial class SlotEditor : ObservableObject
{
    private readonly InspectorViewModel _panel;
    private readonly VisualEditorViewModel _editor;
    private string _comment = string.Empty;
    private string _draft = string.Empty;

    internal SlotEditor(InspectorViewModel panel, SlotViewModel slot)
    {
        _panel = panel;
        _editor = panel.Editor;
        Model = slot;

        _draft = SlotValue.Read(slot.Owner.Block, slot.Descriptor).Text;
    }

    /// <summary>Whether the draft is anything other than what the block holds.</summary>
    /// <remarks>
    /// <see cref="Text"/> reads through to the block whenever the draft matches it, and only falls back to
    /// the draft while it differs. That single rule is what stops the panel fighting itself: every edit
    /// rebuilds the canvas and therefore the rows, so a row that always preferred its own copy would show
    /// "10" over a block holding 7 as soon as anything else happened, and a row that always preferred the
    /// block would discard the character being typed the moment the binding re-read it.
    /// </remarks>
    private bool IsDrafting => !string.Equals(_draft, Value.Text, StringComparison.Ordinal);

    /// <summary>The slot this row edits.</summary>
    public SlotViewModel Model { get; private set; }

    /// <summary>
    /// Points this row at a fresh slot view model for the same slot, keeping the row itself alive.
    /// </summary>
    /// <remarks>
    /// Every edit replaces the block behind the slot, so a row that kept its old model would read and write
    /// the previous block - the one the user had just changed. Replacing the *model* rather than the row is
    /// what lets the panel survive a keystroke: the row owns the TextBox, the model owns the document, and
    /// only the second of those has to change.
    /// </remarks>
    /// <param name="slot">The new view model for the same slot name.</param>
    internal void Retarget(SlotViewModel slot)
    {
        Model = slot;
        _draft = SlotValue.Read(slot.Owner.Block, slot.Descriptor).Text;

        foreach (var name in new[]
                 {
                     nameof(Label), nameof(Help), nameof(Type), nameof(Text), nameof(Value),
                     nameof(HasChild), nameof(ChildLabel), nameof(IsNumber), nameof(IsBoolean),
                     nameof(IsReference),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>The slot's key in the catalogue, which is also its key in the document.</summary>
    public string Name => Model.Name;

    /// <summary>The label shown beside the editor.</summary>
    public string Label => Model.Descriptor.Label is { Length: > 0 } label ? label : Name;

    /// <summary>One line saying what this slot is for.</summary>
    public string Help => Model.Descriptor.Required
        ? "Required. The document cannot be saved with it empty."
        : "Optional.";

    /// <summary>The type, which decides which editor the view shows.</summary>
    public SlotType Type => Model.Descriptor.Type;

    /// <summary>What the slot holds now, read from the block.</summary>
    public SlotValue Value => SlotValue.Read(Model.Owner.Block, Model.Descriptor);

    /// <summary>
    /// The text in the editor.
    /// </summary>
    /// <remarks>
    /// Writes go to <see cref="Commit"/>, and reads come from the block. The draft only exists while the
    /// user is typing in it — see <see cref="Commit"/> — because a field that writes on every keystroke
    /// puts 800ms of history on the stack for one word, and a field that never writes until blur loses the
    /// value when the panel rebuilds underneath it.
    /// </remarks>
    public string Text
    {
        get => IsDrafting ? _draft : Value.Text;
        set
        {
            // Only a genuine change from what is on screen commits. The property-changed binding re-reads
            // this after every edit, and by then the canvas has rebuilt the row — so without this, typing
            // "7" into a count of 10 commits 7, the rebuild hands the setter back the freshly-read "7",
            // and the next keystroke appends to that instead of replacing it.
            //
            // The comparison is against the draft rather than only against the block, because the draft is
            // the only thing that knows what the field is actually showing right now.
            if (string.Equals(_draft, value, StringComparison.Ordinal) && !IsDrafting)
            {
                return;
            }

            _draft = value;
            Commit(value);
        }
    }

    /// <summary>Whether a nested reporter is in the hole rather than a literal.</summary>
    public bool HasChild => Value.HasBlock;

    /// <summary>What the nested reporter is, for the row's second line.</summary>
    public string ChildLabel => Value.HasBlock ? $"{Value.BlockLabel}  ·  drop something else in, or clear it" : string.Empty;

    /// <summary>Whether the editor should offer a spinner.</summary>
    public bool IsNumber => Type == SlotType.Number;

    /// <summary>Whether the editor should offer a toggle.</summary>
    public bool IsBoolean => Type == SlotType.Boolean;

    /// <summary>Whether the editor should offer a picker over the document's own names.</summary>
    public bool IsReference => Type is not (SlotType.Text or SlotType.Number or SlotType.Boolean or SlotType.Any);

    /// <summary>
    /// Whether the clear button should be shown at all.
    /// </summary>
    /// <remarks>
    /// Hidden for a boolean, because clearing one produces a required slot with nothing in it and the
    /// validator turns that into an error. A button that can only produce an unsaveable document is worse
    /// than no button, even one that explains itself.
    /// </remarks>
    public bool CanClear => Type != SlotType.Boolean;

    /// <summary>Whether the row shows a validation complaint.</summary>
    public string? Problem => SlotValue.Problem(Model.Descriptor, Text);

    /// <summary>The block's own value as a number, for the spinner.</summary>
    public double Number
    {
        get => Value.Number;
        set => Text = SlotValue.FormatNumber(value);
    }

    /// <summary>The block's own value as a flag, for the toggle.</summary>
    public bool Boolean
    {
        get => Value.Boolean;
        set => Text = value ? "true" : "false";
    }

    /// <summary>Puts the value back from whatever is in the editor, after a refusal.</summary>
    /// <remarks>
    /// Exists because a refused keystroke leaves the field showing something the document does not
    /// contain. Without this the field would keep the bad text and the next keystroke would build on it, so
    /// one typo into a number slot would poison every attempt to fix it.
    /// </remarks>
    [RelayCommand]
    private void Revert()
    {
        _draft = Value.Text;
        RaiseAll();
    }

    /// <summary>
    /// Empties the slot, for the slots where that is a real answer.
    /// </summary>
    /// <remarks>
    /// A boolean slot is refused, and the refusal is the interesting part. Emptying one leaves a required
    /// slot with nothing in it, which the validator reports as an error — so the button would appear to
    /// work and leave a document behind that nothing can compile. Every other type can be genuinely
    /// cleared, and for a reference slot it is the only way to change the value at all, since those are
    /// picked rather than typed.
    /// </remarks>
    [RelayCommand]
    private void Clear()
    {
        if (Type == SlotType.Boolean)
        {
            _editor.Report("A condition has to be true or false. Untick it rather than clearing it.");
            return;
        }

        _draft = string.Empty;
        _panel.Apply(new BindSlot(Model.Owner.Id, Name, SlotValue.Current(Model.Owner.Block, Model.Descriptor), null));
        RaiseAll();
    }

    private void Commit(string text)
    {
        var problem = SlotValue.Problem(Model.Descriptor, text);

        if (problem is not null)
        {
            // Refused, and the row keeps the text so the user can see what they typed and correct it. The
            // document is untouched, so there is nothing to undo either.
            ProblemChanged();
            return;
        }

        _panel.Apply(
            new BindSlot(Model.Owner.Id, Name, SlotValue.Current(Model.Owner.Block, Model.Descriptor), SlotValue.From(Model.Descriptor, text)));

        RaiseAll();
    }

    private void ProblemChanged()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Text));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Boolean));
        OnPropertyChanged(nameof(HasChild));
        OnPropertyChanged(nameof(ChildLabel));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(CanClear));
    }
}

/// <summary>One inline dropdown's editor row.</summary>
public sealed partial class MenuEditor : ObservableObject
{
    private readonly InspectorViewModel _panel;
    private readonly BlockNodeViewModel _block;

    internal MenuEditor(InspectorViewModel panel, MenuViewModel menu, BlockNodeViewModel block)
    {
        _panel = panel;
        Model = menu;
        _block = block;
    }

    /// <summary>The dropdown this row edits.</summary>
    public MenuViewModel Model { get; private set; }

    /// <summary>
    /// Points this row at a fresh menu view model for the same menu, keeping the row alive.
    /// </summary>
    /// <remarks>
    /// The same reason as <see cref="SlotEditor.Retarget"/>: the block behind the dropdown is a new object
    /// after every edit, and a row holding the old one would keep writing to the block the user has just
    /// changed.
    /// </remarks>
    /// <param name="menu">The new view model for the same menu name.</param>
    internal void Retarget(MenuViewModel menu)
    {
        Model = menu;

        foreach (var name in new[] { nameof(Label), nameof(Options), nameof(Selected) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>The dropdown's key in the catalogue.</summary>
    public string Name => Model.Name;

    /// <summary>The label shown beside the dropdown.</summary>
    public string Label => Model.Descriptor.Label is { Length: > 0 } label ? label : Name;

    /// <summary>The options, in the order the catalogue lists them.</summary>
    public IReadOnlyList<string> Options => Model.Options;

    /// <summary>The chosen option, read from the block rather than cached.</summary>
    public string Selected => SlotValue.ReadMenu(_block.Block, Model.Descriptor);

    /// <summary>Chooses an option.</summary>
    [RelayCommand]
    private void Choose(string option)
    {
        if (!Model.Options.Contains(option, StringComparer.Ordinal) || string.Equals(Selected, option, StringComparison.Ordinal))
        {
            return;
        }

        // Guarded on the option list, because the value comes from a binding and a binding will happily
        // pass anything a converter produces. A document with an option no menu declares is emitted as
        // whatever the emitter's default branch does, which is the wrong answer quietly.
        _panel.Apply(new EditField(_block.Id, Name, Selected, option, true), $"Using {option}.");
        OnPropertyChanged(nameof(Selected));
    }
}