using System.Text;
using DeckForge.Core.Visual.Runtime;

namespace DeckForge.Core.Visual.Providers;

/// <summary>
/// The contract a third-party block provider implements, from Part 23.1 of <c>visual.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// DeckForge is built for additive extension, and this is the same shape
/// <c>IProjectContentContributor</c> already has: a type, registered in the container, discovered from an
/// extension assembly, off by default. What makes this one different is that it is
/// <strong>higher-trust</strong> than a page, and the interface is arranged so the extra trust buys
/// something. A provider's emitter writes into a user's source tree, so a block that cannot be checked is a
/// block nobody can hold to the guarantees the built-in ones have — which is why all three members are
/// required and none of them has a default implementation.
/// </para>
/// <para>
/// <strong>Why all three are required</strong> (§23.2): a palette row without an emitter produces a block
/// that draws and does nothing, and an emitter without an interpreter step produces a block the simulator
/// cannot preview — which breaks the central promise that the canvas shows what will run. Making the members
/// non-optional makes that impossible to satisfy by accident. A provider that genuinely cannot be simulated
/// implements the step as one <see cref="ExecutionStepKind.Error"/> step and the stage says "partially
/// simulated", rather than pretending.
/// </para>
/// <para>
/// <strong>Ids are namespaced by <see cref="Id"/>.</strong> A contributed id can therefore never collide with
/// a catalogue id, and a document that uses one records the provider that supplied it — which is what lets a
/// machine without the extension say "this block is from something you have not enabled" instead of
/// corrupting the file or silently dropping a block.
/// </para>
/// </remarks>
public interface IVisualBlockProvider
{
    /// <summary>
    /// The provider's stable id, and the namespace every block it contributes is filed under.
    /// </summary>
    /// <remarks>
    /// Lowercase and dotted, like every other id in this project, because it becomes the first segment of
    /// <see cref="BlockDescriptor.Kind"/> and a document stores that verbatim. It is also what
    /// <c>vis-provider-missing</c> names, so it has to read as something a user can type.
    /// </remarks>
    string Id { get; }

    /// <summary>
    /// Where the provider's categories appear in the rail, after the built-in ones.
    /// </summary>
    /// <remarks>
    /// Ties are broken by id so two providers loading together cannot produce two different palettes
    /// depending on which one the container happened to enumerate first.
    /// </remarks>
    int Order { get; }

    /// <summary>
    /// Categories this provider adds. May be empty when it only extends the built-in ones.
    /// </summary>
    /// <remarks>
    /// A category is a <see cref="BlockCategory"/> enum member, and every one of the eleven the built-in
    /// catalogue uses is spoken for — which is why the honest space for a provider is
    /// <see cref="BlockCategory.Media"/>, the one the built-in catalogue deliberately leaves empty. Adding a
    /// thirteenth member would mean every <c>switch</c> over the enum, and the rail, had to know about it.
    /// </remarks>
    IReadOnlyList<BlockCategoryDescriptor> Categories { get; }

    /// <summary>Block descriptors, in palette order.</summary>
    IReadOnlyList<BlockDescriptor> Blocks { get; }

    /// <summary>
    /// The C# for one block's value, or null when this provider does not own it.
    /// </summary>
    /// <param name="context">The document being emitted, and the names the region has declared.</param>
    /// <param name="block">The block to render.</param>
    /// <remarks>
    /// Returning null is how a provider says "not mine", and it is how the emitter finds the right provider
    /// without a registry having to map every kind to an object first.
    /// </remarks>
    string? EmitExpression(BlockEmitContext context, Block block);

    /// <summary>
    /// The C# for one block as a statement, written through <paramref name="writer"/>.
    /// </summary>
    /// <param name="context">The document being emitted, and the names the region has declared.</param>
    /// <param name="block">The block to render.</param>
    /// <param name="writer">The region's line writer, already indented.</param>
    void EmitStatement(BlockEmitContext context, Block block, IndentedCodeWriter writer);

    /// <summary>
    /// The steps a contributed block takes when the simulator runs it.
    /// </summary>
    /// <param name="context">The simulated host and the parameters the action declared.</param>
    /// <param name="block">The block to run.</param>
    /// <param name="ct">Cancellation, which the stage uses to stop a run between blocks.</param>
    IAsyncEnumerable<ExecutionStep> ExecuteAsync(InterpretContext context, Block block, CancellationToken ct);
}

/// <summary>
/// What a provider is handed when it emits: the document, the target's name, and the region's own state.
/// </summary>
/// <param name="Project">The document being emitted.</param>
/// <param name="TargetName">The action's name, which several catalogue rows substitute into their expression.</param>
/// <param name="Locals">The names the region has declared, which is how a slot reads one rather than quoting it.</param>
/// <remarks>
/// <para>
/// The three names a provider always has in scope — <c>context</c>, <c>_logger</c> and
/// <c>_integration</c> — are not passed in, because a provider may not declare them. A block local that
/// shadows <c>_integration</c> is worse than a collision: the guard the emitter writes tests
/// <c>_integration is null</c>, so a local of that name makes the guard permanently false and every host
/// call underneath it throws. The emitter's own rule is
/// <see cref="VisualValidator.ReservedNames"/>, and a provider that wants the same protection asks
/// <see cref="VisualProviderLocals.Declaring"/> for the name rather than using one.
/// </para>
/// </remarks>
public sealed record BlockEmitContext(VisualProject Project, string TargetName, VisualProviderLocals Locals);

/// <summary>
/// The names a region has already declared, and the one rule a provider may not break.
/// </summary>
/// <remarks>
/// A tiny class rather than a record of sets because it has one mutating job — declaring a local — and a
/// record would copy the set on every change. <see cref="VisualEmitter.EmitState"/> keeps the same state for
/// the same reason, and a provider handed a second implementation of the rule would be a second place for
/// the shadowing bug to live.
/// </remarks>
public sealed class VisualProviderLocals
{
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);
    private int _counter;

    /// <summary>Whether the region has declared a local of this name.</summary>
    public bool Has(string? name) => !string.IsNullOrWhiteSpace(name) && _declared.Contains(name.Trim());

    /// <summary>
    /// A local name derived from a designer-supplied one, made unique.
    /// </summary>
    /// <remarks>
    /// Disambiguated rather than refused, because a provider cannot know what else the region declares —
    /// the whole document is being emitted around it. The uniqueness suffix means the caller's own block
    /// does not compile because another provider's block wanted the same word.
    /// </remarks>
    public string Declaring(string? wanted)
    {
        var name = wanted?.Trim() is { Length: > 0 } candidate ? candidate : "local";

        if (_declared.Add(name))
        {
            return name;
        }

        var candidate2 = name + (++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        while (!_declared.Add(candidate2))
        {
            candidate2 = name + (++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate2;
    }

    /// <summary>A number no other local in this region was given, for a synthesised variable.</summary>
    public int Next() => _counter++;
}

/// <summary>
/// The region's line writer: indentation and line breaks, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a class over a <see cref="StringBuilder"/> rather than a delegate or a callback. A provider
/// is writing into a user's source file, and the two things that make generated code readable are that
/// every line is indented at the depth the region is at and that nothing has to guess which newline the file
/// uses. Handing out a writer rather than a string keeps both in one implementation, so a contributed block
/// cannot arrive with its own idea of indentation.
/// </para>
/// <para>
/// It carries no WPF and no file access: it builds text, and the writer that splices the region into the
/// file is the one that knows about files.
/// </para>
/// </remarks>
public sealed class IndentedCodeWriter
{
    private readonly StringBuilder _text;
    private int _depth;

    /// <summary>Creates a writer over <paramref name="text"/>.</summary>
    public IndentedCodeWriter(StringBuilder text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <summary>One level of indentation, as spaces.</summary>
    public const string Indent = "    ";

    /// <summary>How deep the writer currently is.</summary>
    public int Depth => _depth;

    /// <summary>The indentation for the current depth.</summary>
    public string Pad() => new(' ', _depth * Indent.Length);

    /// <summary>Writes one line at the current depth.</summary>
    public void Line(string text) => _text.Append(Pad()).AppendLine(text);

    /// <summary>Writes text at the current depth, without a line break of its own.</summary>
    public void Text(string text) => _text.Append(Pad()).Append(text);

    /// <summary>Writes an opening brace and steps in.</summary>
    public void Open()
    {
        Line("{");
        _depth++;
    }

    /// <summary>Steps out and writes the closing brace.</summary>
    public void Close()
    {
        _depth--;
        Line("}");
    }

    /// <summary>An empty line, with no trailing indentation on it.</summary>
    public void Blank() => _text.AppendLine();

    /// <summary>Everything written so far.</summary>
    public override string ToString() => _text.ToString();
}

/// <summary>
/// What a provider is handed when the simulator runs one of its blocks.
/// </summary>
/// <param name="Host">
/// The simulated host. Every runtime call a contributed block makes goes here, never to a real client and
/// never to the network — the stage's whole claim is that it is showing the user what will happen.
/// </param>
/// <param name="DeclaredParameters">The action's parameter names, which a contributed block may read.</param>
/// <remarks>
/// <para>
/// The host, not the real SDK, and that is the reason a contributed block can be previewed at all: it is
/// handed the same surface every built-in block is, so a provider author writes against one interface and
/// gets a stage that runs it.
/// </para>
/// <para>
/// Cancellation is a parameter of the step sequence rather than of this record, because the sequence is
/// asynchronous and the stage cancels between blocks — so a provider sees the token it was handed and not
/// one that has already fired.
/// </para>
/// </remarks>
public sealed record InterpretContext(IVisualHost Host, IReadOnlyList<string>? DeclaredParameters = null)
{
    /// <summary>The parameter names, never null.</summary>
    public IReadOnlyList<string> Parameters => DeclaredParameters ?? [];
}