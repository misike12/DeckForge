using System.Text;
using System.Text.RegularExpressions;
using DeckForge.Core.Code;
using DeckForge.Core.Visual;

namespace DeckForge.CodeGen.Generation;

/// <summary>What went wrong while compiling a document, in the language of the canvas.</summary>
public sealed record VisualCompileProblem(string BlockId, string Message);

/// <summary>
/// Compiles a <see cref="VisualProject"/> into the C# that goes inside the block region.
/// </summary>
/// <remarks>
/// <para>
/// Data, not a switch: the catalog row's <see cref="SdkMapping.Expression"/> is a template and this
/// class fills its holes. A new block therefore needs no emitter change, and the risk that the catalog
/// and the code generator disagree about what a block does is designed away rather than tested for —
/// the expression IS the emission, quoted from the surface it was verified against.
/// </para>
/// <para>
/// Holes come in two spellings, and the substitution is the same for both. A braced hole
/// (<c>{seconds}</c>) is the documented form; a bare hole — the slot's own name appearing as a whole
/// word in the template, as in <c>VisualRuntime.ToNumber(seconds)</c> — reads as if the slot were a
/// local, which is why the rows are written that way. Rendering is per slot type: a <c>List</c> slot
/// becomes the local's identifier, a <c>Variable</c> or <c>Parameter</c> slot becomes the name as a
/// string literal, a <c>Procedure</c> slot becomes the local function's identifier, and value slots
/// become literals converted at the use site or a nested reporter's expression. A slot whose text
/// names a local the region already declared (<c>into</c>, the response record) reads the local.
/// </para>
/// <para>
/// Anything unmatched is a <see cref="VisualCompileProblem"/>, never a silently empty string. The
/// three invariants inherited from the old compiler hold: the guard is emitted once per region,
/// output is deterministic from the document, and nothing outside <c>context</c>, <c>_logger</c> and
/// <c>_integration</c> is assumed.
/// </para>
/// </remarks>
public static partial class VisualEmitter
{
    /// <summary>The names a generated region may not redeclare.</summary>
    public static readonly IReadOnlyList<string> ReservedNames = ["context", "_logger", "_integration"];

    /// <summary>Compiles one target's scripts into region-ready C#, without markers.</summary>
    public static (string Code, IReadOnlyList<VisualCompileProblem> Problems) CompileTarget(
        VisualTarget target,
        VisualProject project)
    {
        var state = new EmitState { TargetName = target.Name, Project = project };
        EmitProcedures(project, state);

        foreach (var script in target.Scripts)
        {
            EmitScript(script, state);
        }

        return (state.Flush(), state.Problems);
    }

    /// <summary>
    /// Compiles a whole document: one region per action target, procedures hoisted into each region
    /// that references them.
    /// </summary>
    /// <remarks>
    /// A procedure is emitted into every region whose blocks call it (Part 7.14): the generated action
    /// class is <c>sealed</c>, so a local function cannot outlive the method it is declared in, and
    /// sharing across targets is a Phase 10 question. An uncalled procedure costs nothing.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Compile(VisualProject project)
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in project.Targets)
        {
            regions[target.Id] = CompileTarget(target, project).Code;
        }

        return regions;
    }

    // ---- scripts ------------------------------------------------------------------------------------

    private static void EmitScript(VisualScript script, EmitState state)
    {
        state.Line($"// script: {script.Name}");
        if (script.Disabled)
        {
            state.Line("// (disabled - skipped by the emitter and the interpreter)");
            return;
        }

        foreach (var statement in script.Body)
        {
            EmitStatement(statement, state, 0);
        }
    }

    /// <summary>Emits every procedure the target references, hoisted above the scripts.</summary>
    private static void EmitProcedures(VisualProject project, EmitState state)
    {
        foreach (var procedure in project.Procedures)
        {
            if (!Referenced(project, procedure.Name))
            {
                continue;
            }

            var returnType = procedure.Returns ? "Task<object?>" : "Task";
            // Parameters travel as objects: the use sites coerce, so a call site never has to know a
            // parameter's declared type to pass text that parses.
            var parameters = string.Join(", ", procedure.Parameters.Select(p =>
                $"object? {CSharpCode.Identifier(p.Name)}"));

            state.Blank();
            state.Line($"async {returnType} {ProcedureIdentifier(procedure.Name)}({parameters})");
            state.Open();
            var bodyState = state.Fork();
            foreach (var parameter in procedure.Parameters)
            {
                // Bound into Locals under their own name, so a `var.get` that names one reads it like
                // any other variable — and the identifier of the parameter itself works too.
                bodyState.Line(
                    $"{bodyState.Pad(1)}VisualRuntime.Set({CSharpCode.StringLiteral(parameter.Name)}, {CSharpCode.Identifier(parameter.Name)});");
            }
            foreach (var statement in procedure.Body)
            {
                EmitStatement(statement, bodyState, 1);
            }

            state.Absorb(bodyState);
            state.Close();
        }
    }

    private static bool Referenced(VisualProject project, string procedureName)
    {
        foreach (var block in project.Blocks())
        {
            if (block.Kind.StartsWith("proc.call", StringComparison.Ordinal)
                && string.Equals(block.InputText("name"), procedureName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A procedure's emitted name. The Async suffix is the C# convention the generated region lives
    /// in, and it keeps a procedure called <c>Wait</c> from shadowing a task method.
    /// </summary>
    private static string ProcedureIdentifier(string name) =>
        CSharpCode.Identifier(name.EndsWith("Async", StringComparison.OrdinalIgnoreCase) ? name : name + "Async");

    // ---- statements -----------------------------------------------------------------------------------

    private static void EmitStatement(Block block, EmitState state, int indent)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is null)
        {
            state.Problem(block.Id, $"\"{block.Kind}\" is not a block this build knows; it was skipped.");
            return;
        }

        var pad = state.Pad(indent);

        if (block.Disabled)
        {
            state.Line($"{pad}// {descriptor.Label} (disabled)");
            return;
        }

        if (block.Comment is { Length: > 0 } comment)
        {
            foreach (var line in comment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                state.Line($"{pad}// {line}");
            }
        }

        if (block.Kind == "control.comment")
        {
            state.Line($"{pad}// {block.InputText("text")}");
            return;
        }

        // A reporter at statement position is a canvas mistake the drop resolver refuses on the way
        // in; a hand-edited document can still carry one, and an expression statement is CS0202.
        // proc.call-value is the one exception: it is shaped like a reporter because the palette
        // offers it where a value is wanted, but as a statement it is a fire-and-forget call.
        if (descriptor.IsReporter && block.Kind != "proc.call-value")
        {
            state.Problem(block.Id, $"\"{descriptor.Label}\" produces a value; nothing here reads it.");
            return;
        }

        switch (block.Kind)
        {
            case "control.repeat": EmitRepeat(block, state, indent, pad); return;
            case "control.repeat-until": EmitRepeatUntil(block, state, indent, pad); return;
            case "control.forever": EmitForever(block, state, indent, pad); return;
            case "control.while": EmitWhile(block, state, indent, pad); return;
            case "control.if": EmitIf(block, withElse: false, state, indent, pad); return;
            case "control.if-else": EmitIf(block, withElse: true, state, indent, pad); return;
            case "control.if-else-if": EmitIfElseIf(block, state, indent, pad); return;
            case "control.break": state.Line($"{pad}break;"); return;
            case "control.continue": state.Line($"{pad}continue;"); return;
            case "control.finish-failed": EmitFinishFailed(block, state, pad); return;
            case "control.finish-accepted": EmitFinishAccepted(block, state, pad); return;
            case "control.finish-success":
            case "control.stop-script":
            case "control.stop-all":
                state.Line($"{pad}return ActionResult.Success();");
                return;
            case "control.return":
                state.Line($"{pad}return {Value(block, "value", state)};");
                return;

            case "proc.call":
                state.Line($"{pad}await {ProcedureIdentifier(block.InputText("name") ?? "procedure")}({CallArgs(block, state)});");
                return;

            case "proc.call-with":
                state.Line($"{pad}await {ProcedureIdentifier(block.InputText("name") ?? "procedure")}({CallArgs(block, state)});");
                return;

            case "proc.call-value":
                state.Line($"{pad}var _r{state.Next()} = await {ProcedureIdentifier(block.InputText("name") ?? "procedure")}({CallArgs(block, state)});");
                return;
            case "legacy.unsupported":
                state.Line($"{pad}// {block.InputText("legacyType")} came from a build this one does not know, so it is skipped.");
                return;
        }

        if (descriptor.Shape == BlockShape.Modifier)
        {
            state.Problem(block.Id, $"\"{block.Kind}\" cannot be emitted; it is a canvas modifier.");
            return;
        }

        EmitTemplate(block, descriptor, state, pad);
    }

    private static void EmitRepeat(Block block, EmitState state, int indent, string pad)
    {
        var counter = $"_i{state.Next()}";
        state.Line($"{pad}for (var {counter} = 0; {counter} < {Value(block, "count", state, "Number")}; {counter}++)");
        state.Open();
        EmitBody(block, "body", block, state, indent, cancel: true);
        state.Close();
    }

    private static void EmitRepeatUntil(Block block, EmitState state, int indent, string pad)
    {
        // Scratch semantics: the body runs first, the condition is tested after.
        state.Line($"{pad}do");
        state.Open();
        EmitBody(block, "body", block, state, indent, cancel: true);
        state.Close();
        state.Line($"{pad}while (!{Value(block, "condition", state, "Boolean")});");
    }

    private static void EmitForever(Block block, EmitState state, int indent, string pad)
    {
        state.Line($"{pad}while (true)");
        state.Open();
        EmitBody(block, "body", block, state, indent, cancel: true);
        state.Close();
    }

    private static void EmitWhile(Block block, EmitState state, int indent, string pad)
    {
        state.Line($"{pad}while ({Value(block, "condition", state, "Boolean")})");
        state.Open();
        EmitBody(block, "body", block, state, indent, cancel: true);
        state.Close();
    }

    private static void EmitIf(Block block, bool withElse, EmitState state, int indent, string pad)
    {
        state.Line($"{pad}if ({Value(block, "condition", state, "Boolean")})");
        state.Open();
        EmitBody(block, "then", block, state, indent, cancel: false);
        state.Close();

        if (!withElse)
        {
            return;
        }

        if (!block.Bodies.TryGetValue("else", out var @else) || @else.Count == 0)
        {
            return;
        }

        state.Line($"{pad}else");
        state.Open();
        EmitBody(block, "else", block, state, indent, cancel: false);
        state.Close();
    }

    private static void EmitIfElseIf(Block block, EmitState state, int indent, string pad)
    {
        state.Line($"{pad}if ({Value(block, "condition", state, "Boolean")})");
        state.Open();
        EmitBody(block, "then", block, state, indent, cancel: false);
        state.Close();

        var hasSecond = block.Inputs.TryGetValue("condition2", out var second) && !second.IsEmpty;
        if (block.Bodies.TryGetValue("elseIf", out var elseIf) && elseIf.Count > 0)
        {
            state.Line($"{pad}else if ({(hasSecond ? Value(block, "condition2", state, "Boolean") : "false")})");
            state.Open();
            EmitBody(block, "elseIf", block, state, indent, cancel: false);
            state.Close();
        }

        if (block.Bodies.TryGetValue("else", out var @else) && @else.Count > 0)
        {
            state.Line($"{pad}else");
            state.Open();
            EmitBody(block, "else", block, state, indent, cancel: false);
            state.Close();
        }
    }

    private static void EmitBody(Block block, string bodyName, Block owner, EmitState state, int indent, bool cancel)
    {
        if (cancel)
        {
            state.Line($"{state.Pad(indent + 1)}context.CancellationToken.ThrowIfCancellationRequested();");
        }

        if (!block.Bodies.TryGetValue(bodyName, out var body))
        {
            return;
        }

        foreach (var child in body)
        {
            EmitStatement(child, state, indent + 1);
        }
    }

    /// <summary>The error-code menu, checked rather than trusted at emission.</summary>
    private static readonly IReadOnlyList<string> ErrorCodes =
    [
        "NotConfigured", "NotConnected", "PermissionDenied", "ProviderError", "ProviderRejected",
        "InvalidParameter", "NotFound", "Timeout", "Unavailable",
    ];

    private static void EmitFinishFailed(Block block, EmitState state, string pad)
    {
        var chosen = block.Field("code");
        var code = chosen is { Length: > 0 } && ErrorCodes.Contains(chosen, StringComparer.Ordinal)
            ? chosen
            : "ProviderError";
        state.Line($"{pad}return ActionResult.Failed(ActionErrorCodes.{code}, {Value(block, "message", state)});");
    }

    private static void EmitFinishAccepted(Block block, EmitState state, string pad)
    {
        state.Line($"{pad}return ActionResult.Accepted({Value(block, "message", state)});");
    }

    // ---- templates ----------------------------------------------------------------------------------

    /// <summary>Fills the row's expression template. The default path for every non-control block.</summary>
    private static void EmitTemplate(Block block, BlockDescriptor descriptor, EmitState state, string pad)
    {
        var expression = Render(block, descriptor, state);

        if (descriptor.Sdk.RequiresHost)
        {
            state.NoteHostGuard();
        }

        if (descriptor.Sdk.RequiresInteraction)
        {
            state.NoteInteractionGuard();
        }

        // A declaration template carries its own trailing semicolon; a bare call does not.
        if (expression.EndsWith(";", StringComparison.Ordinal))
        {
            state.Indented(pad, expression);
        }
        else
        {
            state.Indented(pad, expression + ";");
        }
    }

    /// <summary>
    /// Renders a template's holes into C#: braced holes first, then bare slot names as whole words.
    /// </summary>
    internal static string Render(Block block, BlockDescriptor descriptor, EmitState state)
    {
        var braced = Hole().Replace(descriptor.Sdk.Expression, match =>
        {
            var hole = match.Groups[1].Value;
            return hole switch
            {
                "level" => CSharpCode.Identifier(block.Field(hole) ?? "Information"),
                "actionId" => CSharpCode.StringLiteral(state.TargetName ?? string.Empty),
                _ when IsIntoHole(descriptor, hole) => state.DeclareLocal(block.InputText(hole)),
                _ => Value(block, hole, state),
            };
        });

        // Bare holes: the slot's name as a whole word anywhere left in the template. Single pass, so
        // replacement text is never rescanned and a value containing the word `value` cannot recurse.
        var result = braced;
        if (descriptor.Slots is { Count: > 0 })
        {
            foreach (var slot in descriptor.Slots)
            {
                var pattern = BareHole(slot.Name);
                if (pattern is null)
                {
                    continue;
                }

                result = pattern.Replace(result, _ => Value(block, slot.Name, state));
            }
        }

        if (descriptor.Menus is { Count: > 0 })
        {
            foreach (var menu in descriptor.Menus)
            {
                var pattern = BareHole(menu.Name);
                if (pattern is null)
                {
                    continue;
                }

                result = pattern.Replace(result, _ => CSharpCode.StringLiteral(block.Field(menu.Name) ?? menu.Default));
            }
        }

        return result;
    }

    /// <summary>A word-bounded regex for a bare hole, or null when the name is not a safe pattern.</summary>
    private static Regex? BareHole(string name) =>
        name.Length == 0 || !char.IsAsciiLetter(name[0])
            ? null
            : new Regex($"\\b{Regex.Escape(name)}\\b", RegexOptions.None);

    private static bool IsIntoHole(BlockDescriptor descriptor, string hole)
    {
        if (hole is "into")
        {
            return true;
        }

        // `{name}` declares on list.define and names a procedure parameter everywhere else.
        return hole == "name" && descriptor.Slot("name")?.Type == SlotType.List;
    }

    /// <summary>The args slot's lines, one C# string literal.</summary>
    private static string ArgsLiteral(Block block) =>
        CSharpCode.StringLiteral(block.InputText("args") ?? string.Empty);

    /// <summary>
    /// The call's arguments: one per declared parameter, positional, values taken from the args
    /// slot's <c>name=value</c> lines. An unknown procedure keeps the old whole-string behaviour and
    /// says so — never a silent scramble of the caller's intent.
    /// </summary>
    private static string CallArgs(Block block, EmitState state)
    {
        var name = block.InputText("name");
        var procedure = name is null
            ? null
            : state.Project?.Procedures.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.Ordinal));
        if (procedure is null)
        {
            if (block.InputText("args") is { Length: > 0 })
            {
                state.Problem(
                    block.Id,
                    $"\"{name}\" is not a procedure this document declares; its arguments were not bound.");
            }

            return ArgsLiteral(block);
        }

        var values = ParsePairs(block.InputText("args") ?? string.Empty);
        return string.Join(", ", procedure.Parameters.Select(p =>
            CSharpCode.StringLiteral(values.TryGetValue(p.Name, out var value) ? value : string.Empty)));
    }

    private static IReadOnlyDictionary<string, string> ParsePairs(string lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=');
            if (line.Length == 0 || equals <= 0)
            {
                continue;
            }

            result[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return result;
    }

    /// <summary>
    /// The C# for one value: a local, a name, a literal converted at the use site, or a reporter.
    /// </summary>
    private static string Value(Block block, string slotName, EmitState state, string? coerce = null)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        var slot = descriptor?.Slot(slotName);

        if (!block.Inputs.TryGetValue(slotName, out var input) || input.IsEmpty)
        {
            return coerce switch
            {
                "Number" => "0",
                "Boolean" => "false",
                _ => "string.Empty",
            };
        }

        // A name the region already declared reads the local, whatever the slot's preferred type:
        // that is how the response record's readers find their request.
        if (slot?.Type is not SlotType.Variable
            && (input.Text ?? input.Variable) is { Length: > 0 } candidate
            && state.HasLocal(candidate))
        {
            return CSharpCode.Identifier(candidate);
        }

        switch (slot?.Type)
        {
            case SlotType.List:
                return CSharpCode.Identifier(input.Text ?? input.Variable ?? "list");
            case SlotType.Variable:
            case SlotType.Parameter:
            case SlotType.HostVariable:
            case SlotType.UserVariable:
            case SlotType.Event:
            case SlotType.Action:
            case SlotType.Script:
            case SlotType.Widget:
            case SlotType.ConfigEntry:
                return CSharpCode.StringLiteral(input.Text ?? input.Variable ?? string.Empty);
            case SlotType.Procedure:
                return ProcedureIdentifier(input.Text ?? "procedure");
        }

        switch (input.Kind)
        {
            case BlockInputKind.Block:
            {
                var nested = input.Block!;
                var nestedDescriptor = BlockCatalog.Find(nested.Kind);
                if (nestedDescriptor is null)
                {
                    state.Problem(nested.Id, $"\"{nested.Kind}\" is not a block this build knows.");
                    return "string.Empty";
                }

                if (!nestedDescriptor.IsReporter)
                {
                    state.Problem(nested.Id, $"\"{nestedDescriptor.Label}\" is a statement, and statements cannot be values.");
                    return "string.Empty";
                }

                return Render(nested, nestedDescriptor, state);
            }

            case BlockInputKind.Variable:
                return CSharpCode.Identifier(input.Variable!);

            case BlockInputKind.Number:
                return coerce == null
                    ? CSharpCode.NumberLiteral(input.Number)
                    : $"VisualRuntime.ToNumber({CSharpCode.NumberLiteral(input.Number)})";

            case BlockInputKind.Boolean:
                return input.Boolean is true ? "true" : "false";

            default:
            {
                var text = input.Text ?? string.Empty;
                return coerce switch
                {
                    "Number" => $"VisualRuntime.ToNumber({CSharpCode.StringLiteral(text)})",
                    "Boolean" => $"VisualRuntime.ToBool({CSharpCode.StringLiteral(text)})",
                    _ => CSharpCode.StringLiteral(text),
                };
            }
        }
    }

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex Hole();

    /// <summary>Mutable state for one region: the lines, the locals, the problems, the guards.</summary>
    public sealed class EmitState
    {
        private readonly StringBuilder _sb = new();
        private readonly HashSet<string> _locals = new(StringComparer.Ordinal);
        private readonly List<VisualCompileProblem> _problems = [];
        private int _counter;

        public string? TargetName { get; init; }

        public VisualProject? Project { get; init; }

        public bool WantsHostGuard { get; private set; }

        public bool WantsInteractionGuard { get; private set; }

        public void NoteHostGuard() => WantsHostGuard = true;

        public void NoteInteractionGuard() => WantsInteractionGuard = true;

        public IReadOnlyList<VisualCompileProblem> Problems => _problems;

        public void Problem(string blockId, string message) =>
            _problems.Add(new VisualCompileProblem(blockId, message));

        public string Pad(int indent) => new(' ', indent * 4);

        public void Line(string line) => _sb.AppendLine(line);

        public void Indented(string pad, string text) => _sb.Append(pad).AppendLine(text);

        public void Blank() => _sb.AppendLine();

        public void Open() => _sb.AppendLine("{");

        public void Close() => _sb.AppendLine("}");

        public bool HasLocal(string? name) =>
            !string.IsNullOrWhiteSpace(name) && _locals.Contains(name.Trim());

        public int Next() => _counter++;

        /// <summary>Declares a local the region may later read by name.</summary>
        public string DeclareLocal(string? wanted)
        {
            var name = CSharpCode.Identifier(wanted);
            if (!_locals.Add(name))
            {
                var candidate = $"{name}{_counter++}";
                while (!_locals.Add(candidate))
                {
                    candidate = $"{name}{_counter++}";
                }

                name = candidate;
            }

            return name;
        }

        /// <summary>A fork for a procedure body: its locals are its own.</summary>
        public EmitState Fork() => new() { TargetName = TargetName, Project = Project };

        public void Absorb(EmitState other)
        {
            _sb.Append(other._sb);
            _problems.AddRange(other._problems);
            WantsHostGuard |= other.WantsHostGuard;
            WantsInteractionGuard |= other.WantsInteractionGuard;
        }

        /// <summary>The finished region, with the deduplicated guards at the top.</summary>
        public string Flush()
        {
            var prefix = new StringBuilder();
            if (WantsHostGuard)
            {
                prefix.AppendLine("if (_integration is null)");
                prefix.AppendLine("{");
                prefix.AppendLine("    return ActionResult.Failed(ActionErrorCodes.NotConnected, \"No host session is established yet.\");");
                prefix.AppendLine("}");
                prefix.AppendLine();
            }

            if (WantsInteractionGuard)
            {
                prefix.AppendLine("if (context.Ui is null || context.Interactions is null)");
                prefix.AppendLine("{");
                prefix.AppendLine("    return ActionResult.Failed(ActionErrorCodes.Unavailable, \"No client surface is available yet.\");");
                prefix.AppendLine("}");
                prefix.AppendLine();
            }

            return prefix.Append(_sb).ToString();
        }
    }
}
