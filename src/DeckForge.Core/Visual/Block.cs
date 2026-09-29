using System.Text.Json.Serialization;

namespace DeckForge.Core.Visual;

/// <summary>
/// One block on the canvas: a statement, a reporter or a boolean, distinguished by its catalog
/// descriptor's <see cref="BlockShape"/> rather than by its C# type.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately one node type rather than a class per block. The design first described a
/// hierarchy, which is right for eighteen statement kinds and absurd for a hundred and fifty-three:
/// every one of them would be a file of properties that the catalog already declares, and every new
/// block would need a new class plus a serializer registration. The descriptor carries the shape, the
/// slots, the menus, the bodies and the emission, so the node only has to carry the user's choices.
/// This also matches how real block editors store a document — Scratch and Blockly both key blocks by
/// type id — and it makes the sidecar readable, which Part 6.4 asks for.
/// </para>
/// <para>
/// Behaviour that depends on the shape (which body is a loop, which block may be a hat) is read from
/// the catalog, so the emitter never switches on a C# type and cannot forget a case: a kind with no
/// descriptor is a failing test, not a silent fall-through.
/// </para>
/// </remarks>
public sealed class Block
{
    /// <summary>The catalog id, such as <c>control.repeat</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// A stable id, unique within the document.
    /// </summary>
    /// <remarks>
    /// Used by the layout cache, by undo, by diagnostics and by breakpoints. Stable across edits is
    /// what makes a round-trip diff quiet, so it is never regenerated for an existing block.
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    /// <summary>Slot values, keyed by the slot's name in the catalog.</summary>
    public Dictionary<string, BlockInput> Inputs { get; set; } = [];

    /// <summary>Inline menu selections and free text fields, keyed by the field's name in the catalog.</summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>Wrapped bodies, keyed by the body's name in the catalog: <c>then</c>, <c>else</c>, <c>body</c>.</summary>
    public Dictionary<string, List<Block>> Bodies { get; set; } = [];

    /// <summary>Whether the <c>disable</c> modifier is attached, which skips the block and its bodies.</summary>
    public bool Disabled { get; set; }

    /// <summary>The attached comment, or null.</summary>
    public string? Comment { get; set; }

    /// <summary>The block wrapped in a slot, when the slot holds a reporter rather than a literal.</summary>
    public Block? InputBlock(string name) => Inputs.TryGetValue(name, out var input) ? input.Block : null;

    /// <summary>The value in a slot as text, from either a literal or a variable reference.</summary>
    public string? InputText(string name) =>
        Inputs.TryGetValue(name, out var input) ? input.Text ?? input.Variable : null;

    /// <summary>The value in a slot as a number, or null when it holds something else.</summary>
    public double? InputNumber(string name) =>
        Inputs.TryGetValue(name, out var input) ? input.Number : null;

    /// <summary>The value in a slot as a boolean, or null when it holds something else.</summary>
    public bool? InputBoolean(string name) =>
        Inputs.TryGetValue(name, out var input) ? input.Boolean : null;

    /// <summary>A field's value, or null when it was never set.</summary>
    public string? Field(string name) =>
        Fields.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// A body, created empty when it does not exist yet.
    /// </summary>
    /// <remarks>
    /// Creates on demand because every caller is an editing operation that is about to put something
    /// in it. Reads that must not mutate use <see cref="TryBody"/>.
    /// </remarks>
    public List<Block> Body(string name)
    {
        if (!Bodies.TryGetValue(name, out var body))
        {
            body = [];
            Bodies[name] = body;
        }

        return body;
    }

    /// <summary>A body when it exists, without creating it.</summary>
    public IReadOnlyList<Block> TryBody(string name) =>
        Bodies.TryGetValue(name, out var body) ? body : [];

    /// <summary>Sets a slot to a literal text value.</summary>
    public Block WithText(string name, string? value)
    {
        Inputs[name] = BlockInput.Of(value);
        return this;
    }

    /// <summary>Sets a slot to a literal number.</summary>
    public Block WithNumber(string name, double value)
    {
        Inputs[name] = BlockInput.Of(value);
        return this;
    }

    /// <summary>Sets a slot to a literal boolean.</summary>
    public Block WithBoolean(string name, bool value)
    {
        Inputs[name] = BlockInput.Of(value);
        return this;
    }

    /// <summary>Sets a slot to a variable reference.</summary>
    public Block WithVariable(string name, string variable)
    {
        Inputs[name] = BlockInput.OfVariable(variable);
        return this;
    }

    /// <summary>Puts a reporter block in a slot.</summary>
    public Block WithBlock(string name, Block block)
    {
        Inputs[name] = BlockInput.Of(block);
        return this;
    }

    /// <summary>Sets a field, which is where menus and free text live.</summary>
    public Block WithField(string name, string? value)
    {
        if (value is null)
        {
            Fields.Remove(name);
        }
        else
        {
            Fields[name] = value;
        }

        return this;
    }

    /// <summary>
    /// This block, everything inside its bodies, and every nested block in its slots.
    /// </summary>
    /// <remarks>
    /// One walk, used by the id check, the validator and the emitter's collision scan. Each of those
    /// recursing on its own is how a nested branch gets forgotten in one of them and not the others.
    /// </remarks>
    public IEnumerable<Block> Walk()
    {
        yield return this;

        foreach (var input in Inputs.Values)
        {
            if (input.Block is { } nested)
            {
                foreach (var descendant in nested.Walk())
                {
                    yield return descendant;
                }
            }
        }

        foreach (var body in Bodies.Values)
        {
            foreach (var statement in body)
            {
                foreach (var descendant in statement.Walk())
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>The blocks in this block's bodies, one level down.</summary>
    public IEnumerable<Block> DirectChildren() => Bodies.Values.SelectMany(body => body);

    /// <summary>
    /// Every place this block's tree refers to the named variable, so a rename can carry its uses.
    /// </summary>
    /// <remarks>
    /// Renaming a variable is the one edit that reaches outside its own subtree, and a rename that
    /// misses a use is the exact defect the existing engine refuses to risk at all by renaming
    /// declarations. Returning the uses makes the rename testable as a pure function.
    /// </remarks>
    public IEnumerable<BlockInput> VariableUses(string name)
    {
        foreach (var input in Inputs.Values)
        {
            if (string.Equals(input.Variable, name, StringComparison.Ordinal))
            {
                yield return input;
            }
        }

        foreach (var walk in Walk().Where(block => !ReferenceEquals(block, this)))
        {
            foreach (var input in walk.Inputs.Values)
            {
                if (string.Equals(input.Variable, name, StringComparison.Ordinal))
                {
                    yield return input;
                }
            }
        }
    }

    /// <summary>
    /// This block and its subtree, copied, with fresh ids from <paramref name="nextId"/>.
    /// </summary>
    /// <remarks>
    /// Duplicate and paste both go through here. Reusing an id would make two blocks share a layout
    /// entry, an undo target and a breakpoint, which is a hard bug to see on screen.
    /// </remarks>
    public Block Clone(Func<string> nextId)
    {
        var copy = new Block
        {
            Kind = Kind,
            Id = nextId(),
            Disabled = Disabled,
            Comment = Comment,
            Fields = new Dictionary<string, string>(Fields, StringComparer.Ordinal),
        };

        foreach (var (name, input) in Inputs)
        {
            copy.Inputs[name] = new BlockInput
            {
                Text = input.Text,
                Number = input.Number,
                Boolean = input.Boolean,
                Variable = input.Variable,
                Block = input.Block?.Clone(nextId),
            };
        }

        foreach (var (name, body) in Bodies)
        {
            copy.Bodies[name] = [.. body.Select(statement => statement.Clone(nextId))];
        }

        return copy;
    }
}

/// <summary>Which member of <see cref="BlockInput"/> carries the value.</summary>
public enum BlockInputKind
{
    /// <summary>Nothing is set: the slot is empty.</summary>
    Empty,

    /// <summary>A reporter block.</summary>
    Block,

    /// <summary>A variable reference.</summary>
    Variable,

    /// <summary>A literal text value.</summary>
    Text,

    /// <summary>A literal number.</summary>
    Number,

    /// <summary>A literal boolean.</summary>
    Boolean,
}

/// <summary>
/// One slot's contents: a nested reporter, a variable reference, or a literal.
/// </summary>
/// <remarks>
/// All members are nullable and exactly one is expected to be set. The JSON shape follows from that:
/// <c>{ "slot": { … } }</c>, <c>{ "var": "response" }</c>, <c>{ "text": "…" }</c>,
/// <c>{ "number": 500 }</c> or <c>{ "bool": true }</c>. A custom converter writes only the member
/// that is set, so a document stays readable and diffable — and <see cref="Kind"/> decides which one
/// that is, in one place, rather than at every call site.
/// </remarks>
public sealed class BlockInput
{
    /// <summary>A nested reporter block, when the slot holds a block.</summary>
    public Block? Block { get; set; }

    /// <summary>A variable name, when the slot references a variable.</summary>
    public string? Variable { get; set; }

    /// <summary>A literal text value.</summary>
    public string? Text { get; set; }

    /// <summary>A literal number.</summary>
    public double? Number { get; set; }

    /// <summary>A literal boolean.</summary>
    public bool? Boolean { get; set; }

    /// <summary>
    /// Which member carries the value.
    /// </summary>
    /// <remarks>
    /// Precedence is deliberate and narrow: a block wins over a variable, which wins over a literal.
    /// A malformed document with two members set is reported by <see cref="IsAmbiguous"/> rather than
    /// silently resolved, because which one the user meant is not knowable from the file.
    /// </remarks>
    [JsonIgnore]
    public BlockInputKind Kind =>
        Block is not null ? BlockInputKind.Block
        : Variable is not null ? BlockInputKind.Variable
        : Text is not null ? BlockInputKind.Text
        : Number is not null ? BlockInputKind.Number
        : Boolean is not null ? BlockInputKind.Boolean
        : BlockInputKind.Empty;

    /// <summary>Whether more than one member is set, which no writer produces.</summary>
    [JsonIgnore]
    public bool IsAmbiguous =>
        new object?[] { Block, Variable, Text, Number, Boolean }.Count(member => member is not null) > 1;

    /// <summary>Whether the slot holds nothing.</summary>
    [JsonIgnore]
    public bool IsEmpty => Kind == BlockInputKind.Empty;

    /// <summary>A literal text value. Null produces an empty slot rather than the four-letter word.</summary>
    public static BlockInput Of(string? value) =>
        value is null ? new BlockInput() : new BlockInput { Text = value };

    /// <summary>A literal number.</summary>
    public static BlockInput Of(double value) => new() { Number = value };

    /// <summary>A literal boolean.</summary>
    public static BlockInput Of(bool value) => new() { Boolean = value };

    /// <summary>A reporter block in a slot.</summary>
    public static BlockInput Of(Block block) => new() { Block = block };

    /// <summary>
    /// A reference to a variable by name.
    /// </summary>
    /// <remarks>
    /// Named <c>OfVariable</c> rather than the obvious <c>Variable</c> because a static factory and
    /// the <see cref="Variable"/> property cannot share a name: the compiler rejects the pair as a
    /// duplicate member. Found by the first build of this file.
    /// </remarks>
    public static BlockInput OfVariable(string name) => new() { Variable = name };
}
