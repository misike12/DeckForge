namespace DeckForge.Core.Visual;

/// <summary>How badly a document is wrong, on the same three-step ladder the rest of the tool uses.</summary>
/// <remarks>
/// Matches <c>ValidationSeverity</c> in DeckForge.Validators by value and by meaning, but is declared
/// here because the tests project cannot reference the UI stack and the interpreter (Part 10) has to
/// read severities too. A mapping at the App boundary converts one to the other, in one place.
/// </remarks>
public enum VisualSeverity
{
    /// <summary>A fact the user may want to know.</summary>
    Info,

    /// <summary>Probably a mistake; the document can still be saved and run.</summary>
    Warning,

    /// <summary>The generated code would not compile or would do something different from the drawing.</summary>
    Error,
}

/// <summary>
/// One finding about a document, named as the catalogue in Appendix F names it.
/// </summary>
/// <param name="Code">The kebab-case id from Appendix F, such as <c>vis-unbound-slot</c>.</param>
/// <param name="Severity">How badly the document is wrong.</param>
/// <param name="BlockId">The block the finding belongs to, so the pane can select and centre it.</param>
/// <param name="Message">What is wrong, in the language of the canvas rather than of the compiler.</param>
/// <param name="Hint">What to do about it, shown next to the message.</param>
/// <param name="DocsPath">An offline docs page, when the block catalog or the capability names one.</param>
public sealed record VisualDiagnostic(
    string Code,
    VisualSeverity Severity,
    string? BlockId,
    string Message,
    string? Hint = null,
    string? DocsPath = null);

/// <summary>
/// The document, held to the rules the design states.
/// </summary>
/// <remarks>
/// <para>
/// Pure Core: it reads a <see cref="VisualProject"/> and its blocks against
/// <see cref="BlockCatalog"/>, and produces <see cref="VisualDiagnostic"/>s. No file system, no WPF,
/// no workspace — the target-level checks that need those (<c>vis-target-file-missing</c>,
/// <c>vis-region-conflict</c>) live with the writer in CodeGen, where the file actually is. What is
/// here is everything the user can fix on the canvas, which is also everything the simulator needs
/// before it agrees to run a script.
/// </para>
/// <para>
/// Two rules shape the whole file. First, <strong>shape is checked, type is warned</strong>: the
/// grammar refuses a statement in a value slot outright (the drop resolver already refuses it on the
/// way in), while a text in a numeric slot is the user's business — the emitter coerces, so it works,
/// and the warning is the only honest thing to say. Second, <strong>a disabled block is not excused
/// from naming checks</strong>: it emits nothing, but the name it references is still shown, still
/// picked in dropdowns, and still the user's mental model — so a rename that would break a disabled
/// block is reported, at the severity the reference deserves.
/// </para>
/// </remarks>
public static class VisualValidator
{
    /// <summary>The reserved names of the block region, from the generated executor's signature.</summary>
    public static readonly IReadOnlyList<string> ReservedNames =
        ["context", "_logger", "_integration"];

    /// <summary>
    /// Validates a whole document against a plugin that is known to declare nothing.
    /// </summary>
    /// <remarks>
    /// <see cref="VisualValidationContext.None"/> rather than <c>Empty</c>, deliberately. A caller who
    /// does not pass a context has not said "I don't know what exists" - it has said nothing, and the
    /// honest reading of nothing is "nothing is declared", which is the reading that keeps the check
    /// useful. A caller who genuinely has not resolved the names says so by passing
    /// <see cref="VisualValidationContext.Empty"/>, which is what the Visual page does until a workspace
    /// is open.
    /// </remarks>
    public static IReadOnlyList<VisualDiagnostic> Validate(VisualProject project) =>
        Validate(project, VisualValidationContext.None);

    /// <summary>
    /// Validates a whole document against what the plugin actually declares.
    /// </summary>
    /// <param name="project">The document.</param>
    /// <param name="context">The declared names the reference checks run against.</param>
    public static IReadOnlyList<VisualDiagnostic> Validate(
        VisualProject project,
        VisualValidationContext context)
    {
        var diagnostics = new List<VisualDiagnostic>();
        var variableNames = project.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var listNames = project.Lists.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        // Name -> declaration, not a set of names. Part 7.14's call rules need more than "does this name
        // exist": they need how many parameters it takes and whether it returns a value, and a set cannot
        // answer either. Duplicate names collapse here, which is correct - the duplicate check reports
        // them and there is nothing sensible to say about the arity of two procedures with one name.
        var procedures = project.Procedures
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var declarations = new HashSet<string>(variableNames, StringComparer.Ordinal);
        declarations.UnionWith(listNames);

        foreach (var name in project.Variables.Where(v => v.Scope == VariableScope.Local).Select(v => v.Name)
            .Concat(project.Lists.Select(l => l.Name)))
        {
            if (ReservedNames.Contains(name, StringComparer.Ordinal))
            {
                diagnostics.Add(new VisualDiagnostic(
                    "vis-local-collision", VisualSeverity.Error, null,
                    $"\"{name}\" is a name the generated code already uses.",
                    "Rename it, or rename the existing local in your code."));
            }
        }

        diagnostics.AddRange(CheckNames("variable", project.Variables.Select(v => (v.Name, v.Scope))));
        diagnostics.AddRange(CheckNames("list", project.Lists.Select(l => (l.Name, VariableScope.Local))));
        diagnostics.AddRange(CheckNames(
            "procedure", project.Procedures.Select(p => (p.Name, VariableScope.Local))));

        // Duplicate procedures are an error of their own: two procedures with one name means one of
        // the definitions is unreachable, and the C# compiler will refuse the file anyway.
        var duplicatedProcedures = project.Procedures
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var name in duplicatedProcedures)
        {
            diagnostics.Add(new VisualDiagnostic(
                "vis-name-duplicate", VisualSeverity.Error, null,
                $"Two procedures are named \"{name}\".",
                "Names must be unique; rename one of them."));
        }

        var scriptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in project.Targets)
        {
            foreach (var script in target.Scripts)
            {
                if (scriptIds.Add(script.Id))
                {
                    // A setter declares its local on the line it runs: the document's own answer to
                    // Scratch's "you can use a variable before you declare it". Walking the body in
                    // order, each setter's name becomes readable for everything after it. Scanning the
                    // whole script first was the alternative, and it would call a genuinely-forward
                    // reference clean — the one mistake this check exists for.
                    var readable = new HashSet<string>(declarations, StringComparer.Ordinal);
                    diagnostics.AddRange(ValidateScript(script, context, readable, procedures));
                    CheckTrailingCap(script.Hat, script.Body, script.Name, diagnostics);
                }
            }
        }

        foreach (var procedure in project.Procedures)
        {
            var readable = new HashSet<string>(declarations, StringComparer.Ordinal);
            diagnostics.AddRange(ValidateProcedureBody(procedure, context, readable, procedures));
        }

        diagnostics.AddRange(CheckRecursion(project));

        return diagnostics;
    }

    /// <summary>
    /// A procedure that can reach itself, directly or through others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning, never an error. The design says so explicitly — recursion is bounded by the action's
    /// timeout and not by us — and refusing it would be a claim the editor cannot keep: a mutually
    /// recursive pair with no base case is legal C# and a legitimate thing to write.
    /// </para>
    /// <para>
    /// Transitive, because the interesting case is not `A` calling `A`. Two procedures calling each other
    /// produce no diagnostic from a direct check and hang exactly the same way, so the walk follows the
    /// call graph until it revisits the procedure it started from, with a visited set so a wide graph
    /// costs each edge once.
    /// </para>
    /// </remarks>
    private static IEnumerable<VisualDiagnostic> CheckRecursion(VisualProject project)
    {
        var byName = project.Procedures
            .GroupBy(procedure => procedure.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var procedure in project.Procedures)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { procedure.Name };
            var path = new List<string>();
            var reached = false;

            if (Reaches(procedure, procedure.Name, byName, seen, path, depth: 0))
            {
                reached = true;
            }

            if (reached)
            {
                yield return new VisualDiagnostic(
                    "vis-procedure-recursion", VisualSeverity.Warning, procedure.Body.FirstOrDefault()?.Id,
                    $"\"{procedure.Name}\" can call itself"
                    + (path.Count > 0 ? $" through {string.Join(" → ", path)}" : string.Empty)
                    + ".",
                    "That is allowed, but it needs a way out — a condition, or a maximum count — or the "
                    + "action will run until it times out.");
            }
        }
    }

    private static bool Reaches(
        ProcedureDeclaration from,
        string target,
        IReadOnlyDictionary<string, ProcedureDeclaration> byName,
        ISet<string> seen,
        List<string> path,
        int depth)
    {
        // Depth-capped rather than cycle-checked to infinity: a call graph this document can produce is
        // shallow, and a bound keeps a pathological document from turning validation into the hang it is
        // supposed to report.
        if (depth > 16)
        {
            return false;
        }

        foreach (var call in from.Body.SelectMany(statement => statement.Walk())
            .Where(block => block.Kind.StartsWith("proc.call", StringComparison.Ordinal)))
        {
            if (call.InputText("name") is not { Length: > 0 } called)
            {
                continue;
            }

            if (string.Equals(called, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (!byName.TryGetValue(called, out var next) || !seen.Add(called))
            {
                continue;
            }

            path.Add(called);

            if (Reaches(next, target, byName, seen, path, depth + 1))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    /// <summary>Validates one script: its hat, its stack, and everything nested inside both.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateScript(
        VisualScript script,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        if (BlockCatalog.Find(script.Hat.Kind) is not { IsHat: true })
        {
            yield return new VisualDiagnostic(
                "vis-shape-mismatch", VisualSeverity.Error, script.Hat.Id,
                $"\"{script.Name}\" starts with \"{script.Hat.Kind}\", which is not a starting block.",
                "Every script begins with a hat block, such as \"when action runs\".");
        }

        // A hat with an empty body is legal: it is a script that does nothing yet, which is how every
        // script starts. Diagnosing it would make an empty canvas permanently noisy.
        var walk = new StackWalk();
        foreach (var statement in script.Body)
        {
            walk.Push(loopDepth: 0, inProcedure: false);
            foreach (var diagnostic in ValidateStatement(statement, walk, context, declarations, procedures))
            {
                yield return diagnostic;
            }
            walk.Pop();
        }
    }

    private static IEnumerable<VisualDiagnostic> ValidateProcedureBody(
        ProcedureDeclaration procedure,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        var walk = new StackWalk();
        foreach (var statement in procedure.Body)
        {
            walk.Push(loopDepth: 0, inProcedure: true, procedure);
            foreach (var diagnostic in ValidateStatement(statement, walk, context, declarations, procedures))
            {
                yield return diagnostic;
            }

            walk.Pop();
        }

        // A procedure's body is the one place a plain `return {value}` is meaningful, so no
        // vis-return-outside-procedure is raised for anything inside it.
    }

    /// <summary>
    /// Validates one statement, recursing through bodies and slots.
    /// </summary>
    /// <remarks>
    /// The <paramref name="walk"/> carries what only the path from the root can know: how deep in
    /// loops this statement sits and whether a procedure is above. That is why validation walks the
    /// tree rather than flattening it first — <see cref="Block.Walk"/> would flatten away exactly the
    /// context the control-flow rules need.
    /// </remarks>
    private static IEnumerable<VisualDiagnostic> ValidateStatement(
        Block block,
        StackWalk walk,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        var descriptor = BlockCatalog.Find(block.Kind);
        if (descriptor is null)
        {
            yield return new VisualDiagnostic(
                "vis-menu-key-unknown", VisualSeverity.Error, block.Id,
                $"\"{block.Kind}\" is not a block this build knows.",
                "It came from another version of DeckForge. Re-add the block, or open the file in the " +
                "build that made it.");
            yield break;
        }

        // A setter is a declaration, so the name it writes becomes readable from here on. Recorded
        // before the statement's own checks run, because `set count to count + 1` is the ordinary way
        // to write an increment and must not read as a forward reference.
        if (!block.Disabled && block.Kind is "var.set" or "var.set-from-parameter" or "var.set-from-host")
        {
            switch (block.InputText("var"))
            {
                case { Length: > 0 } declared:
                    declarations.Add(declared);
                    break;
            }
        }

        // list.define does the same for a list.
        if (!block.Disabled && block.Kind == "list.define"
            && block.InputText("name") is { Length: > 0 } declaredList)
        {
            declarations.Add(declaredList);
        }

        if (!block.Disabled)
        {
            // The placeholder emits a comment and nothing else; nothing to validate on it.
            if (block.Kind == "legacy.unsupported")
            {
                yield return new VisualDiagnostic(
                    "vis-unverified-block", VisualSeverity.Info, block.Id,
                    "This block came from a version of DeckForge that knew more about the file than this " +
                    "one does.",
                    "It is kept, visible, and skipped when the code is generated.");
                yield break;
            }

            foreach (var diagnostic in ValidateSlots(block, descriptor, walk, context, declarations, procedures))
            {
                yield return diagnostic;
            }

            foreach (var diagnostic in ValidateFields(block, descriptor))
            {
                yield return diagnostic;
            }

            foreach (var diagnostic in ValidateControlFlow(block, descriptor, walk, procedures))
            {
                yield return diagnostic;
            }

            foreach (var diagnostic in ValidateCall(block, procedures))
            {
                yield return diagnostic;
            }

            foreach (var diagnostic in ValidateBusyLoops(block, descriptor, walk))
            {
                yield return diagnostic;
            }
        }
        else
        {
            // A disabled block still has to name things that exist, because the user will re-enable
            // it and expect it to have kept working. Only the name references are checked; an empty
            // slot on a disabled block is not worth a red dot.
            foreach (var diagnostic in ValidateNameReferences(block, descriptor, context, declarations, procedures))
            {
                yield return diagnostic;
            }
        }

        foreach (var (bodyName, body) in block.Bodies)
        {
            var insideLoop = walk.LoopDepth > 0
                || descriptor.IsContainer && IsLoopBody(descriptor, bodyName);
            foreach (var child in body)
            {
                walk.Push(insideLoop ? walk.LoopDepth + 1 : walk.LoopDepth, walk.InProcedure);
                foreach (var diagnostic in ValidateStatement(child, walk, context, declarations, procedures))
                {
                    yield return diagnostic;
                }
                walk.Pop();
            }
        }
    }

    /// <summary>Slots: shape of what sits in them, requiredness, and the names they reference.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateSlots(
        Block block,
        BlockDescriptor descriptor,
        StackWalk walk,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        foreach (var slot in descriptor.Slots ?? [])
        {
            block.Inputs.TryGetValue(slot.Name, out var input);
            var isEmpty = input is null || input.IsEmpty;
            if (isEmpty)
            {
                if (slot.Required)
                {
                    yield return new VisualDiagnostic(
                        "vis-unbound-slot", VisualSeverity.Error, block.Id,
                        $"\"{block.Kind}\" needs a value in \"{slot.Label ?? slot.Name}\".",
                        "Drop a value or block here, or open the block's fields in the inspector.");
                }

                continue;
            }

            var kind = input!.Kind;
            var slotIsValue = slot.Type is SlotType.Any or SlotType.Text or SlotType.Number
                or SlotType.Boolean or SlotType.File or SlotType.Icon or SlotType.Color;
            if (slotIsValue && kind == BlockInputKind.Block
                && BlockCatalog.Find(input.Block!.Kind) is not { IsReporter: true })
            {
                yield return new VisualDiagnostic(
                    "vis-shape-mismatch", VisualSeverity.Error, block.Id,
                    "A stack block was dropped into a value slot.",
                    "Reporter blocks go in the round slots; stack blocks go between blocks.");
            }
            else if (!slotIsValue && kind == BlockInputKind.Block
                && BlockCatalog.Find(input.Block!.Kind) is { IsReporter: true })
            {
                // A reporter in a source slot is fine - it types a name at runtime. But a statement in
                // a source slot is a shape error like any other.
                yield return new VisualDiagnostic(
                    "vis-shape-mismatch", VisualSeverity.Error, block.Id,
                    "A stack block was dropped into a picker slot.",
                    "Pick a name, or drop a reporter that produces one.");
            }

            if (slot.Type == SlotType.Boolean && kind is BlockInputKind.Text or BlockInputKind.Number
                or BlockInputKind.Boolean)
            {
                var value = input.Text ?? input.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? input.Boolean?.ToString();
                yield return new VisualDiagnostic(
                    "vis-type-mismatch", VisualSeverity.Warning, block.Id,
                    $"\"{value}\" is not a condition.",
                    "This will be converted at runtime; the value may not be what you expect.");
            }

            if (slot.Type == SlotType.Number && kind == BlockInputKind.Text
                && !double.TryParse(input.Text, out _))
            {
                yield return new VisualDiagnostic(
                    "vis-type-mismatch", VisualSeverity.Warning, block.Id,
                    $"\"{input.Text}\" is text where a number is expected.",
                    "This will be converted at runtime; the value may not be what you expect.");
            }

            foreach (var diagnostic in ValidateNameReference(
                block.Id, slot.Name, slot.Type, input, context, declarations, procedures))
            {
                yield return diagnostic;
            }
        }

        foreach (var nested in block.Inputs.Values.Select(input => input.Block).Where(b => b is not null))
        {
            // A reporter in a slot is validated as a small tree of its own.
            if (BlockCatalog.Find(nested!.Kind)?.IsReporter == true)
            {
                foreach (var diagnostic in ValidateNestedReporter(nested, context, declarations, procedures))
                {
                    yield return diagnostic;
                }
            }
            else
            {
                // A statement dropped into a slot was reported where it sits; its own slots are still
                // checked, so the user sees the whole problem: the block is in the wrong place AND it
                // is missing a value. Anything nested in its bodies is out of the control flow here,
                // so it is walked at zero loop depth.
                var misplaced = BlockCatalog.Find(nested.Kind)!;
                foreach (var diagnostic in ValidateSlots(nested, misplaced, new StackWalk(), context, declarations, procedures))
                {
                    yield return diagnostic;
                }

                foreach (var diagnostic in ValidateFields(nested, misplaced))
                {
                    yield return diagnostic;
                }

                foreach (var body in nested.Bodies.Values)
                {
                    foreach (var child in body)
                    {
                        walk.Push(0, walk.InProcedure);
                        foreach (var childDiagnostic in ValidateStatement(child, walk, context, declarations, procedures))
                        {
                            yield return childDiagnostic;
                        }
                        walk.Pop();
                    }
                }
            }
        }
    }

    /// <summary>A reporter nested in a slot, validated as its own small tree.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateNestedReporter(
        Block reporter,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        var descriptor = BlockCatalog.Find(reporter.Kind);
        if (descriptor is null)
        {
            yield return new VisualDiagnostic(
                "vis-menu-key-unknown", VisualSeverity.Error, reporter.Id,
                $"\"{reporter.Kind}\" is not a block this build knows.", null);
            yield break;
        }

        if (!descriptor.IsReporter)
        {
            yield return new VisualDiagnostic(
                "vis-shape-mismatch", VisualSeverity.Error, reporter.Id,
                $"\"{descriptor.Label}\" is a statement, and statements cannot be values.",
                "Reporter blocks go in the round slots; stack blocks go between blocks.");
            yield break;
        }

        foreach (var diagnostic in ValidateSlots(reporter, descriptor, new StackWalk(), context, declarations, procedures))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in ValidateFields(reporter, descriptor))
        {
            yield return diagnostic;
        }
    }

    /// <summary>Menus: the stored selection must be one the block offers.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateFields(Block block, BlockDescriptor descriptor)
    {
        foreach (var menu in descriptor.Menus ?? [])
        {
            if (!block.Fields.TryGetValue(menu.Name, out var chosen) || chosen.Length == 0)
            {
                // No selection means the block is fresh; the descriptor's default applies and is by
                // construction one of the options. An unset menu is not a finding.
                continue;
            }

            if (!menu.Options.Contains(chosen, StringComparer.Ordinal))
            {
                yield return new VisualDiagnostic(
                    "vis-menu-key-unknown", VisualSeverity.Error, block.Id,
                    $"\"{chosen}\" is not one of the choices for \"{menu.Label ?? menu.Name}\".",
                    "Valid choices: " + string.Join(", ", menu.Options));
            }
        }
    }

    /// <summary>Control flow: loop controls inside loops, returns inside procedures.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateControlFlow(
        Block block,
        BlockDescriptor descriptor,
        StackWalk walk,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        if (block.Kind is "control.break" or "control.continue" && walk.LoopDepth == 0)
        {
            yield return new VisualDiagnostic(
                "vis-loop-control-outside-loop", VisualSeverity.Error, block.Id,
                block.Kind == "control.break"
                    ? "\"break\" is not inside a loop."
                    : "\"continue\" is not inside a loop.",
                "Move it inside a repeat, forever, while or repeat until block.");
        }

        if (block.Kind == "control.return" && !walk.InProcedure)
        {
            yield return new VisualDiagnostic(
                "vis-return-outside-procedure", VisualSeverity.Error, block.Id,
                "\"return\" only means something inside a procedure.",
                "Use a finish block instead.");
        }
        else if (block.Kind == "control.return" && walk.Procedure is { Returns: false } voidProcedure
            && block.InputText("value") is { Length: > 0 } value)
        {
            yield return new VisualDiagnostic(
                "vis-return-value-void", VisualSeverity.Error, block.Id,
                $"\"{voidProcedure.Name}\" does not return a value, but this \"return\" hands back \"{value}\".",
                "Tick \"returns a value\" on the procedure, or return nothing.");
        }
        else if (block.Kind == "control.return" && walk.Procedure is { Returns: true } procedure
            && block.InputText("value") is not { Length: > 0 })
        {
            yield return new VisualDiagnostic(
                "vis-return-no-value", VisualSeverity.Error, block.Id,
                $"\"{procedure.Name}\" returns a value, so every \"return\" needs one.",
                "Hand back a value, or untick \"returns a value\" on the procedure.");
        }
    }

    /// <summary>
    /// Calls: the argument count has to match, and only a procedure that returns can be used as a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two checks that exist because the alternative is a C# compile error on the user's file rather than
    /// a sentence on the canvas. A call with the wrong number of arguments emits
    /// <c>NameAsync(a, b)</c> against a function taking one parameter, and the reporter form against a
    /// void one emits <c>var _r0 = await NameAsync();</c> where <c>NameAsync</c> returns <c>Task</c> —
    /// neither is catchable from the editor.
    /// </para>
    /// <para>
    /// Only the reported count matters, not the values: a call's arguments are one <c>name=value</c>
    /// text slot, and checking them means parsing that text three more times per call. A wrong count is the
    /// mistake worth naming; a wrong value already surfaces as the diagnostic the argument's own value
    /// produces.
    /// </para>
    /// </remarks>
    private static IEnumerable<VisualDiagnostic> ValidateCall(
        Block block,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        if (!block.Kind.StartsWith("proc.call", StringComparison.Ordinal)
            || block.InputText("name") is not { Length: > 0 } name)
        {
            yield break;
        }

        if (!procedures.TryGetValue(name, out var procedure))
        {
            // Already reported as vis-procedure-missing by the name check; saying it twice with a
            // different code would leave one fix producing two remaining errors.
            yield break;
        }

        var given = CallArgumentCount(block.InputText("args"));
        var wanted = procedure.Parameters.Count;

        if (given != wanted)
        {
            yield return new VisualDiagnostic(
                "vis-call-arity", VisualSeverity.Error, block.Id,
                $"\"{name}\" takes {CountOf(wanted, "argument")}, and this call passes {CountOf(given, "argument")}.",
                wanted == 0
                    ? "Drop the argument, or give the procedure a parameter."
                    : "Match the call to the procedure's parameters.");
        }

        if (block.Kind == "proc.call-value" && !procedure.Returns)
        {
            yield return new VisualDiagnostic(
                "vis-call-value-void", VisualSeverity.Error, block.Id,
                $"\"{name}\" does not return a value, so it cannot be used as one.",
                "Use the plain call block, or tick \"returns a value\" on the procedure.");
        }

        if (block.Kind is "proc.call" or "proc.call-with" && procedure.Returns)
        {
            yield return new VisualDiagnostic(
                "vis-call-value-unused", VisualSeverity.Warning, block.Id,
                $"\"{name}\" returns a value, and this call throws it away.",
                "Use the reporter form in a slot if you meant to use it.");
        }
    }

    /// <summary>
    /// How many arguments a call's text carries.
    /// </summary>
    /// <remarks>
    /// Split on commas rather than lines, because the emitter binds <c>name=value</c> pairs in
    /// declaration order and accepts either separator — so a count that only understood one of them would
    /// call a correct program wrong.
    /// </remarks>
    private static int CallArgumentCount(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return 0;
        }

        var count = 0;
        var seen = false;

        foreach (var character in args)
        {
            if (character is ',' or '\n' or '\r' or ';')
            {
                if (seen)
                {
                    count++;
                    seen = false;
                }

                continue;
            }

            seen = true;
        }

        return seen ? count + 1 : count;
    }

    private static string CountOf(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>What follows a cap, and a forever with nothing that yields inside it.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateBusyLoops(
        Block block,
        BlockDescriptor descriptor,
        StackWalk walk)
    {
        if (block.Kind == "control.forever")
        {
            var yields = block.TryBody("body").Any(child =>
                child.Walk().Any(descendant =>
                    BlockCatalog.Find(descendant.Kind) is { } inner
                    && (inner.Kind.StartsWith("control.wait", StringComparison.Ordinal)
                        || inner.Sdk.RequiresHost
                        || inner.Sdk.RequiresInteraction
                        || inner.Kind is "control.break" or "control.stop-script")));
            if (!yields)
            {
                yield return new VisualDiagnostic(
                    "vis-forever-no-wait", VisualSeverity.Warning, block.Id,
                    "This \"forever\" never waits and never calls the host.",
                    "The loop will run as fast as the host allows; add a wait.");
            }
        }
    }

    /// <summary>Unreachable statements: everything after a cap, at the top level of a body.</summary>
    private static void CheckTrailingCap(
        Block parent,
        IReadOnlyList<Block> body,
        string bodyName,
        List<VisualDiagnostic> diagnostics)
    {
        var capIndex = -1;
        for (var i = 0; i < body.Count; i++)
        {
            if (BlockCatalog.Find(body[i].Kind)?.Shape == BlockShape.Cap)
            {
                capIndex = i;
                break;
            }
        }
        if (capIndex < 0 || capIndex == body.Count - 1)
        {
            return;
        }

        foreach (var unreachable in body.Skip(capIndex + 1))
        {
            var descriptor = BlockCatalog.Find(unreachable.Kind);
            if (descriptor?.Shape == BlockShape.Cap)
            {
                // A second cap is its own problem, but not this one: it is unreachable, which is the
                // finding already being reported.
                continue;
            }

            diagnostics.Add(new VisualDiagnostic(
                "vis-cap-unreachable", VisualSeverity.Warning, unreachable.Id,
                $"This will never run: the \"{bodyName}\" body stops at the block before it.",
                "Move it above the finishing block, or delete it."));
        }
    }

    /// <summary>Name references from slots: parameters, host variables, lists, variables, procedures.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateNameReference(
        string blockId,
        string slotName,
        SlotType slotType,
        BlockInput input,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        // A reporter in the slot produces the name at runtime; the validator checks the literal and
        // the variable reference, which are what the user typed.
        var text = input.Kind switch
        {
            BlockInputKind.Text => input.Text,
            BlockInputKind.Variable => input.Variable,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        switch (slotType)
        {
            case SlotType.Parameter:
                // An empty declaration set means "nobody has told me what exists yet", not "nothing
                // exists". `VisualValidationContext.Empty` promises the first, and the App relies on it:
                // the page validates against it until a workspace's action parameters are resolved, and
                // treating an empty set as a plugin that declares nothing turned every parameter
                // reference on every canvas into a red "not a name anything declares".
                if (context.NamesResolved && !context.Parameters.Contains(text, StringComparer.Ordinal))
                {
                    yield return Unknown(
                        "vis-param-unknown", blockId, slotName, text,
                        "Add it on the Actions page, or pick an existing parameter.");
                }
                break;

            case SlotType.HostVariable or SlotType.UserVariable:
                if (context.NamesResolved && !context.HostVariables.Contains(text, StringComparer.Ordinal))
                {
                    yield return Unknown(
                        "vis-host-var-unknown", blockId, slotName, text,
                        "Declare it in the plugin's variables, or pick an existing one.",
                        docsPath: "features/variables");
                }
                break;

            case SlotType.Variable:
                // Its own code. This reported "vis-name-duplicate" - defined in Appendix F as two things
                // sharing a name - for a variable that does not exist at all, so one diagnostics list mixed
                // "you have a duplicate name" with "that name is not declared" under one heading, and a
                // user searching the log for duplicates found the wrong thing.
                if (!declarations.Contains(text)
                    && (!context.NamesResolved || !context.HostVariables.Contains(text, StringComparer.Ordinal)))
                {
                    yield return Unknown(
                        "vis-name-unknown", blockId, slotName, text,
                        "Set it first with one of the variable blocks, or declare it in the document.");
                }
                break;

            case SlotType.List:
                if (!declarations.Contains(text)
                    && (!context.NamesResolved || !context.HostVariables.Contains(text, StringComparer.Ordinal)))
                {
                    yield return Unknown(
                        "vis-name-unknown", blockId, slotName, text,
                        "Declare the list in the document, or set it first with a list block.");
                }
                break;

            case SlotType.Procedure:
                if (!procedures.ContainsKey(text))
                {
                    yield return Unknown(
                        "vis-procedure-missing", blockId, slotName, text,
                        "Define it, or fix the name.");
                }
                break;
        }
    }

    /// <summary>Name references from a disabled block, which is the only check a disabled block gets.</summary>
    private static IEnumerable<VisualDiagnostic> ValidateNameReferences(
        Block block,
        BlockDescriptor descriptor,
        VisualValidationContext context,
        ISet<string> declarations,
        IReadOnlyDictionary<string, ProcedureDeclaration> procedures)
    {
        foreach (var slot in descriptor.Slots ?? [])
        {
            if (block.Inputs.TryGetValue(slot.Name, out var input) && input is not null)
            {
                foreach (var diagnostic in ValidateNameReference(
                    block.Id, slot.Name, slot.Type, input, context, declarations, procedures))
                {
                    yield return diagnostic;
                }
            }
        }
    }

    /// <summary>The unknown-name diagnostic, shared so the codes stay consistent with Appendix F.</summary>
    private static VisualDiagnostic Unknown(
        string code,
        string blockId,
        string slotName,
        string text,
        string hint,
        string? docsPath = null) =>
        new(code, SeverityOf(code), blockId,
            $"\"{text}\" is not a name anything declares (in \"{slotName}\").",
            hint, docsPath);

    /// <summary>Names: non-empty, not reserved, unique per kind of declaration.</summary>
    private static IEnumerable<VisualDiagnostic> CheckNames(
        string what,
        IEnumerable<(string Name, VariableScope Scope)> declarations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in declarations)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                yield return new VisualDiagnostic(
                    "vis-name-duplicate", VisualSeverity.Error, null,
                    $"A {what} has an empty name.", "Give it a name in the document's panel.");
            }
            else if (!seen.Add(name))
            {
                yield return new VisualDiagnostic(
                    "vis-name-duplicate", VisualSeverity.Error, null,
                    $"Two {what}s are named \"{name}\".",
                    "Names must be unique; rename one of them.");
            }
        }
    }

    /// <summary>Severity for the reference codes, which Appendix F splits across two levels.</summary>
    private static VisualSeverity SeverityOf(string code) => code switch
    {
        "vis-param-unknown" or "vis-host-var-unknown" => VisualSeverity.Warning,
        _ => VisualSeverity.Error,
    };

    /// <summary>Whether a body of a container is a loop body, for break/continue depth.</summary>
    private static bool IsLoopBody(BlockDescriptor descriptor, string bodyName) =>
        descriptor.Kind switch
        {
            "control.repeat" or "control.forever" or "control.while" or "control.repeat-until" => true,
            "http.retry" => true,
            _ => false,
        };

    /// <summary>
    /// The path from the root to the statement being validated.
    /// </summary>
    /// <remarks>
    /// One instance per script rather than one per statement: the walk is the recursion state, and
    /// allocating a list per block was the only thing the first version of this file did that showed
    /// up in the stopwatch at all.
    ///
    /// <c>Procedure</c> carries the declaration being walked rather than a flag, because the return rules
    /// need to know whether *that* procedure returns a value — and a flag cannot answer it.
    /// </remarks>
    private sealed class StackWalk
    {
        private readonly Stack<(int LoopDepth, ProcedureDeclaration? Procedure)> _frames = new();

        public int LoopDepth => _frames.Count > 0 ? _frames.Peek().LoopDepth : 0;

        public bool InProcedure => Procedure is not null;

        /// <summary>The procedure whose body this statement is in, or null for a script.</summary>
        public ProcedureDeclaration? Procedure => _frames.Count > 0 ? _frames.Peek().Procedure : null;

        public void Push(int loopDepth, bool inProcedure, ProcedureDeclaration? procedure = null) =>
            _frames.Push((loopDepth, inProcedure ? procedure : null));

        public void Pop() => _frames.Pop();
    }
}
