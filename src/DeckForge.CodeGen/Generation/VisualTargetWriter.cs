using DeckForge.Core.Code;
using DeckForge.Core.Visual;

namespace DeckForge.CodeGen.Generation;

/// <summary>
/// Writes a visual target into the file that owns it, wherever that file is.
/// </summary>
/// <remarks>
/// <para>
/// Part 8.5's table lists three targets that did not exist at the time: the widget node's handler, the
/// setup flow's step hooks and the integration's lifecycle. They are one class here rather than three
/// because they differ in exactly one thing — the anchor they are written into — and the part that is
/// easy to get wrong, finding the anchor and replacing it on the second save, is the part
/// <see cref="BlockCompiler.Splice"/> already got right for the action executor.
/// </para>
/// <para>
/// Re-implementing that would have been the mistake the table warns about: the last editor to grow its
/// own copy of anchoring produced code that was wrong on the *second* save and right on the first, which
/// is the hardest kind of bug to notice and the easiest to ship. So each target resolves an anchor string
/// and hands the rest to the same splice.
/// </para>
/// </remarks>
public static class VisualTargetWriter
{
    /// <summary>
    /// The anchor for a target, or null when the target's kind and id do not name one we know.
    /// </summary>
    /// <remarks>
    /// Returning null rather than a best guess is the point. An anchor that is nearly right writes the
    /// region into the wrong method, and the result still compiles — which is worse than a refusal,
    /// because the user's blocks run in someone else's lifecycle.
    /// </remarks>
    public static string? AnchorFor(VisualTarget target) => (target.Kind, target.AnchorId) switch
    {
        (TargetKind.Action, _) => VisualProgramWriter.ExecutorAnchor,

        (TargetKind.Lifecycle, "InitializeAsync") => "public Task InitializeAsync(IIntegrationContext context)",
        (TargetKind.Lifecycle, "ShutdownAsync") => "public Task ShutdownAsync()",

        (TargetKind.ConfigFlowHook, "StartAsync") => "public Task<ConfigFlowResult> StartAsync(",
        (TargetKind.ConfigFlowHook, "SubmitAsync") => "public Task<ConfigFlowResult> SubmitAsync(",

        (TargetKind.WidgetHandler, var eventName) when !string.IsNullOrWhiteSpace(eventName) =>
            WidgetHandlerAnchor(eventName),

        _ => null,
    };

    /// <summary>
    /// The anchor for one of a widget node's designed events.
    /// </summary>
    /// <param name="eventName">The event's name, as the designer recorded it.</param>
    /// <remarks>
    /// The anchor stops before the <c>{</c> because <see cref="BlockCompiler.Splice"/> looks for the first
    /// brace after the anchor and this lambda's is the one it wants. Matching the brace here instead
    /// would find the brace that opens the *next* node's handler, because the whole provider is one
    /// collection initialiser full of lambdas.
    /// </remarks>
    public static string WidgetHandlerAnchor(string eventName) =>
        $"UiEventHandler.On({CSharpCode.StringLiteral(eventName)}";

    /// <summary>
    /// Writes a target's scripts into the file that owns it.
    /// </summary>
    /// <param name="source">The target file's current contents.</param>
    /// <param name="project">The owning document, for procedure lookup.</param>
    /// <param name="target">The target to write.</param>
    /// <param name="integrationAvailable">Whether the plugin declares host integration.</param>
    /// <returns>
    /// The rewritten source, or a refusal naming what is missing. The contract is
    /// <see cref="VisualWriteResult"/>'s, the same one the action writer returns, so a caller that saves
    /// a document does not have to know which kind of target it is saving.
    /// </returns>
    public static VisualWriteResult Write(
        string source,
        VisualProject project,
        VisualTarget target,
        bool integrationAvailable = true)
    {
        if (AnchorFor(target) is not { } anchor)
        {
            return VisualWriteResult.Failed(
                source,
                $"A {target.Kind} target named '{target.AnchorId}' has nowhere to be written. "
                + "Its anchor id has to name a method the generated file actually has.");
        }

        var (code, problems) = VisualEmitter.CompileTarget(target, project);

        if (code.Trim().Length == 0)
        {
            return VisualWriteResult.Failed(source, "There are no blocks to write.");
        }

        if (UnreachableHere(target) is { } unreachable)
        {
            return VisualWriteResult.Failed(source, unreachable);
        }

        var hostGuarded = code.Contains("_integration", StringComparison.Ordinal);
        if (hostGuarded && !integrationAvailable)
        {
            return VisualWriteResult.Failed(
                source,
                "This plugin has no host integration, so blocks that call the host cannot be written. "
                + "Add the integration capability on the Capabilities page and regenerate.");
        }

        var compiled =
            "// <macrodeck-blocks>\n"
            + "// Generated by DeckForge's Visual editor. Edit on the canvas; anything outside\n"
            + "// these markers is yours and is preserved across regeneration.\n"
            + code.TrimEnd()
            + "\n// </macrodeck-blocks>\n";

        var spliced = BlockCompiler.Splice(source, compiled, anchor);
        if (spliced is null)
        {
            return VisualWriteResult.Failed(
                source,
                $"Could not find `{anchor}` to write the blocks into. The file may have been edited "
                + "outside DeckForge, or the capability that generates it may not be present.");
        }

        // Deliberately not EnsureAsyncExecutor: that repairs the stock action template's synchronous
        // `return` statements, which is a fact about the action scaffold and nothing to do with a widget
        // lambda or a lifecycle hook. Running it here would rewrite methods the canvas never touched.
        var message = problems.Count == 0
            ? $"Visual blocks written into {target.Name}."
            : $"Visual blocks written into {target.Name} with {problems.Count} problem(s) to resolve "
              + "on the canvas.";

        return VisualWriteResult.Ok(spliced, message);
    }

    /// <summary>
    /// The runtime support file these blocks need, for a plugin's root namespace.
    /// </summary>
    /// <remarks>
    /// Every kind needs it, and it was missing: the template's <c>__NAMESPACE__</c> placeholder had no
    /// substitution anywhere in the project, so a region using <c>VisualRuntime</c> could only ever have
    /// compiled in a plugin somebody had hand-fixed. A writer that can emit the blocks owes the caller the
    /// file they reference, so it renders it here rather than leaving it to be remembered.
    /// </remarks>
    public static (string FileName, string Content) RuntimeFile(string rootNamespace) =>
        (VisualRuntimeTemplate.FileName, VisualRuntimeTemplate.Render(rootNamespace));

    /// <summary>
    /// Every anchor id a kind accepts, for a caller that has to offer a choice and would otherwise offer
    /// whatever the user typed.
    /// </summary>
    public static IReadOnlyList<string> AnchorIdsFor(TargetKind kind) => kind switch
    {
        TargetKind.Action => ["ExecuteAsync"],
        TargetKind.Lifecycle => ["InitializeAsync", "ShutdownAsync"],
        TargetKind.ConfigFlowHook => ["StartAsync", "SubmitAsync"],
        TargetKind.WidgetHandler => [],
        _ => [],
    };

    /// <summary>
    /// The names a target's method does not have, which an action executor's always does.
    /// </summary>
    /// <remarks>
    /// The action executor is handed an <c>ActionExecutionContext</c> and owns an <c>ILogger</c>. A widget
    /// node's event handler is a bare lambda with no parameter, and the SDK's UI event surface supplies
    /// none: P0 did not establish an anchor for one, and inventing a context parameter would be inventing
    /// API. So a block whose expression mentions <c>context</c> or <c>_logger</c> cannot go into a widget
    /// handler at all, and the writer says which blocks rather than emitting a file that does not compile
    /// — which is how this rule was found in the first place.
    /// </remarks>
    private static IReadOnlyList<string> UnreachableNames(TargetKind kind) => kind switch
    {
        TargetKind.Action => [],
        TargetKind.Lifecycle => [],
        TargetKind.ConfigFlowHook => ["_logger", "context"],
        _ => ["_logger", "_integration", "context"],
    };

    /// <summary>
    /// A refusal naming the blocks that cannot be written where this target goes, or null when they all
    /// can be.
    /// </summary>
    /// <remarks>
    /// Asked per block rather than by scanning the emitted text, so the message can name the blocks the
    /// user has to move. "Something in this script needs a context" is a puzzle; <c>sensing.tap-velocity</c>
    /// does is a sentence they can act on before they press save.
    /// </remarks>
    private static string? UnreachableHere(VisualTarget target)
    {
        if (UnreachableNames(target.Kind) is not { Count: > 0 } unavailable)
        {
            return null;
        }

        var offenders = target.Scripts
            .SelectMany(script => script.Blocks())
            .Where(block => Needs(block, unavailable))
            .Select(block => block.Kind)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (offenders.Count == 0)
        {
            return null;
        }

        var where = target.Kind == TargetKind.WidgetHandler
            ? "a widget node's event handler, which the SDK calls with no context and no logger"
            : "a setup flow's hooks, which are handed a config context rather than an action one";

        return $"These blocks cannot be written into {where}: {string.Join(", ", offenders)}. "
               + "Move them to an action target, or call one - an action is where the host surfaces are.";
    }

    /// <summary>Whether this block's own emission mentions one of the names the target cannot supply.</summary>
    private static bool Needs(Block block, IReadOnlyList<string> unavailable)
    {
        var row = BlockCatalog.Find(block.Kind)?.Sdk?.Expression;

        if (string.IsNullOrEmpty(row))
        {
            // A container's structure is emitted in code rather than from the row, so a loop that waits
            // still reaches for the action's cancellation token and has to be refused as well.
            row = block.Kind switch
            {
                "control.wait-seconds" or "control.wait-ms" or "control.forever" or "control.repeat"
                    or "control.repeat-until" or "control.while"
                    => "context.CancellationToken",
                _ => null,
            };
        }

        return row is not null && unavailable.Any(name => row.Contains(name, StringComparison.Ordinal));
    }
}