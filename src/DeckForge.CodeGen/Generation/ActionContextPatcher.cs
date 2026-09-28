using System.Text;
using System.Text.RegularExpressions;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Gives an action access to the host through <c>IIntegrationContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// A block region calls the host: it changes folders and profiles, invalidates widget icons,
/// notifies, reads and writes variables, publishes events. None of that is on
/// <c>ActionExecutionContext</c> - it is on <c>IIntegrationContext</c>, which the SDK hands to
/// exactly one place, <c>IIntegration.InitializeAsync</c>.
/// </para>
/// <para>
/// There is no route from there to an action. <c>IActionDefinition.CreateExecutor()</c> takes no
/// parameters, and the SDK defines no <c>IIntegrationContext</c>-aware interface for actions, so
/// the context has to be carried by hand: the integration remembers it, hands it to the actions
/// that asked for it, and those actions pass it into the executor they create.
/// </para>
/// <para>
/// Without this, a generated action compiles with a <c>_integration</c> field that is
/// permanently null - <see cref="ActionGenerator"/>'s own <c>new Executor(_logger)</c> never
/// supplies it - and every host-calling block throws <c>NullReferenceException</c> the first time a
/// user runs it. That is why <see cref="BlockCompiler.EnsureIntegrationContext"/> exists: it is the
/// only thing standing between a canvas that looks correct and a plugin that throws.
/// </para>
/// </remarks>
public static class ActionContextPatcher
{
    /// <summary>
    /// The contract the SDK does not have. Declared into the generated project so an action can
    /// ask for the context without the SDK having to know actions exist.
    /// </summary>
    public const string InterfaceName = "IIntegrationContextAware";

    /// <summary>The file the interface is written to, relative to the plugin's source folder.</summary>
    public const string InterfaceFileName = "IIntegrationContextAware.cs";

    /// <summary>True when <paramref name="source"/> already participates in the hand-off.</summary>
    public static bool IsAware(string source) =>
        source.Contains(InterfaceName, StringComparison.Ordinal);

    /// <summary>
    /// Rewires an action so its executor can reach the host, and rewires the integration so it has
    /// a context to hand over.
    /// </summary>
    /// <returns>
    /// The rewritten source, or null when the shape was not recognised. Returning null rather than
    /// a best effort matters: a silent partial patch produces code that compiles and then throws at
    /// run time, which is far harder to diagnose than a failed save.
    /// </returns>
    public static SourcePatch PatchAction(string source)
    {
        if (IsAware(source))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, "already wired");
        }

        var nl = Newline(source);
        var tab = source.Contains("\n\t", StringComparison.Ordinal);
        var level = tab ? "\t" : "    ";

        // 1. The action takes part in the hand-off.
        var classMatch = ClassName.Match(source);
        if (!classMatch.Success)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the file declares no class");
        }

        var className = classMatch.Groups[1].Value;
        var classIndex = classMatch.Index;
        var open = source.IndexOf('{', classMatch.Index + classMatch.Length);
        if (open < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the class declaration has no body");
        }

        // The interface goes at the end of the base list: the text between the class name and the
        // opening brace, minus the whitespace before the brace. A class with no base list has
        // nothing to extend.
        var baseListEnd = open;
        while (baseListEnd > classIndex && char.IsWhiteSpace(source[baseListEnd - 1]))
        {
            baseListEnd--;
        }

        var baseList = source[(classMatch.Index + classMatch.Length)..baseListEnd];
        if (!baseList.Contains(':'))
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the action declares no base list to extend");
        }

        source = source.Insert(baseListEnd, ", " + InterfaceName);

        // 2. The action remembers the context it is handed.
        var ctor = source.IndexOf("public " + className + "(", StringComparison.Ordinal);
        if (ctor < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the action has no public constructor");
        }

        var memberIndent = LineIndent(source, ctor);
        var ctorEnd = EndOfMember(source, ctor);
        if (ctorEnd < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the constructor has no end");
        }

        var setter = string.Concat(
            nl, nl,
            memberIndent, "public void SetIntegrationContext(IIntegrationContext context) => _integration = context;");
        source = source.Insert(ctorEnd, setter);

        // 3. The field, on its own line above the constructor that establishes the object's state.
        //    It goes at the line start, not at the member: inserting at the member would leave the
        //    line's own indentation stranded in front of the new text, which both pushes the field
        //    down a line and drops the constructor back to column zero.
        var field = string.Concat(memberIndent, "private IIntegrationContext? _integration;", nl, nl);
        source = source.Insert(LineStart(source, ctor), field);

        // 4. The executor receives it.
        var create = source.IndexOf("CreateExecutor()", StringComparison.Ordinal);
        if (create < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the action has no CreateExecutor");
        }

        var pass = source.IndexOf("new Executor(_logger)", create, StringComparison.Ordinal);
        if (pass < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "CreateExecutor does not construct new Executor(_logger)");
        }

        source = source.Remove(pass, "new Executor(_logger)".Length)
            .Insert(pass, "new Executor(_logger, _integration)");

        // 5. The executor stores it.
        var executorClass = source.IndexOf("class Executor", StringComparison.Ordinal);
        if (executorClass < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the action has no nested Executor class");
        }

        var executorOpen = source.IndexOf('{', executorClass);
        var executorCtor = source.IndexOf("public Executor(", executorClass, StringComparison.Ordinal);
        if (executorOpen < 0 || executorCtor < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the nested Executor has no body or no public constructor");
        }

        var execIndent = LineIndent(source, executorCtor);
        var execField = string.Concat(execIndent, "private readonly IIntegrationContext? _integration;", nl, nl);
        source = source.Insert(LineStart(source, executorCtor), execField);

        var execCtorMoved = executorCtor + execField.Length;
        var execCtorEnd = EndOfMember(source, execCtorMoved);
        if (execCtorEnd < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the executor constructor has no end");
        }

        var execBody = source[execCtorMoved..execCtorEnd];
        var rewritten = RewriteExecutorConstructor(execBody, nl, execIndent, level);
        if (rewritten is null)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the executor constructor could not be rewritten");
        }

        return new SourcePatch(PatchOutcome.Patched, string.Concat(source[..execCtorMoved], rewritten, source[execCtorEnd..]), "action wired to the host context");
    }

    /// <summary>
    /// Rewires the integration so it remembers the context and hands it to every action that
    /// asked for one.
    /// </summary>
    /// <returns>The rewritten source, or null when the shape was not recognised.</returns>
    public static SourcePatch PatchIntegration(string source)
    {
        if (IsAware(source))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, "hand-off already present");
        }

        var nl = Newline(source);
        var initialize = source.IndexOf("InitializeAsync(IIntegrationContext", StringComparison.Ordinal);
        if (initialize < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the integration has no InitializeAsync(IIntegrationContext)");
        }

        var open = source.IndexOf('{', initialize);
        if (open < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "the InitializeAsync signature has no body");
        }

        var indent = LineIndent(source, initialize);
        var body = indent + (source.Contains("\n\t", StringComparison.Ordinal) ? "\t" : "    ");

        // The hand-off goes first, before any the integration's own work, so that a capability
        // or action a host call depends on is already wired by the time it runs.
        //
        // Built with plain newlines and converted once at the end. Joining with the file's newline
        // and *then* replacing "\n" with it turns every already-correct "\r\n" into "\r\r\n", and a
        // lone "\r" is itself a newline to the C# lexer - so that put a blank line between every
        // line of the hand-off in every generated project, and it still compiled.
        var lines = new[]
        {
            "foreach (var action in Actions)",
            "{",
            "    if (action is " + InterfaceName + " aware)",
            "    {",
            "        aware.SetIntegrationContext(context);",
            "    }",
            "}",
        };

        var handoff = string.Join(nl, lines.Select(l => body + l));
        var insertion = string.Concat(nl, handoff, nl, body);
        return new SourcePatch(PatchOutcome.Patched, source.Insert(open + 1, insertion), "hand-off injected into InitializeAsync");
    }

    /// <summary>
    /// Turns the executor's expression-bodied constructor into one that takes and stores the
    /// context, keeping whatever it already initialised.
    /// </summary>
    private static string? RewriteExecutorConstructor(string member, string nl, string indent, string level)
    {
        var parenOpen = member.IndexOf('(', StringComparison.Ordinal);
        var parenClose = MatchingBracket(member, parenOpen);
        if (parenOpen < 0 || parenClose < 0)
        {
            return null;
        }

        var hasParameters = member[(parenOpen + 1)..parenClose].Trim().Length > 0;
        var parameters = string.Concat(
            member.AsSpan(parenOpen, parenClose - parenOpen),
            hasParameters ? ", IIntegrationContext? integration = null" : "IIntegrationContext? integration = null",
            ")");

        // An expression-bodied constructor has to become a block. The obvious one-liner,
        // `=> (_logger = logger, _integration = integration);`, is a tuple literal, and a
        // constructor's expression body has to be a statement expression - so it does not compile.
        var arrow = member.IndexOf("=>", parenClose, StringComparison.Ordinal);
        if (arrow > 0)
        {
            // The member text ends at the member's own semicolon, so the expression is everything
            // between the arrow and that trailing one. It is a single expression and is emitted as a
            // single statement: splitting it on `;` would tear a lambda with a block in half.
            var expression = member[(arrow + 2)..].TrimEnd().TrimEnd(';').Trim();
            if (expression.Length == 0)
            {
                return null;
            }

            return string.Concat(
                member[..parenOpen],
                parameters,
                nl,
                indent, "{",
                nl, indent, level, expression, ";",
                nl, indent, level, "_integration = integration;",
                nl, indent, "}");
        }

        var brace = member.IndexOf('{', parenClose);
        if (brace < 0)
        {
            return null;
        }

        return string.Concat(
            member[..parenOpen],
            parameters,
            member[(parenClose + 1)..brace],
            "{",
            nl, indent, level, "_integration = integration;",
            member[(brace + 1)..]);
    }

    /// <summary>The first type declared in a file, and the source shape DeckForge generates.</summary>
    private static readonly Regex ClassName = new(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>
    /// The leading whitespace of the line that contains <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="IntegrationPatcher.IndentOf"/> searches backwards to the previous newline and
    /// includes it, so it describes the line <em>before</em> <paramref name="index"/> whenever the
    /// index sits at a line start - and a member declaration always does. Asking it for the indent
    /// of <c>public Foo(</c> therefore returns nothing, and the members added here land at column
    /// zero. This walks forward from the true line start instead, so it answers the same question
    /// correctly either way.
    /// </remarks>
    /// <summary>The index of the first character of the line containing <paramref name="index"/>.</summary>
    private static int LineStart(string source, int index) =>
        source.LastIndexOf('\n', Math.Clamp(index, 0, source.Length - 1)) + 1;

    private static string LineIndent(string source, int index)    {
        var lineStart = source.LastIndexOf('\n', Math.Clamp(index, 0, source.Length - 1)) + 1;
        var sb = new StringBuilder();
        for (var i = lineStart; i < source.Length && source[i] is ' ' or '\t'; i++)
        {
            sb.Append(source[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The index just past the member starting at <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// This has to find the member's own terminating semicolon, not the first one after it. An
    /// expression-bodied constructor can contain a semicolon of its own - a lambda with a block, a
    /// <c>for</c>, an inline statement - and taking the first one cut the member in half and
    /// produced a file that did not parse, while reporting success. Brackets, string literals and
    /// comments are all skipped so a brace or semicolon inside any of them cannot end the scan.
    /// </remarks>
    private static int EndOfMember(string source, int from)
    {
        var parenOpen = source.IndexOf('(', from);
        if (parenOpen < 0)
        {
            return -1;
        }

        var afterParams = MatchingBracket(source, parenOpen);
        if (afterParams < 0)
        {
            return -1;
        }

        var depth = 0;
        for (var i = afterParams + 1; i < source.Length; i++)
        {
            var c = source[i];

            if (c is '"' or '\'')
            {
                i = EndOfLiteral(source, i);
                if (i < 0)
                {
                    return -1;
                }

                continue;
            }

            if (c is '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (source[i + 1] == '*')
                {
                    var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        return -1;
                    }

                    i = close + 1;
                    continue;
                }
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                if (--depth < 0)
                {
                    return -1;
                }
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    return -1;
                }

                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>The index of the closing quote of the literal starting at <paramref name="start"/>.</summary>
    private static int EndOfLiteral(string source, int start)
    {
        var quote = source[start];
        for (var i = start + 1; i < source.Length; i++)
        {
            if (source[i] == '\\')
            {
                i++;
                continue;
            }

            if (source[i] == quote)
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchingBracket(string source, int open)
    {
        if (open < 0 || open >= source.Length)
        {
            return -1;
        }

        var close = source[open] switch { '(' => ')', '{' => '}', '[' => ']', _ => '\0' };
        if (close == '\0')
        {
            return -1;
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == source[open])
            {
                depth++;
            }
            else if (source[i] == close && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Newline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
