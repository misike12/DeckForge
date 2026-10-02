namespace DeckForge.Core.Visual;

/// <summary>What kind of code artefact a target writes into.</summary>
/// <remarks>
/// The sprite analogue from Part 5.1. Only <see cref="Action"/> writes today; the rest exist so the
/// document format does not have to change when Phase 9 adds them, and so an imported or hand-edited
/// document cannot silently retarget a script from an action into a widget handler.
/// </remarks>
public enum TargetKind
{
    /// <summary>An action executor, spliced into the action's block region.</summary>
    Action,

    /// <summary>A widget node's event handler.</summary>
    WidgetHandler,

    /// <summary>A setup-flow step hook.</summary>
    ConfigFlowHook,

    /// <summary>An integration lifecycle surface, such as initialisation or teardown.</summary>
    Lifecycle,
}

/// <summary>Where a variable lives.</summary>
public enum VariableScope
{
    /// <summary>A local the generated method declares.</summary>
    Local,

    /// <summary>A variable shared with the host through the SDK's user-variable API.</summary>
    Host,
}

/// <summary>One action, widget handler, setup-flow hook or lifecycle surface that holds scripts.</summary>
/// <param name="Id">Stable identity, such as <c>action:log-message</c>.</param>
/// <param name="Name">The display name, which is the action's name.</param>
/// <param name="Kind">Which kind of artefact this writes into.</param>
/// <param name="TargetFile">The file, relative to the plugin project, the region lives in.</param>
/// <param name="AnchorId">The method signature the region is spliced into.</param>
public sealed class VisualTarget
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public TargetKind Kind { get; set; } = TargetKind.Action;

    public string TargetFile { get; set; } = string.Empty;

    public string AnchorId { get; set; } = "ExecuteAsync";

    public List<VisualScript> Scripts { get; set; } = [];

    /// <summary>Every block in every script of this target, hats included.</summary>
    public IEnumerable<Block> Blocks() =>
        Scripts.SelectMany(script => script.Blocks());
}

/// <summary>
/// One script: a hat, a stack, and where it sits on the canvas.
/// </summary>
/// <remarks>
/// The position is part of the document. Arranging scripts is the user's spatial memory of their own
/// program, and losing it on reload is the kind of small betrayal that makes an editor feel
/// unreliable.
/// </remarks>
public sealed class VisualScript
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "script";

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Whether the whole script is switched off, which the emitter and the interpreter both honour.</summary>
    public bool Disabled { get; set; }

    /// <summary>The hat that starts this script.</summary>
    public Block Hat { get; set; } = new();

    public List<Block> Body { get; set; } = [];

    /// <summary>The hat and every statement beneath it.</summary>
    public IEnumerable<Block> Blocks()
    {
        yield return Hat;

        foreach (var statement in Body)
        {
            foreach (var block in statement.Walk())
            {
                yield return block;
            }
        }
    }
}

/// <summary>A declared variable, local or shared with the host.</summary>
public sealed class VariableDeclaration
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The declared type, as the SDK's own three kinds: <c>Text</c>, <c>Numeric</c> or <c>Boolean</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately the SDK's vocabulary rather than C#'s. A host variable is created through
    /// <c>IUserVariableApi.CreateAsync</c>, which takes <c>VariableType</c>, and a document that says
    /// <c>number</c> where the SDK says <c>Numeric</c> would need translating in two places forever.
    /// </remarks>
    public string Type { get; set; } = "Text";

    public string? Initial { get; set; }

    public VariableScope Scope { get; set; } = VariableScope.Local;
}

/// <summary>A declared list. Lists are local to the generated method; the SDK has no list surface.</summary>
public sealed class ListDeclaration
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The element type, in the same vocabulary as <see cref="VariableDeclaration.Type"/>.</summary>
    public string ItemType { get; set; } = "Text";

    public List<string> Initial { get; set; } = [];
}

/// <summary>One parameter of a procedure.</summary>
public sealed class ProcedureParameter
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "Any";
}

/// <summary>
/// A user-defined procedure, emitted as a local function inside the block region.
/// </summary>
/// <remarks>
/// Local rather than a class member because the generated action class is <c>sealed</c>: see Part 4.2
/// of <c>visual.md</c>. The consequence worth remembering is that a procedure belongs to one target
/// file, so a shared library of procedures across actions is a Phase 10 question and not a promise.
/// </remarks>
public sealed class ProcedureDeclaration
{
    /// <summary>
    /// This procedure's stable id, which is how its body is addressed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in Phase 7, when a procedure became something a user can edit rather than something the
    /// emitter reads. The body has to be addressable the same way a script's body is — by id, because
    /// <see cref="BodyRef"/> is ids all the way down and a path of indices goes stale the moment anything
    /// above it moves.
    /// </para>
    /// <para>
    /// It is an id and not the name, and the reason is undo: renaming a procedure must not invalidate the
    /// body reference an undo entry is holding. With the name as the address, renaming and then undoing
    /// an edit made inside the body would resolve the reference to nothing.
    /// </para>
    /// <para>
    /// Documents written before this existed deserialise with an empty string, and
    /// <see cref="VisualProjectJson.TryLoad"/> fills one in rather than refusing the file — a canvas that
    /// saved perfectly well should not stop opening because a field was added.
    /// </para>
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<ProcedureParameter> Parameters { get; set; } = [];

    /// <summary>Whether the procedure returns a value, which makes its call reporter-shaped.</summary>
    public bool Returns { get; set; }

    public List<Block> Body { get; set; } = [];
}

/// <summary>
/// The whole visual document: one file per plugin workspace, stored under
/// <c>.deckforge/visual/project.visual.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The source of truth for what the canvas shows. The generated plugin never contains this file; it
/// contains the C# the document compiles to, so a workspace can be read with the Visual page absent.
/// </para>
/// <para>
/// <see cref="DocumentId"/> is written into the generated region's header comment. When a region's
/// recorded document id and body hash both match, opening it is a file read rather than a parse
/// (Part 24.1); when they do not, the region falls back to the subset parser and anything
/// unrecognised is preserved rather than dropped.
/// </para>
/// </remarks>
public sealed class VisualProject
{
    /// <summary>The schema version this build writes.</summary>
    /// <remarks>
    /// A legacy <c>.blocks.json</c> is not a version of this format at all: it is detected structurally
    /// (it has <c>statements</c> and no <c>targets</c>) and migrated, rather than by a version number,
    /// because <c>BlockProgram.CurrentVersion</c> is also 1 and reading it as this format would
    /// deserialize an empty document without complaining. See <see cref="VisualProjectJson.TryLoad"/>.
    /// </remarks>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>A short id for this document, recorded in the generated region header.</summary>
    public string DocumentId { get; set; } = string.Empty;

    public List<VisualTarget> Targets { get; set; } = [];

    public List<VariableDeclaration> Variables { get; set; } = [];

    public List<ListDeclaration> Lists { get; set; } = [];

    public List<ProcedureDeclaration> Procedures { get; set; } = [];

    /// <summary>Every block in the document, across every target, script, procedure and slot.</summary>
    public IEnumerable<Block> Blocks()
    {
        foreach (var target in Targets)
        {
            foreach (var block in target.Blocks())
            {
                yield return block;
            }
        }

        foreach (var procedure in Procedures)
        {
            foreach (var statement in procedure.Body)
            {
                foreach (var block in statement.Walk())
                {
                    yield return block;
                }
            }
        }
    }

    /// <summary>The target with the given id, or null.</summary>
    public VisualTarget? FindTarget(string id) =>
        Targets.FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.Ordinal));

    /// <summary>The procedure with the given name, or null.</summary>
    public ProcedureDeclaration? FindProcedure(string name) =>
        Procedures.FirstOrDefault(procedure => string.Equals(procedure.Name, name, StringComparison.Ordinal));

    /// <summary>The procedure with the given id, or null.</summary>
    public ProcedureDeclaration? FindProcedureById(string id) =>
        Procedures.FirstOrDefault(procedure => string.Equals(procedure.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Gives every procedure an id, and returns how many needed one.
    /// </summary>
    /// <remarks>
    /// Called after a document is read and before one is written, so a canvas saved before Phase 7 gains
    /// ids on first load rather than being refused — and so two saves of the same document produce the
    /// same text, which <see cref="VisualStore.IsDirty"/> depends on. Ids are derived from the index, not
    /// randomly, so opening and saving a document twice does not churn the file.
    /// </remarks>
    public int EnsureProcedureIds()
    {
        var filled = 0;

        for (var index = 0; index < Procedures.Count; index++)
        {
            var procedure = Procedures[index];

            if (procedure.Id.Length > 0)
            {
                continue;
            }

            procedure.Id = "proc" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            filled++;
        }

        return filled;
    }

    /// <summary>
    /// The next free block id in the form the design uses, <c>b1</c>, <c>b2</c>, …
    /// </summary>
    /// <remarks>
    /// A counter rather than a GUID. The sidecar is meant to be read and diffed, and a fresh document
    /// with hexadecimal noise in every id makes a three-block change look like a rewrite.
    /// </remarks>
    public string NextBlockId()
    {
        var highest = 0;
        foreach (var block in Blocks())
        {
            if (block.Id.Length > 1 && block.Id[0] == 'b'
                && int.TryParse(block.Id.AsSpan(1), out var number) && number > highest)
            {
                highest = number;
            }
        }

        return "b" + (highest + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A fresh document id.
    /// </summary>
    /// <remarks>
    /// Short and lowercase-hex so it reads well inside a C# comment. It is an identity, not a secret,
    /// and it never reaches the host.
    /// </remarks>
    public static string NewDocumentId() =>
        Guid.NewGuid().ToString("N")[..7];
}
