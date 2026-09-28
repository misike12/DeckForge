using System.Text;
using DeckForge.Core.Code;

namespace DeckForge.CodeGen.Generation;

/// <summary>One generated file, plus the resx entries it references.</summary>
public sealed record GeneratedFile(string Path, string Content, IReadOnlyDictionary<string, string> ResxEntries)
{
    public static GeneratedFile Code(string path, string content) => new(path, content, new Dictionary<string, string>());

    public static GeneratedFile WithStrings(string path, string content, IReadOnlyDictionary<string, string> entries) =>
        new(path, content, entries);
}

/// <summary>The outcome of patching an existing file.</summary>
public enum PatchOutcome
{
    /// <summary>The file was rewritten.</summary>
    Patched,

    /// <summary>The file already contained the change, so nothing was written.</summary>
    AlreadyPresent,

    /// <summary>The expected shape was not found, so nothing was written.</summary>
    AnchorMissing,
}

/// <summary>One edit applied to an existing source file.</summary>
public sealed record SourcePatch(PatchOutcome Outcome, string Content, string Message)
{
    public bool Success => Outcome is not PatchOutcome.AnchorMissing;
}

/// <summary>
/// Idempotent source edits for a generated plugin's <c>PluginIntegration.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Four editors each grew their own private copy of this logic, with their own anchor strings
/// and their own idea of what to do when the anchor was missing. Two of them inserted a
/// <c>using</c> before the first literal occurrence of the text "using " - which, in a file whose
/// XML doc comment happens to mention it, means inserting a using directive into a comment.
/// Another appended <c>new Foo(logger)</c> to a collection by calling <c>IndexOf(']', -1)</c>,
/// which throws.
/// </para>
/// <para>
/// Every operation here is idempotent, reports why it did or did not apply, and never throws on
/// a shape it did not recognise. A caller that ignores <see cref="PatchOutcome.AnchorMissing"/>
/// can tell the user which file needs a manual edit instead of claiming success.
/// </para>
/// </remarks>
public static class IntegrationPatcher
{
    /// <summary>The class declaration the stock template emits.</summary>
    public const string IntegrationClassAnchor = "public sealed class PluginIntegration : IPluginIntegration";

    /// <summary>The stock template's Actions assignment.</summary>
    public const string ActionsAnchor = "Actions = [new LogMessageAction(logger)];";

    /// <summary>The stock template's Actions property.</summary>
    public const string ActionsPropertyAnchor = "public IReadOnlyList<IActionDefinition> Actions { get; }";

    /// <summary>Adds a <c>using</c> directive if the namespace is not already imported.</summary>
    public static SourcePatch AddUsing(string source, string @namespace)
    {
        if (source.Contains($"using {@namespace};", StringComparison.Ordinal))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, $"using {@namespace}; is already present.");
        }

        // Anchor on the first using *directive*: a line that starts with optional whitespace and
        // then "using " and ends with ";". The previous IndexOf("using ") matched inside comments.
        var directive = FirstUsingDirective(source);
        if (directive < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source,
                "No using directive found; add `using " + @namespace + ";` by hand.");
        }

        // FirstUsingDirective already returns a line start, but keep the derivation local so a
        // future change to that helper cannot silently turn an insert into a replace. Splicing
        // from lineStart to lineEnd would overwrite the first directive, and every added
        // capability would delete an import the previous one needed.
        var lineStart = directive;
        var insertion = $"using {@namespace};" + Environment.NewLine;
        return new SourcePatch(PatchOutcome.Patched,
            string.Concat(source.AsSpan(0, lineStart), insertion, source.AsSpan(lineStart)),
            $"Added using {@namespace};.");
    }

    private static int FirstUsingDirective(string source)
    {
        var offset = 0;
        while (offset < source.Length)
        {
            var lineEnd = source.IndexOf('\n', offset);
            if (lineEnd < 0)
            {
                lineEnd = source.Length;
            }

            // Trim both ends, not just the start. A CRLF file leaves a \r at the end of every
            // line, and a test for EndsWith(";") then fails on every directive in the file - which
            // is why the first capability could be added and the second reported "add this by hand".
            var line = source.AsSpan(offset, lineEnd - offset).Trim();
            if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(";", StringComparison.Ordinal))
            {
                return offset;
            }

            offset = lineEnd + 1;
        }

        return -1;
    }

    /// <summary>Adds an interface to the integration's base list.</summary>
    /// <remarks>
    /// The base list is parsed rather than matched as a fixed string. Matching
    /// <c>"public sealed class PluginIntegration : IPluginIntegration"</c> works exactly once: the
    /// moment a second interface is added the anchor no longer appears, so every subsequent
    /// capability reported "add this by hand" against a class it had just edited itself.
    /// </remarks>
    public static SourcePatch AddInterface(string source, string interfaceName)
    {
        var classIndex = source.IndexOf("public sealed class PluginIntegration", StringComparison.Ordinal);
        if (classIndex < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source,
                "Could not find the integration class declaration; add " + interfaceName + " to it by hand.");
        }

        var body = source.IndexOf('{', classIndex);
        if (body < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "The integration class has no body.");
        }

        // The declaration runs from the class keyword to the opening brace of the body.
        var colon = source.IndexOf(':', classIndex);
        if (colon < 0 || colon > body)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source,
                "The integration class declares no base list; add " + interfaceName + " by hand.");
        }

        var raw = source[colon..body];
        var list = raw.TrimEnd();
        if (list.Contains(interfaceName, StringComparison.Ordinal))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, $"Already implements {interfaceName}.");
        }

        // Insert at the end of the trimmed list, not at the brace. The stock template puts the
        // opening brace on the next line, so appending at `body` lands the interface after the
        // newline and produces `IPluginIntegration\n , IVariableProvider{` - which is valid C#
        // but reads as though a generator mangled the file.
        var insertAt = colon + list.Length;
        var separator = raw.Length == list.Length ? " " : " ";
        var updated = string.Concat(
            source.AsSpan(0, insertAt),
            separator + ", " + interfaceName,
            source.AsSpan(insertAt));
        return new SourcePatch(PatchOutcome.Patched, updated, $"Integration now implements {interfaceName}.");
    }

    /// <summary>The interfaces the integration class currently declares, base list only.</summary>
    public static IReadOnlyList<string> DeclaredInterfaces(string source)
    {
        var classIndex = source.IndexOf("public sealed class PluginIntegration", StringComparison.Ordinal);
        if (classIndex < 0)
        {
            return [];
        }

        var body = source.IndexOf('{', classIndex);
        var colon = source.IndexOf(':', classIndex);
        if (body < 0 || colon < 0 || colon > body)
        {
            return [];
        }

        return
        [
            .. source[colon..body]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0),
        ];
    }

    /// <summary>Appends an action to the integration's Actions collection.</summary>
    public static SourcePatch AddAction(string source, string className)
    {
        if (source.Contains($"new {className}(", StringComparison.Ordinal))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, $"{className} is already registered.");
        }

        if (source.Contains(ActionsAnchor, StringComparison.Ordinal))
        {
            var updated = source.Replace(
                ActionsAnchor,
                $"Actions = [new LogMessageAction(logger), new {className}(logger)];",
                StringComparison.Ordinal);
            return new SourcePatch(PatchOutcome.Patched, updated, $"Registered {className}.");
        }

        var open = source.IndexOf("Actions = [", StringComparison.Ordinal);
        if (open < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source,
                "Could not find the Actions collection; add `new " + className + "(logger)` to it by hand.");
        }

        var close = MatchingBracket(source, source.IndexOf('[', open));
        if (close < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source,
                "The Actions collection is not closed; fix it by hand and register " + className + ".");
        }

        var before = source[..close].TrimEnd();
        var needsComma = !before.EndsWith('[');
        var updated2 = source.Insert(close, $"{(needsComma ? ", " : string.Empty)}new {className}(logger)");
        return new SourcePatch(PatchOutcome.Patched, updated2, $"Registered {className}.");
    }

    /// <summary>
    /// Finds the index of the bracket that closes the one at <paramref name="open"/>, skipping
    /// string and character literals so a <c>"]"</c> inside a default value cannot end the
    /// scan early.
    /// </summary>
    public static int MatchingBracket(string source, int open)
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
                case '[':
                case '{':
                case '(':
                    depth++;
                    continue;
                case ']':
                case '}':
                case ')':
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

    /// <summary>Inserts a member into the integration class body, just before the closing brace.</summary>
    public static SourcePatch AddMember(string source, string memberCode)
    {
        if (source.Contains(memberCode.Trim(), StringComparison.Ordinal))
        {
            return new SourcePatch(PatchOutcome.AlreadyPresent, source, "Member already present.");
        }

        var classIndex = source.IndexOf("public sealed class PluginIntegration", StringComparison.Ordinal);
        if (classIndex < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "Could not find the integration class.");
        }

        var open = source.IndexOf('{', classIndex);
        if (open < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "The integration class has no body.");
        }

        var close = MatchingBracket(source, open);
        if (close < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "The integration class body is not closed.");
        }

        var before = source[..close].TrimEnd();
        var insertion = Environment.NewLine + Environment.NewLine + CSharpCode.Indent(memberCode, MemberIndentOf(source, classIndex));
        var updated = before + insertion + Environment.NewLine + source[close..];
        return new SourcePatch(PatchOutcome.Patched, updated, "Member added to the integration.");
    }

    /// <summary>
    /// The indentation a member of this class body should have: the class declaration's own
    /// indentation plus one level, in whichever style the file already uses.
    /// </summary>
    /// <remarks>
    /// The obvious implementation asks for the indentation of the opening brace, which is the
    /// class declaration's column - so members were inserted at column zero, at the same level as
    /// the class itself. And the level has to be a tab, not four spaces: the official template is
    /// tab-indented, so adding members with spaces left a plugin with two indentation styles in one
    /// class.
    /// </remarks>
    private static string MemberIndentOf(string source, int classIndex)
    {
        var declarationIndent = IndentOf(source, classIndex);

        // One level in whichever style the file already indents with. A file that uses spaces
        // gets a space level, so a hand-written project keeps its own style.
        var level = source.Contains("\n\t", StringComparison.Ordinal) ? "\t" : "    ";
        return declarationIndent + level;
    }

    /// <summary>Replaces or adds the body of the integration's InitializeAsync.</summary>
    public static SourcePatch AddInitializeBody(string source, IEnumerable<string> statements)
    {
        var body = string.Join(Environment.NewLine, statements.Select(s => CSharpCode.Indent(s, 8)));
        var existing = ExtractMethodBody(source, "InitializeAsync");
        if (existing is null)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "Could not find InitializeAsync.");
        }

        var open = source.IndexOf('{', existing.Value);
        var close = MatchingBracket(source, open);
        if (open < 0 || close < 0)
        {
            return new SourcePatch(PatchOutcome.AnchorMissing, source, "InitializeAsync has no body.");
        }

        var updated = source[..(open + 1)]
            + Environment.NewLine
            + body
            + Environment.NewLine + IndentOf(source, open)
            + source[close..];
        return new SourcePatch(PatchOutcome.Patched, updated, "InitializeAsync body replaced.");
    }

    /// <summary>The index of a method name in the source, or null.</summary>
    private static int? ExtractMethodBody(string source, string methodName)
    {
        var index = source.IndexOf(methodName, StringComparison.Ordinal);
        return index < 0 ? null : index;
    }

    /// <summary>The leading whitespace of the line containing <paramref name="index"/>.</summary>
    public static string IndentOf(string source, int index)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        var sb = new StringBuilder();
        for (var i = lineStart; i < source.Length && i < index; i++)
        {
            if (source[i] is ' ' or '\t')
            {
                sb.Append(source[i]);
            }
            else
            {
                break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Applies a sequence of patches, stopping at the first that cannot apply.</summary>
    public static SourcePatch ApplyAll(string source, params Func<string, SourcePatch>[] patches)
    {
        var current = source;
        var messages = new List<string>();
        foreach (var patch in patches)
        {
            var result = patch(current);
            if (!result.Success)
            {
                messages.Add(result.Message);
                return new SourcePatch(PatchOutcome.AnchorMissing, current, string.Join(" ", messages));
            }

            messages.Add(result.Message);
            current = result.Content;
        }

        return new SourcePatch(
            messages.Any(m => m.Contains("by hand", StringComparison.Ordinal)) ? PatchOutcome.AnchorMissing : PatchOutcome.Patched,
            current,
            string.Join(" ", messages));
    }
}
