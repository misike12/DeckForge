using System.Text;
using System.Text.RegularExpressions;
using DeckForge.Core.Blocks;
using DeckForge.Core.Code;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Compiles a <see cref="BlockProgram"/> into the C# body of
/// <c>IActionExecutor.ExecuteAsync</c>, wrapped in <c>// &lt;macrodeck-blocks&gt;</c> markers so
/// hand edits outside the markers survive regeneration and the canvas can re-read its own.
/// </summary>
/// <remarks>
/// <para>Three names are in scope, and <see cref="ActionGenerator"/> emits all three:</para>
/// <list type="bullet">
/// <item><c>context</c> - the <c>ActionExecutionContext</c> parameter.</item>
/// <item><c>_logger</c> - the executor's Serilog <c>ILogger</c>.</item>
/// <item><c>_integration</c> - the <c>IIntegrationContext</c> captured in
/// <c>InitializeAsync</c>. <c>Deck</c>, <c>Notifications</c>, <c>Widgets</c>,
/// <c>Variables</c>, <c>Scripts</c>, <c>Events</c> and <c>Messages</c> live there, not on
/// <c>ActionExecutionContext</c>.</item>
/// </list>
/// <para>
/// Every generated method is <c>async</c> and forwards <c>context.CancellationToken</c>, which is
/// what the SDK's own MDP3001 analyzer asks for.
/// </para>
/// </remarks>
public static partial class BlockCompiler
{
    public const string BeginMarker = "// <macrodeck-blocks>";
    public const string EndMarker = "// </macrodeck-blocks>";

    /// <summary>The nine <c>ActionErrorCodes</c> members, transcribed from the SDK.</summary>
    public static IReadOnlyList<string> ErrorCodes { get; } =
    [
        "NotConfigured", "NotConnected", "PermissionDenied", "ProviderError", "ProviderRejected",
        "InvalidParameter", "NotFound", "Timeout", "Unavailable",
    ];

    /// <summary>The log levels a <see cref="LogBlock"/> may name.</summary>
    public static IReadOnlyList<string> LogLevels { get; } =
        ["Verbose", "Debug", "Information", "Warning", "Error"];

    /// <summary>Notification levels a <see cref="NotifyBlock"/> may name.</summary>
    public static IReadOnlyList<string> NotificationLevels { get; } = ["Info", "Warning", "Error"];

    /// <summary>Every comparison an <see cref="IfBlock"/> may name.</summary>
    public static IReadOnlyList<string> ComparisonOperators { get; } =
    [
        "==", "!=", ">", "<", ">=", "<=", "isEmpty", "isNotEmpty", "isAvailable", "isNotAvailable",
        "contains", "notContains",
    ];

    /// <summary>Variable types a <see cref="SetVariableBlock"/> may declare.</summary>
    public static IReadOnlyList<string> VariableTypes { get; } = ["string", "number", "bool"];

    /// <summary>
    /// Whether a program calls the host, and so needs the action wired for a host context.
    /// </summary>
    /// <remarks>
    /// Everything except log, set-variable, if, return-result, delay, http-request and throw compiles
    /// to a call on <c>_integration</c>. Decided here rather than by scanning the compiled text,
    /// because "does this canvas need the host" is a question about the program, and answering it from
    /// the output would break the moment the compiler's spelling changed.
    /// </remarks>
    public static bool UsesHost(BlockProgram program) =>
        program.Statements.Any(UsesHost);

    private static bool UsesHost(BlockStatement statement) => statement switch
    {
        NotifyBlock or NavigateBlock or GoToParentBlock or GoBackBlock or ChangeProfileBlock
            or RunScriptBlock or PublishEventBlock or ReadVariableBlock or SetVariableValueBlock
            or ShowModalBlock or InvalidateIconBlock => true,
        IfBlock branch => branch.Then.Any(UsesHost) || branch.Else.Any(UsesHost),
        _ => false,
    };

    /// <summary>
    /// Whether the program always ends by returning, and so decides the action's result itself.
    /// </summary>
    /// <remarks>
    /// Decided from the last statement, not from the compiled text. A trailing return in the region
    /// makes the action's own final return unreachable, which is CS0162 on every build of the user's
    /// plugin - and the only two placements available are both bad, because a method cannot have two
    /// reachable trailing returns. The user's code is not edited to make the warning go away; the page
    /// says what happened instead.
    /// </remarks>
    public static bool EndsInReturn(BlockProgram program) =>
        program.Statements.Count > 0 && program.Statements[^1] is ReturnResultBlock;

    /// <summary>Every block kind, in canvas order, for the palette.</summary>
    public static IReadOnlyList<BlockKind> Kinds { get; } =
    [
        new("log", "Log a message", "Writes to the Macro Deck log, substituting parameters into {holes}.", ["log", "message"]),
        new("set-variable", "Set variable", "Reads a configured parameter into a typed local.", ["variable", "parameter"]),
        new("if", "If", "A conditional with a then and an else branch.", ["branch"]),
        new("return-result", "Return result", "Succeeds, fails with an error code, or accepts as pending.", ["result"]),
        new("delay", "Delay", "Waits, honouring cancellation.", ["time"]),
        new("http-request", "HTTP request", "An HTTP GET whose body lands in a variable.", ["network", "http"]),
        new("notify", "Notify", "Raises a host notification.", ["notification"]),
        new("navigate", "Open folder", "Opens a folder on the pressing client.", ["deck", "folder"]),
        new("go-to-parent", "Parent folder", "Moves to the parent folder.", ["deck"]),
        new("go-back", "Back", "Returns to the previously shown folder.", ["deck"]),
        new("change-profile", "Switch profile", "Switches to a profile's start folder.", ["deck", "profile"]),
        new("run-script", "Run script", "Runs a host script with named inputs.", ["script"]),
        new("publish-event", "Publish event", "Publishes an event occurrence.", ["event"]),
        new("read-variable", "Read host variable", "Reads a shared host variable into a local.", ["variable"]),
        new("set-variable-value", "Write host variable", "Writes a shared host variable.", ["variable"]),
        new("show-modal", "Show modal", "Asks the pressing client a question.", ["ui", "modal"]),
        new("invalidate-icon", "Invalidate icon", "Asks the host to re-fetch an action icon.", ["icon", "cache"]),
        new("throw", "Throw", "Fails the invocation with an exception.", ["error"]),
    ];

    /// <summary>A block kind, for the canvas palette.</summary>
    public sealed record BlockKind(string Id, string Name, string Summary, IReadOnlyList<string> Glyphs);

    public static BlockKind? FindKind(string? id) =>
        Kinds.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.Ordinal));

    /// <summary>Creates an empty statement of the named kind.</summary>
    public static BlockStatement Create(string kind) => kind switch
    {
        "log" => new LogBlock(),
        "set-variable" => new SetVariableBlock(),
        "if" => new IfBlock(),
        "return-result" => new ReturnResultBlock(),
        "delay" => new DelayBlock(),
        "http-request" => new HttpRequestBlock(),
        "notify" => new NotifyBlock(),
        "navigate" => new NavigateBlock(),
        "go-to-parent" => new GoToParentBlock(),
        "go-back" => new GoBackBlock(),
        "change-profile" => new ChangeProfileBlock(),
        "run-script" => new RunScriptBlock(),
        "publish-event" => new PublishEventBlock(),
        "read-variable" => new ReadVariableBlock(),
        "set-variable-value" => new SetVariableValueBlock(),
        "show-modal" => new ShowModalBlock(),
        "invalidate-icon" => new InvalidateIconBlock(),
        "throw" => new ThrowBlock(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown block kind."),
    };

    /// <summary>
    /// The names in scope inside the generated region that a block must not redeclare, and the
    /// names a block declares.
    /// </summary>
    /// <remarks>
    /// These are the three the class documents as always in scope. A block local that shadows
    /// <c>_integration</c> is worse than a collision: the guard the compiler emits tests
    /// <c>_integration is null</c>, so a local of that name makes the guard permanently false and
    /// every host call underneath it throws.
    /// </remarks>
    public static IReadOnlyCollection<string> ReservedNames { get; } =
        ["context", "_logger", "_integration"];

    /// <summary>The local a statement declares, or null when it declares none.</summary>
    private static string? DeclaredLocal(BlockStatement statement) => statement switch
    {
        SetVariableBlock set => set.VariableName,
        HttpRequestBlock http => http.IntoVariable,
        ReadVariableBlock read => read.IntoVariable,
        _ => null,
    };

    /// <summary>
    /// The names a program would declare that it must not: either they are already taken in the
    /// method the region goes into, or two blocks want the same one.
    /// </summary>
    /// <param name="program">The canvas to check.</param>
    /// <param name="reservedNames">
    /// Identifiers already in scope where the region will be spliced. <see cref="ReservedNames"/>
    /// is always included.
    /// </param>
    /// <returns>
    /// The offending names, in the order the canvas declares them. A name is listed once even when
    /// it collides with several things, because the user has one decision to make about it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A block's local is emitted into a method the user already wrote, so a name chosen on the
    /// canvas can collide with one already there - the stock example action declares
    /// <c>var message</c>, and naming a variable <c>message</c> on the canvas is entirely reasonable.
    /// That is a CS0128 in the user's plugin.
    /// </para>
    /// <para>
    /// The compiler could rename the declaration, and it does for two blocks in the same canvas.
    /// Renaming here is not safe: an <c>If</c> refers to its variable by name, so a declaration
    /// renamed to <c>message1</c> leaves every reference still reading <c>message</c> - which
    /// resolves to the host's local, and the conditional silently tests the wrong value. Two blocks
    /// wanting the same name have the same problem, for the same reason. A wrong answer is worse
    /// than a refused save, so this reports instead.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> LocalNameConflicts(
        BlockProgram program,
        IEnumerable<string>? reservedNames = null)
    {
        var reserved = new HashSet<string>(ReservedNames, StringComparer.Ordinal);
        foreach (var name in reservedNames ?? [])
        {
            reserved.Add(name);
        }

        var conflicts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in program.Statements)
        {
            foreach (var declared in DeclaredNames(statement))
            {
                // Already reported: the user has one decision to make about a name, not several.
                if (!seen.Add(declared))
                {
                    continue;
                }

                if (reserved.Contains(declared) || ReusedLater(program, declared))
                {
                    conflicts.Add(declared);
                }
            }
        }

        return conflicts;
    }

    /// <summary>Every local name a statement declares, including inside its branches.</summary>
    private static IEnumerable<string> DeclaredNames(BlockStatement statement)
    {
        if (DeclaredLocal(statement) is { Length: > 0 } name)
        {
            var identifier = CSharpCode.Identifier(name);
            if (identifier.Length > 0)
            {
                yield return identifier;
            }
        }

        if (statement is IfBlock branch)
        {
            // A branch is a nested scope, so a declaration there shadows an enclosing local - which
            // is CS0136 rather than CS0128, and just as uncompilable.
            foreach (var nestedThen in branch.Then)
            {
                foreach (var declaredThen in DeclaredNames(nestedThen))
                {
                    yield return declaredThen;
                }
            }

            foreach (var nestedElse in branch.Else)
            {
                foreach (var declaredElse in DeclaredNames(nestedElse))
                {
                    yield return declaredElse;
                }
            }
        }
    }

    /// <summary>True when more than one block in the program wants <paramref name="name"/>.</summary>
    private static bool ReusedLater(BlockProgram program, string name)
    {
        var count = 0;
        foreach (var statement in program.Statements)
        {
            foreach (var declared in DeclaredNames(statement))
            {
                if (declared == name && ++count > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Compiles the whole program, markers included.</summary>
    public static string Compile(BlockProgram program)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BeginMarker);
        sb.AppendLine("// Generated by DeckForge's Blocks canvas. Edit on the canvas; anything outside");
        sb.AppendLine("// these markers is yours and is preserved across regeneration.");

        var state = new EmitState();
        foreach (var statement in program.Statements)
        {
            Emit(statement, sb, state, indent: 1);
        }

        sb.AppendLine(EndMarker);
        return sb.ToString();
    }

    /// <summary>Compiles just the statements, without markers, for a live preview.</summary>
    public static string CompileBody(BlockProgram program)
    {
        var sb = new StringBuilder();
        var state = new EmitState();
        foreach (var statement in program.Statements)
        {
            Emit(statement, sb, state, indent: 0);
        }

        return sb.ToString();
    }

    /// <summary>Extracts the marker-wrapped region from a C# file body, if present.</summary>
    public static string? ExtractRegion(string csharpSource)
    {
        var begin = csharpSource.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = csharpSource.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < 0 || end < begin)
        {
            return null;
        }

        return csharpSource[(begin + BeginMarker.Length)..end].Trim();
    }

    /// <summary>
    /// Adds the <c>_integration</c> field and constructor parameter that host-calling blocks need.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A block region can navigate folders, invalidate widget icons, notify and read variables, and
    /// none of those live on <c>ActionExecutionContext</c> - they are on
    /// <c>IIntegrationContext</c>, reached through the <c>_integration</c> field that
    /// <see cref="ActionGenerator"/> emits into a generated action. The stock example action has no
    /// such field, so a canvas dropped into it produces code that does not compile, with eight
    /// <c>CS0103</c>s naming a field nobody declared.
    /// </para>
    /// <para>
    /// Callers used to fix this by hand with a <see cref="string.Replace(string, string, StringComparison)"/>
    /// that assumed four-space indentation. The official template is tab-indented, so the replace
    /// matched nothing, changed nothing, and failed silently - the defect only surfaced as a
    /// compiler error far from its cause.
    /// </para>
    /// </remarks>
    /// <returns>The rewritten source, the source unchanged if it is already wired, or null when the
    /// action's shape was not recognised.</returns>
    public static SourcePatch EnsureIntegrationContext(string source) =>
        ActionContextPatcher.PatchAction(source);

    /// <summary>
    /// Splices a compiled region into <paramref name="source"/>, replacing an existing region or
    /// inserting at <paramref name="anchor"/>.
    /// </summary>
    /// <param name="source">The action file's current body.</param>
    /// <param name="compiled">The output of <see cref="Compile"/>.</param>
    /// <param name="anchor">
    /// The method signature to insert after when no region exists yet, matched by
    /// <paramref name="anchor"/>.
    /// </param>
    /// <param name="indent">
    /// Indent applied to every line of the inserted region, in spaces. Ignored when the file
    /// indents with tabs, which the official template does: there a tab is one level, and a
    /// space-counted region would land at the wrong depth and leave two styles in one method.
    /// </param>
    /// <returns>The rewritten source, or null when no anchor could be found.</returns>
    public static string? Splice(string source, string compiled, string anchor, int indent = 8)
    {
        var tabbed = source.Contains("\n\t", StringComparison.Ordinal);
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // One indent level, in the file's own style, applied to every line including the first.
        string Apply(string text) => Reindent(text, tabbed ? "\t" : new string(' ', indent), nl);

        var begin = source.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = source.IndexOf(EndMarker, StringComparison.Ordinal);

        if (source.IndexOf(anchor, StringComparison.Ordinal) < 0)
        {
            return null;
        }

        // An existing region is taken out first, wherever it is, and the new one goes at the end of
        // the body. Replacing in place kept the region wherever the first save put it, so a file
        // saved by the previous version stayed wrong for ever: the region was replaced, but it was
        // still at the top of the method, still above the user's code.
        if (begin >= 0 && end > begin)
        {
            // The whitespace in front of the marker goes with it. Leaving it and indenting the
            // replacement on top of it grew the first line of the region by one level on every save,
            // without bound - save twice and the file was no longer the file the first save produced.
            //
            // "With it" means the region marker *and* the newline in front of it. Taking the marker only
            // left the carriage return of a CRLF file on its own line, so a second save indented that
            // stray line and the region walked one level deeper than the first save had put it - the same
            // unbounded drift, one character at a time and only on Windows line endings.
            var start = LineStartOf(source, begin);
            if (start > 0 && source[start - 1] == '\n' && start - 2 >= 0 && source[start - 2] == '\r')
            {
                start--;
            }

            var after = end + EndMarker.Length;
            var lineEnd = source.IndexOf('\n', after);
            source = source.Remove(start, (lineEnd < 0 ? after : lineEnd + 1) - start);
        }

        // Every position is resolved here, after the removal above, and not one index earlier.
        // Taking the region out deletes characters from inside the method body, so every index
        // after it moves earlier by however many characters went, including the index of the
        // method's own closing brace. Resolving the brace first and using it after the removal
        // left it pointing past the end of the string: LastTopLevelReturn then scanned beyond the
        // end of the executor and could pick up the return of a different method further down the
        // file, and when it found nothing the insert either landed outside the executor or threw
        // outright. It only ever showed up on the second save of a program, because the first save
        // has no region to remove and so never goes near this branch.
        var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
        var open = source.IndexOf('{', anchorIndex);
        if (open < 0)
        {
            return null;
        }

        var close = MatchingBrace(source, open);
        if (close < 0)
        {
            return null;
        }

        // The closing line has to line up with the method signature, in the file's own style.
        var closingIndent = LineIndentOf(source, anchorIndex) + (tabbed ? "\t" : new string(' ', indent));

        var braceLine = LineIndentOf(source, open);

        // Whether the opening and closing braces share a line. Asked as "is there a newline between
        // them" rather than by comparing against a computed line end, because a CRLF file's line end is
        // two characters past the `\r` and the closing brace sits *before* it - comparing indices made
        // this false on exactly the line endings Windows files have.
        var braceLineEnd = source.IndexOf('\n', open);
        var isSingleLineBrace = braceLineEnd < 0 || braceLineEnd >= close;

        // Where in the body the region goes: after everything that is already there, but before the
        // method's own final return.
        //
        // A block program usually ends in a return, so appending it after the method's own trailing
        // return made that return unreachable - CS0162 on every build - and the region was dead code.
        // Placing it before the final return means the code that was already there runs first, a
        // canvas that returns decides the result, and a canvas that does not fall through to whatever
        // the action already did.
        var insertAt = LastTopLevelReturn(source, open, close) ?? close;
        var indentation = tabbed ? "\t" : new string(' ', indent);

        // A body whose opening brace is also its closing brace has nowhere to put a statement. That is
        // not a widget handler and a lifecycle hook written slightly differently; it is the stock
        // template's `ShutdownAsync() => Task.CompletedTask;` expanded to a block with an empty body.
        //
        // The first version of this inserted after the opening brace, which closed the empty block and
        // left the region's statements as siblings *after* the method - inside the class, after a method
        // whose signature has no body. It still compiled if the blocks happened to be statements a class
        // member list accepts, and produced a file no user could read. So the empty-body case opens the
        // block up instead.
        if (isSingleLineBrace && insertAt == close)
        {
            var opened = source.Insert(close, nl + braceLine + indentation);
            var newClose = close + (nl + braceLine + indentation).Length;

            return opened.Insert(newClose, nl + braceLine);
        }

        var insertion = nl + Apply(compiled).TrimEnd() + nl + LineIndentOf(source, anchorIndex);

        if (insertAt != close)
        {
            return source.Insert(insertAt, insertion);
        }

        // Appending the closing brace's indent to what is already in front of the brace indents it twice,
        // and then again on every save after that: the region was right on the first save and one level
        // deeper on the second, which is the drift this method is supposed to have outgrown. So the line
        // is rebuilt instead - its own indentation is replaced by the signature's, rather than added to.
        var closeLineStart = LineStartOf(source, close);
        return source[..closeLineStart]
            + LineIndentOf(source, anchorIndex)
            + Apply(compiled).TrimEnd()
            + nl
            + LineIndentOf(source, anchorIndex)
            + source[close..];
    }

    /// <summary>
    /// The index of the last <c>return</c> directly in the body, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Depth one only, so a return inside a local function, a lambda or a nested block is not
    /// mistaken for the method's own. That is the whole reason this walks the body rather than
    /// taking the last <c>return</c> in the text.
    /// <para>
    /// Public so it can be tested directly. Asserting the placement through the spliced output does
    /// not work: the region contains returns of its own, so "the region's marker is before the last
    /// return in the file" is true whichever end the region went on - the test passed while the
    /// region was still being appended after the return it was supposed to precede.
    /// </para>
    /// </remarks>
    public static int? LastTopLevelReturn(string source, int open, int close)
    {
        var depth = 0;
        int? found = null;
        var lineComment = false;
        var blockComment = false;
        var text = false;
        var character = false;
        var verbatim = false;

        for (var i = open; i < close && i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (lineComment)
            {
                if (c == '\n') { lineComment = false; }
                continue;
            }

            if (blockComment)
            {
                if (c == '*' && next == '/') { blockComment = false; i++; }
                continue;
            }

            if (text)
            {
                if (verbatim && c == '"' && next == '"') { i++; continue; }
                if (c == '"' && (!verbatim || next != '"')) { text = false; }
                if (c == '\\' && !verbatim) { i++; }
                continue;
            }

            if (character)
            {
                if (c == '\\') { i++; }
                else if (c == '\'') { character = false; }
                continue;
            }

            switch (c)
            {
                case '/' when next == '/': lineComment = true; i++; continue;
                case '/' when next == '*': blockComment = true; i++; continue;
                case '@' when next == '"': verbatim = true; text = true; i++; continue;
                case '"': text = true; continue;
                case '\'': character = true; continue;
                case '{': depth++; continue;
                case '}': depth--; continue;
            }

            if (depth == 1 && c == 'r' && source.AsSpan(i).StartsWith("return", StringComparison.Ordinal))
            {
                var before = i == 0 || !char.IsLetterOrDigit(source[i - 1]);
                var after = i + 6 >= source.Length || !char.IsLetterOrDigit(source[i + 6]);
                if (before && after)
                {
                    found = i;
                }
            }
        }

        return found;
    }


    /// <summary>The index of the first character of the line containing <paramref name="index"/>.</summary>
    private static int LineStartOf(string source, int index) =>
        source.LastIndexOf('\n', Math.Clamp(index, 0, Math.Max(source.Length - 1, 0))) + 1;

    /// <summary>
    /// The index of the end of the line containing <paramref name="index"/>, counting the line break.
    /// </summary>
    /// <remarks>
    /// The newline is included, and it is the character *it* is that matters: a CRLF file's lines end
    /// with two characters, so an insertion built for the LF case leaves a stray carriage return in the
    /// middle of a line and every later comparison of that file is off by one.
    /// </remarks>
    private static int LineEndOf(string source, int index)
    {
        var nl = source.IndexOf('\n', Math.Clamp(index, 0, Math.Max(source.Length - 1, 0)));
        if (nl < 0)
        {
            return source.Length;
        }

        return nl + 1;
    }

    /// <summary>
    /// The leading whitespace of the line containing <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// Not <see cref="IntegrationPatcher.IndentOf"/>: that searches backwards to the previous
    /// newline and includes it, so for an index sitting at a line start it describes the line
    /// before, and reports no indentation at all.
    /// </remarks>
    private static string LineIndentOf(string source, int index)
    {
        var start = LineStartOf(source, index);
        var end = start;
        while (end < source.Length && source[end] is ' ' or '\t')
        {
            end++;
        }

        return source[start..end];
    }

    /// <summary>
    /// Puts <paramref name="prefix"/> in front of every line, and normalises the line endings to
    /// <paramref name="nl"/> at the same time.
    /// </summary>
    private static string Reindent(string text, string prefix, string nl)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
            {
                lines[i] = prefix + lines[i];
            }
        }

        return string.Join(nl, lines);
    }

    /// <summary>
    /// Makes the executor <c>async</c> and repairs the returns the stock template wrote for a
    /// synchronous method, so a canvas containing a delay, an HTTP call or a navigation step
    /// cannot land a bare <c>await</c> in a method that cannot hold one.
    /// </summary>
    /// <remarks>
    /// Adding <c>async</c> alone is not enough: the template's body ends
    /// <c>return ActionResult.SucceededTask;</c> and its guard returns
    /// <c>Task.FromResult(ActionResult.Failed(...))</c>, and both stop being valid returns the
    /// moment the method becomes <c>async Task&lt;ActionResult&gt;</c> - <c>CS4016</c> and
    /// <c>CS0029</c> respectively. Only the body of <c>ExecuteAsync</c> is touched.
    /// </remarks>
    public static string EnsureAsyncExecutor(string source)
    {
        const string signature = "Task<ActionResult> ExecuteAsync(ActionExecutionContext context)";
        var index = source.IndexOf(signature, StringComparison.Ordinal);
        if (index < 0)
        {
            return source;
        }

        // Already async: nothing to do about the signature, but the body may still return tasks -
        // a hand-edited executor can carry `return ActionResult.SucceededTask;` and then a visual
        // region's awaits make that invalid (CS4016). In an async method those returns are always
        // wrong or pointless, so repairing them is safe whatever put them there.
        var lineStart = source.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        if (source[lineStart..index].TrimEnd().EndsWith("async", StringComparison.Ordinal))
        {
            var asyncOpen = source.IndexOf('{', index);
            var asyncClose = MatchingBrace(source, asyncOpen);
            if (asyncOpen < 0 || asyncClose < 0)
            {
                return source;
            }

            return source[..(asyncOpen + 1)]
                + RepairReturns(source[(asyncOpen + 1)..asyncClose])
                + source[asyncClose..];
        }

        var open = source.IndexOf('{', index);
        var close = MatchingBrace(source, open);
        if (open < 0 || close < 0)
        {
            return source;
        }

        return source[..index] + "async " + source[index..(open + 1)] + RepairReturns(source[(open + 1)..close]) + source[close..];
    }

    /// <summary>Turns task-returning returns into value returns. Used by both async paths.</summary>
    private static string RepairReturns(string body)
    {
        body = ReturnSucceededTaskPattern().Replace(body, "return ActionResult.Success();");
        return ReturnFromResultPattern().Replace(body, "return $1;");
    }

    [GeneratedRegex(@"return\s+ActionResult\.SucceededTask\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex ReturnSucceededTaskPattern();

    /// <summary>Matches <c>return Task.FromResult(X);</c> and keeps X as group one.</summary>
    [GeneratedRegex(@"return\s+Task\.FromResult\((?<value>.+?)\)\s*;", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ReturnFromResultPattern();

    /// <summary>Index of the brace closing the one at <paramref name="open"/>, or -1.</summary>
    /// <remarks>
    /// Public so the placement above can be tested without reaching into private state. The counting
    /// skips string and character literals and both comment styles, so a brace inside a log message
    /// or a comment does not close the body early.
    /// </remarks>
    public static int MatchingBraceForTest(string source, int open) => MatchingBrace(source, open);

    private static int MatchingBrace(string source, int open)
    {
        if (open < 0 || open >= source.Length)
        {
            return -1;
        }

        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            switch (c)
            {
                case '/' when next == '/':
                    inLineComment = true;
                    i++;
                    continue;
                case '/' when next == '*':
                    inBlockComment = true;
                    i++;
                    continue;
                case '"':
                    inString = true;
                    continue;
                case '\'':
                    inChar = true;
                    continue;
                case '{':
                    depth++;
                    continue;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    continue;
            }
        }

        return -1;
    }

    /// <summary>Mutable state threaded through one compile: unique names, collected usings.</summary>
    private sealed class EmitState
    {
        private int _counter;
        private readonly HashSet<string> _locals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usings = new(StringComparer.Ordinal);

        public int Next() => _counter++;

        /// <summary>A unique local name derived from a designer-supplied one.</summary>
        public string Local(string? wanted)
        {
            var baseName = CSharpCode.Identifier(wanted);
            if (!_locals.Add(baseName))
            {
                var candidate = $"{baseName}{_counter++}";
                while (!_locals.Add(candidate))
                {
                    candidate = $"{baseName}{_counter++}";
                }

                baseName = candidate;
            }

            return baseName;
        }

        /// <summary>True when the name is already taken, so a reuse needs disambiguation.</summary>
        public bool IsTaken(string name) => _locals.Contains(name);

        public IReadOnlyCollection<string> Usings => _usings;

        public void Use(string ns) => _usings.Add(ns);
    }

    private static void Emit(BlockStatement statement, StringBuilder sb, EmitState state, int indent)
    {
        var pad = new string(' ', indent * 4);
        switch (statement)
        {
            case LogBlock log:
                EmitLog(log, sb, state, pad);
                break;

            case SetVariableBlock set:
                EmitSetVariable(set, sb, state, pad);
                break;

            case IfBlock ifBlock:
                EmitIf(ifBlock, sb, state, indent, pad);
                break;

            case ReturnResultBlock ret:
                EmitReturnResult(ret, sb, pad);
                break;

            case DelayBlock delay:
                sb.AppendLine($"{pad}await Task.Delay({Math.Max(0, delay.Milliseconds)}, context.CancellationToken);");
                break;

            case HttpRequestBlock http:
                EmitHttp(http, sb, state, pad);
                break;

            case NotifyBlock notify:
                EmitGuard(sb, state, pad);
                EmitNotify(notify, sb, pad);
                break;

            case NavigateBlock navigate:
                EmitGuard(sb, state, pad);
                sb.AppendLine($"{pad}await _integration.Deck.ChangeFolderAsync({CSharpCode.StringLiteral(navigate.FolderId)}, context.OriginClientId, context.CancellationToken);");
                break;

            case GoToParentBlock:
                EmitGuard(sb, state, pad);
                sb.AppendLine($"{pad}await _integration.Deck.GoToParentAsync(context.OriginClientId, context.CancellationToken);");
                break;

            case GoBackBlock:
                EmitGuard(sb, state, pad);
                sb.AppendLine($"{pad}await _integration.Deck.GoBackAsync(context.OriginClientId, context.CancellationToken);");
                break;

            case ChangeProfileBlock profile:
                EmitGuard(sb, state, pad);
                sb.AppendLine($"{pad}await _integration.Deck.ChangeProfileAsync({CSharpCode.StringLiteral(profile.ProfileId)}, context.OriginClientId, context.CancellationToken);");
                break;

            case RunScriptBlock script:
                EmitGuard(sb, state, pad);
                EmitRunScript(script, sb, state, pad);
                break;

            case PublishEventBlock publish:
                EmitGuard(sb, state, pad);
                EmitPublishEvent(publish, sb, state, pad);
                break;

            case ReadVariableBlock read:
                EmitGuard(sb, state, pad);
                EmitReadVariable(read, sb, state, pad);
                break;

            case SetVariableValueBlock write:
                EmitGuard(sb, state, pad);
                EmitSetVariableValue(write, sb, state, pad);
                break;

            case ShowModalBlock modal:
                // A modal opens through context.Ui, which is null until the session is established,
                // exactly like the other host surfaces - so it needs the same guard. It was the one
                // host block without one, and the omission surfaced as a CS8602 in the user's plugin.
                EmitGuard(sb, state, pad);
                EmitShowModal(modal, sb, state, pad);
                break;

            case InvalidateIconBlock icon:
                EmitGuard(sb, state, pad);
                sb.AppendLine($"{pad}await _integration.Widgets.InvalidateIconAsync({CSharpCode.StringLiteral(icon.ActionId)}, context.CancellationToken);");
                break;

            case ThrowBlock throwBlock:
                sb.AppendLine($"{pad}throw new InvalidOperationException({CSharpCode.StringLiteral(throwBlock.Message)});");
                break;

            default:
                sb.AppendLine($"{pad}// unsupported block: {statement.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// Emits the null check that precedes every host call. <c>IIntegrationContext.Deck</c> and
    /// its siblings are declared non-nullable, but they are null until the session is
    /// established, so the generated code checks rather than dereferences.
    /// </summary>
    private static void EmitGuard(StringBuilder sb, EmitState state, string pad)
    {
        if (state.IsTaken("_integration"))
        {
            return;
        }

        sb.AppendLine($"{pad}if (_integration is null)");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    return ActionResult.Failed(ActionErrorCodes.NotConnected, \"No host session is established yet.\");");
        sb.AppendLine($"{pad}}}");
    }

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex HolePattern();

    private static void EmitLog(LogBlock log, StringBuilder sb, EmitState state, string pad)
    {
        var level = LogLevels.Contains(log.Level, StringComparer.Ordinal) ? log.Level : "Information";
        var template = log.Template ?? string.Empty;
        var matches = HolePattern().Matches(template);

        if (matches.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(log.Parameter))
            {
                sb.AppendLine($"{pad}_logger.{level}({CSharpCode.StringLiteral(template)});");
            }
            else
            {
                EmitLookup(sb, log.Parameter, state, pad, out var expression);
                sb.AppendLine($"{pad}_logger.{level}({CSharpCode.StringLiteral(template)}, {expression});");
            }

            return;
        }

        // Rewrite {name} holes to positional placeholders and resolve each from the configured
        // parameters, so the message template survives a round trip through the canvas.
        var message = HolePattern().Replace(template, match => "{" + state.Next().ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        var order = matches.Select(m => m.Groups[1].Value).ToList();
        var args = new List<string>();
        for (var i = 0; i < order.Count; i++)
        {
            EmitLookup(sb, order[i], state, pad, out var expression);
            args.Add(expression);
        }

        if (!string.IsNullOrWhiteSpace(log.Parameter))
        {
            EmitLookup(sb, log.Parameter, state, pad, out var extra);
            args.Add(extra);
        }

        sb.AppendLine($"{pad}_logger.{level}({CSharpCode.StringLiteral(message)}{(args.Count == 0 ? string.Empty : ", " + string.Join(", ", args))});");
    }

    /// <summary>
    /// Emits a bounds-checked read of one configured parameter into a local, and returns the
    /// expression that reads it. The local name is unique per lookup, so reading the same
    /// parameter twice in one program cannot collide.
    /// </summary>
    private static void EmitLookup(StringBuilder sb, string? parameterName, EmitState state, string pad, out string expression)
    {
        var index = state.Next();
        var local = $"_arg{index}";
        var key = CSharpCode.StringLiteral(parameterName);
        sb.AppendLine($"{pad}var {local} = context.Parameters.TryGetValue({key}, out var _p{index}) && _p{index} is not null ? _p{index}.ToString() : null;");
        expression = local;
    }

    private static void EmitSetVariable(SetVariableBlock set, StringBuilder sb, EmitState state, string pad)
    {
        var name = state.Local(set.VariableName);
        var type = VariableTypes.Contains(set.Type, StringComparer.Ordinal) ? set.Type : "string";

        if (set.FromParameter is not null)
        {
            var index = state.Next();
            var raw = $"_raw{index}";
            var key = CSharpCode.StringLiteral(set.FromParameter);
            sb.AppendLine($"{pad}var {raw} = context.Parameters.TryGetValue({key}, out var _p{index}) ? _p{index} : null;");
            sb.AppendLine($"{pad}{type switch
            {
                "number" => $"double? {name} = {raw} is null ? null : Convert.ToDouble({raw}, System.Globalization.CultureInfo.InvariantCulture);",
                "bool" => $"bool? {name} = {raw} is null ? null : {raw} is true;",
                _ => $"string? {name} = {raw}?.ToString();",
            }}");

            if (set.Required)
            {
                var check = type == "bool" ? $"{name} is null" : $"string.IsNullOrWhiteSpace({name})";
                sb.AppendLine($"{pad}if ({check})");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    return ActionResult.Failed(ActionErrorCodes.InvalidParameter, {CSharpCode.StringLiteral($"Parameter {set.FromParameter} is required.")});");
                sb.AppendLine($"{pad}}}");
            }
        }
        else
        {
            sb.AppendLine($"{pad}{type switch
            {
                "number" => $"double? {name} = {CSharpCode.NumberLiteral(set.Literal)};",
                "bool" => $"bool? {name} = {CSharpCode.BoolLiteral(set.Literal)};",
                _ => $"string? {name} = {CSharpCode.StringLiteral(set.Literal)};",
            }}");
        }
    }

    private static void EmitIf(IfBlock ifBlock, StringBuilder sb, EmitState state, int indent, string pad)
    {
        var left = CSharpCode.Identifier(ifBlock.LeftVariable);
        var condition = BuildCondition(sb, left, ifBlock.Operator, ifBlock.RightLiteral, pad, state);

        sb.AppendLine($"{pad}if ({condition})");
        sb.AppendLine($"{pad}{{");
        foreach (var child in ifBlock.Then)
        {
            Emit(child, sb, state, indent + 1);
        }

        sb.AppendLine($"{pad}}}");

        if (ifBlock.Else.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{pad}else");
        sb.AppendLine($"{pad}{{");
        foreach (var child in ifBlock.Else)
        {
            Emit(child, sb, state, indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// Emits the condition, and a local holding the operand rendered as invariant text.
    /// </summary>
    /// <remarks>
    /// The operand is first widened to <c>object</c> and switched on. Comparing directly does
    /// not work: SetVariableBlock declares the local as <c>string?</c>, <c>double?</c> or
    /// <c>bool?</c>, so <c>string.CompareOrdinal(count, "1")</c> is <c>CS1503</c>,
    /// <c>"a" &gt; "b"</c> is <c>CS0019</c>, and a pattern whose input type is statically
    /// <c>string</c> makes an <c>IFormattable</c> arm <c>CS8121</c> rather than an error you can
    /// read.
    /// </remarks>
    private static string BuildCondition(StringBuilder sb, string left, string op, string? rightLiteral, string pad, EmitState state)
    {
        var slot = state.Next();
        var boxed = $"_operand{slot}";
        var text = $"_text{slot}";

        sb.AppendLine($"{pad}object? {boxed} = {left};");
        sb.AppendLine($"{pad}string? {text} = {boxed} switch");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    null => null,");
        sb.AppendLine($"{pad}    string s => s,");
        sb.AppendLine($"{pad}    IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),");
        sb.AppendLine($"{pad}    _ => {boxed}.ToString(),");
        sb.AppendLine($"{pad}}};");

        var right = CSharpCode.StringLiteral(rightLiteral ?? string.Empty);
        // A one-character needle is emitted as a char literal, not a string. string.Contains(string,
        // StringComparison) is correct but reports CA1847, and generated code that reports a warning
        // on every build of the user's plugin is a warning they will come to ask about.
        var needle = (rightLiteral ?? string.Empty) is [var only] ? CSharpCode.CharLiteral(only) : right;
        return op switch
        {
            "isEmpty" => $"{text} is null || string.IsNullOrWhiteSpace({text})",
            "isNotEmpty" => $"{text} is not null && !string.IsNullOrWhiteSpace({text})",
            "isAvailable" => $"{boxed} is not null",
            "isNotAvailable" => $"{boxed} is null",
            "==" => $"string.Equals({text}, {right}, StringComparison.Ordinal)",
            "!=" => $"!string.Equals({text}, {right}, StringComparison.Ordinal)",
            ">" => $"string.CompareOrdinal({text}, {right}) > 0",
            "<" => $"string.CompareOrdinal({text}, {right}) < 0",
            ">=" => $"string.CompareOrdinal({text}, {right}) >= 0",
            "<=" => $"string.CompareOrdinal({text}, {right}) <= 0",
            "contains" => $"({text} ?? string.Empty).Contains({needle}, StringComparison.Ordinal)",
            "notContains" => $"!({text} ?? string.Empty).Contains({needle}, StringComparison.Ordinal)",
            _ => $"string.Equals({text}, {right}, StringComparison.Ordinal)",
        };
    }

    private static void EmitReturnResult(ReturnResultBlock ret, StringBuilder sb, string pad)
    {
        switch (ret.Outcome)
        {
            case "failed":
            {
                var code = ErrorCodes.Contains(ret.ErrorCode ?? string.Empty, StringComparer.Ordinal)
                    ? ret.ErrorCode!
                    : "ProviderError";
                sb.AppendLine($"{pad}return ActionResult.Failed(ActionErrorCodes.{code}, {CSharpCode.StringLiteral(ret.Message)});");
                break;
            }

            case "accepted":
                sb.AppendLine($"{pad}return ActionResult.Accepted({CSharpCode.StringLiteral(ret.Message)});");
                break;

            default:
                sb.AppendLine($"{pad}return ActionResult.Success();");
                break;
        }
    }

    private static void EmitHttp(HttpRequestBlock http, StringBuilder sb, EmitState state, string pad)
    {
        var name = state.Local(http.IntoVariable);
        var index = state.Next();
        var url = CSharpCode.StringLiteral(http.Url);
        var hasToken = !string.IsNullOrWhiteSpace(http.BearerTokenParameter);

        // One HttpClient per invocation is the wrong pattern, but a canvas block has nowhere to
        // cache one; the generated comment points at IHttpClientFactory, which the integration can inject.
        sb.AppendLine($"{pad}// For repeated calls, inject IHttpClientFactory on the integration and pass the client in.");
        sb.AppendLine($"{pad}using var client = new System.Net.Http.HttpClient();");
        if (hasToken)
        {
            var token = CSharpCode.StringLiteral(http.BearerTokenParameter);
            sb.AppendLine($"{pad}var _token{index} = context.Parameters.TryGetValue({token}, out var _t{index}) ? _t{index}?.ToString() : null;");
            sb.AppendLine($"{pad}if (!string.IsNullOrEmpty(_token{index}))");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", _token{index});");
            sb.AppendLine($"{pad}}}");
        }

        sb.AppendLine($"{pad}string? {name} = await client.GetStringAsync({url}, context.CancellationToken);");
    }

    private static void EmitNotify(NotifyBlock notify, StringBuilder sb, string pad)
    {
        var level = NotificationLevels.Contains(notify.Level, StringComparer.Ordinal) ? notify.Level : "Info";
        var body = new System.Text.StringBuilder();

        // Fully qualified, because the region is spliced into the middle of a method: a `using`
        // directive is only legal at the top of a file, so the compiler cannot add one. Naming the
        // namespace here is the only way the type resolves, and getting it wrong is a CS0246 in the
        // user's plugin rather than anything a test here would notice.
        body.Append("new MacroDeck.Sdk.Notifications.UserNotificationRequest { ");
        body.Append($"Title = {CSharpCode.StringLiteral(notify.Title)}");
        if (!string.IsNullOrEmpty(notify.Message))
        {
            body.Append($", Message = {CSharpCode.StringLiteral(notify.Message)}");
        }

        if (!string.IsNullOrWhiteSpace(notify.Key))
        {
            body.Append($", Key = {CSharpCode.StringLiteral(notify.Key)}");
        }

        body.Append(" }");
        sb.AppendLine($"{pad}_integration.Notifications.Notify({body});");
    }

    private static void EmitRunScript(RunScriptBlock script, StringBuilder sb, EmitState state, string pad)
    {
        var outcome = $"_outcome{state.Next()}";
        var pairs = ParsePairs(script.Inputs);
        if (pairs.Count == 0)
        {
            sb.AppendLine($"{pad}var {outcome} = await _integration.Scripts.RunAsync({CSharpCode.StringLiteral(script.ScriptId)}, null, context.OriginClientId, context.OwnerWidgetId, context.CancellationToken);");
            return;
        }

        var name = $"_inputs{state.Next()}";
        sb.AppendLine($"{pad}var {name} = new Dictionary<string, object?>");
        sb.AppendLine($"{pad}{{");
        foreach (var pair in pairs)
        {
            var slot = state.Next();
            sb.AppendLine($"{pad}    [{CSharpCode.StringLiteral(pair.Key)}] = context.Parameters.TryGetValue({CSharpCode.StringLiteral(pair.Value)}, out var _i{slot}) ? _i{slot} : null,");
        }

        sb.AppendLine($"{pad}}};");
        sb.AppendLine($"{pad}var {outcome} = await _integration.Scripts.RunAsync({CSharpCode.StringLiteral(script.ScriptId)}, {name}, context.OriginClientId, context.OwnerWidgetId, context.CancellationToken);");
    }

    private static void EmitPublishEvent(PublishEventBlock publish, StringBuilder sb, EmitState state, string pad)
    {
        var pairs = ParsePairs(publish.Payload);
        if (pairs.Count == 0)
        {
            sb.AppendLine($"{pad}_integration.Events.Publish({CSharpCode.StringLiteral(publish.EventId)});");
            return;
        }

        var name = $"_payload{state.Next()}";
        sb.AppendLine($"{pad}var {name} = new Dictionary<string, object?>");
        sb.AppendLine($"{pad}{{");
        foreach (var pair in pairs)
        {
            var slot = state.Next();
            sb.AppendLine($"{pad}    [{CSharpCode.StringLiteral(pair.Key)}] = context.Parameters.TryGetValue({CSharpCode.StringLiteral(pair.Value)}, out var _e{slot}) ? _e{slot} : null,");
        }

        sb.AppendLine($"{pad}}};");
        sb.AppendLine($"{pad}_integration.Events.Publish({CSharpCode.StringLiteral(publish.EventId)}, {name});");
    }

    private static void EmitReadVariable(ReadVariableBlock read, StringBuilder sb, EmitState state, string pad)
    {
        var name = state.Local(read.IntoVariable);
        var index = state.Next();
        sb.AppendLine($"{pad}var _var{index} = await _integration.Variables.GetByNameAsync({CSharpCode.StringLiteral(read.VariableName)});");
        sb.AppendLine($"{pad}string? {name} = _var{index}?.Value?.ToString();");
    }

    private static void EmitSetVariableValue(SetVariableValueBlock write, StringBuilder sb, EmitState state, string pad)
    {
        var index = state.Next();
        var name = CSharpCode.StringLiteral(write.VariableName);
        var value = write.UseParameter
            ? $"context.Parameters.TryGetValue({CSharpCode.StringLiteral(write.Value)}, out var _w{index}) ? _w{index} : null"
            : CSharpCode.StringLiteral(write.Value);

        sb.AppendLine($"{pad}var _handle{index} = await _integration.Variables.GetByNameAsync({name});");
        sb.AppendLine($"{pad}if (_handle{index} is not null)");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    await _integration.Variables.SetValueAsync(_handle{index}!.Id, {value});");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine($"{pad}else");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    _logger.Warning(\"No host variable named {{VariableName}}.\", {name});");
        sb.AppendLine($"{pad}}}");
    }

    private static void EmitShowModal(ShowModalBlock modal, StringBuilder sb, EmitState state, string pad)
    {
        var index = state.Next();
        var body = new StringBuilder();
        body.Append("new MacroDeck.Sdk.Ui.ModalDefinition { ");
        body.Append($"ViewId = {CSharpCode.StringLiteral(modal.ViewId)}");
        if (!string.IsNullOrEmpty(modal.Title))
        {
            body.Append($", Title = {CSharpCode.StringLiteral(modal.Title)}");
        }

        var pairs = ParsePairs(modal.Data);
        if (pairs.Count > 0)
        {
            // ModalDefinition.Data is IReadOnlyDictionary<string, JsonElement>, so each value is
            // a JSON document element rather than a bare string.
            var entries = pairs
                .Select(p => $"[{CSharpCode.StringLiteral(p.Key)}] = System.Text.Json.JsonDocument.Parse({CSharpCode.StringLiteral(p.Value)}).RootElement.Clone()")
                .ToList();
            body.Append($", Data = new Dictionary<string, System.Text.Json.JsonElement> {{ {string.Join(", ", entries)} }}");
        }

        body.Append(" }");

        // A modal names a view the plugin's own IUiProvider serves, so the block only works once
        // the integration implements one. Without a session there is nothing to ask, so the
        // non-generic overload - open and do not wait - is the right one: a flow must not stall.
        sb.AppendLine($"{pad}if (context.Ui is not null)");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    await context.Ui.ShowModalAsync(context.OriginClientId, {body}, context.CancellationToken);");
        sb.AppendLine($"{pad}}}");
        _ = index;
        _ = state;
    }

    private static Dictionary<string, string> ParsePairs(string? text)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2);
            var key = parts[0].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            pairs[key] = parts.Length > 1 ? parts[1].Trim() : key;
        }

        return pairs;
    }
}
