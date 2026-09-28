using System.Text.RegularExpressions;
using DeckForge.Core.Blocks;

namespace DeckForge.CodeGen.Generation;

/// <summary>What happened when a canvas was written into its action.</summary>
/// <param name="Success">True when the action source was rewritten.</param>
/// <param name="Content">The rewritten source, or the original when <paramref name="Success"/> is false.</param>
/// <param name="Message">A description a user can act on.</param>
public sealed record BlockWriteResult(bool Success, string Content, string Message)
{
    public static BlockWriteResult Ok(string content) => new(true, content, "Blocks written.");

    public static BlockWriteResult Failed(string original, string message) => new(false, original, message);
}

/// <summary>
/// Writes a compiled block program into the action it targets.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the save lived in the WPF view model, where nothing could test it, and it
/// was wrong in three ways at once: it spliced the region in with its own hand-written string
/// surgery at a hardcoded eight-space indent, it never made the executor <c>async</c>, and it never
/// gave the action a host context. So the two things a block program is most used for - waiting, and
/// calling the host - produced code that did not compile, and the view model reported success.
/// </para>
/// <para>
/// Three steps, all of which something else already knew how to do:
/// <list type="number">
/// <item><see cref="ActionContextPatcher"/> - so <c>_integration</c> exists.</item>
/// <item><see cref="BlockCompiler.Splice(string, string, string, int)"/> - which matches the
/// file's own indentation, and replaces an existing region rather than adding a second one.</item>
/// <item><see cref="BlockCompiler.EnsureAsyncExecutor"/> - so a <c>await</c> has a method that can
/// hold it.</item>
/// </list>
/// </para>
/// </remarks>
public static class BlockProgramWriter
{
    /// <summary>The signature the region is spliced into when the action has no region yet.</summary>
    public const string ExecutorAnchor = "public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)";

    /// <summary>
    /// Produces the action source that runs <paramref name="program"/>.
    /// </summary>
    /// <param name="actionSource">The action file's current contents.</param>
    /// <param name="program">The canvas to write.</param>
    /// <returns>
    /// The rewritten source, or a failure naming the step that could not be applied. Every step is
    /// fallible - a hand-edited action may have lost its constructor, or its base list - and
    /// returning a partial rewrite would produce code that compiles and then throws.
    /// </returns>
    public static BlockWriteResult Write(string actionSource, BlockProgram program)
    {
        if (program.Statements.Count == 0)
        {
            return BlockWriteResult.Failed(actionSource, "There are no blocks to write.");
        }

        // 1. Host context. Without it, every block that calls the host names a field nobody
        //    declared, and the compiler says so eight times over.
        var wired = ActionContextPatcher.PatchAction(actionSource);
        if (!wired.Success)
        {
            return BlockWriteResult.Failed(actionSource, $"Could not give the action a host context: {wired.Message}");
        }

        // 2. The region itself. First check the names against what the method already declares:
        //    the region goes inside a method the user wrote, so a canvas variable can collide with
        //    a local already there.
        var conflicts = BlockCompiler.LocalNameConflicts(program, DeclaredNamesIn(wired.Content));
        if (conflicts.Count > 0)
        {
            return BlockWriteResult.Failed(
                actionSource,
                $"These variable names are already used in ExecuteAsync: {string.Join(", ", conflicts)}. "
                + "Rename them on the canvas - they cannot be renamed automatically, because the "
                + "blocks that read them refer to them by name.");
        }

        var compiled = BlockCompiler.Compile(program);
        var spliced = BlockCompiler.Splice(wired.Content, compiled, ExecutorAnchor);
        if (spliced is null)
        {
            return BlockWriteResult.Failed(
                actionSource,
                $"Could not find `{ExecutorAnchor}` to write the blocks into.");
        }

        // 3. async, because a delay, an HTTP call or a navigation step puts an await in the body.
        return BlockWriteResult.Ok(BlockCompiler.EnsureAsyncExecutor(spliced));
    }

    /// <summary>
    /// The identifiers the region will share a scope with: the executor's parameters and locals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scoped to the executor's body, deliberately. Reading the whole file made the check refuse
    /// legal saves - a canvas variable named <c>Id</c> collided with the class's <c>Id</c> property,
    /// which a local is free to shadow - and inconsistently, since a member whose type was not in
    /// the keyword list was not caught at all.
    /// </para>
    /// <para>
    /// Generous inside the method, because there the cost is asymmetric: a false positive costs the
    /// user a rename, a false negative costs them a CS0128 in their plugin.
    /// </para>
    /// </remarks>
    public static IReadOnlyCollection<string> DeclaredNamesIn(string actionSource)
    {
        var method = ExecutorMethod(actionSource);
        if (method is null)
        {
            return [];
        }

        var parameterList = ExecutorParameterList(actionSource);
        var names = new HashSet<string>(StringComparer.Ordinal);

        // `var message`, `string? message`, `double? count` and so on, including a nullable
        // annotation, which the first version's keyword list missed.
        foreach (Match match in Regex.Matches(
            method,
            @"\b(?:var|string|int|long|bool|double|float|decimal|object)\s*\??\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled))
        {
            names.Add(match.Groups[1].Value);
        }

        // The method's own parameters - the one list whose names are genuinely in scope here. A
        // call's arguments are not: `Helper(int a, int b)` puts `a` and `b` in the callee's scope,
        // not the caller's, and reserving them refused saves the user could have made. The earlier
        // pattern also only ever captured the first entry of any list.
        foreach (Match match in Regex.Matches(
            parameterList,
            @"(?:^|[(,;])\s*(?:ref |out |in |params )*[A-Za-z_][A-Za-z0-9_.<>\[\]?]*\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    /// <summary>
    /// The executor's own parameter list, or empty when the signature could not be read.
    /// </summary>
    private static string ExecutorParameterList(string actionSource)
    {
        var start = actionSource.IndexOf(ExecutorAnchor, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        var open = actionSource.IndexOf('(', start);
        if (open < 0)
        {
            return "";
        }

        var depth = 0;
        for (var i = open; i < actionSource.Length; i++)
        {
            if (actionSource[i] == '(')
            {
                depth++;
            }
            else if (actionSource[i] == ')' && --depth == 0)
            {
                return actionSource[(open + 1)..i];
            }
        }

        return "";
    }

    /// <summary>
    /// The executor method, signature and body, or null when it could not be located.
    /// </summary>
    /// <remarks>
    /// The signature is included because the region's locals live in the method's scope, where its
    /// parameters are visible too. A region that declared one of those names would be a CS0128.
    /// </remarks>
    private static string? ExecutorMethod(string actionSource)
    {
        var start = actionSource.IndexOf(ExecutorAnchor, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var open = actionSource.IndexOf('{', start);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < actionSource.Length; i++)
        {
            if (actionSource[i] == '{')
            {
                depth++;
            }
            else if (actionSource[i] == '}' && --depth == 0)
            {
                return actionSource[start..i];
            }
        }

        return null;
    }
}
