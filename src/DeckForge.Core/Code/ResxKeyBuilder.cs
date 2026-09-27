namespace DeckForge.Core.Code;

/// <summary>
/// Builds dotted resx key paths that always compile.
/// </summary>
/// <remarks>
/// <para>
/// The Macro Deck localization source generator turns a dotted key into nested classes, then
/// emits a method for every leaf. Two rules follow, and breaking either is a compile error:
/// </para>
/// <list type="number">
/// <item>A key may not be both a leaf and the group other keys nest under
/// (<c>Setup.Title</c> and <c>Setup.Title.Hint</c>) - <c>CS0102</c> and the SDK's own
/// <c>MDLOC008</c>.</item>
/// <item>A key segment may not equal one of its own ancestors (<c>Setup.Setup.Title</c> makes a
/// member the same name as its enclosing type) - <c>CS0542</c>.</item>
/// </list>
/// <para>
/// Two of the three editors wrote their keys by string interpolation, so both rules were
/// reachable from ordinary input: naming a parameter "Name" collided with the action's own
/// Name leaf, and leaving the flow name at its default of "Setup" collided with the
/// <c>Setup</c> group itself.
/// </para>
/// </remarks>
public sealed class ResxKeyBuilder
{
    private readonly List<string> _segments = [];

    public ResxKeyBuilder(string? root = null)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            Push(root);
        }
    }

    public ResxKeyBuilder(string? root, params string?[] segments)
        : this(root)
    {
        foreach (var segment in segments)
        {
            Push(segment);
        }
    }

    public IReadOnlyList<string> Segments => _segments;

    public int Depth => _segments.Count;

    /// <summary>Appends a segment, sanitised and de-duplicated against the current path.</summary>
    public ResxKeyBuilder Push(string? segment)
    {
        var clean = CSharpCode.ResxSegment(segment);
        if (clean.Length == 0)
        {
            return this;
        }

        // Rule 2: never repeat an ancestor. The suffix keeps the key unique and readable rather
        // than silently dropping the segment, which would merge two unrelated strings.
        if (_segments.Contains(clean, StringComparer.Ordinal))
        {
            clean = clean + "_";
        }

        _segments.Add(clean);
        return this;
    }

    /// <summary>Appends a collection of named sub-groups, e.g. one per parameter.</summary>
    public ResxKeyBuilder PushEach(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            Push(name);
        }

        return this;
    }

    /// <summary>The full dotted key.</summary>
    public override string ToString() => string.Join('.', _segments);

    /// <summary>
    /// The C# expression that reads the leaf, e.g. <c>Strings.Setup.Flows.MyFlow.Steps.Connection.Title</c>.
    /// </summary>
    public string Accessor() => "Strings." + this + "()";

    /// <summary>
    /// Verifies the built key against both rules. Used by the tests, and by the generators
    /// before they write a file.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var groups = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < _segments.Count; i++)
        {
            // Any proper prefix of a key is a group.
            for (var j = 1; j <= i; j++)
            {
                groups.Add(string.Join('.', _segments.Take(j)));
            }
        }

        var full = ToString();
        if (groups.Contains(full))
        {
            problems.Add($"'{full}' is both a key and the group its own sub-keys nest under (MDLOC008).");
        }

        for (var j = 1; j < _segments.Count; j++)
        {
            var ancestor = string.Join('.', _segments.Take(j));
            if (ancestor.EndsWith("." + _segments[j], StringComparison.Ordinal))
            {
                problems.Add($"'{_segments[j]}' repeats an ancestor segment in '{full}' (CS0542).");
            }
        }

        return problems;
    }

    /// <summary>Builds a key under <paramref name="root"/>, sanitised and collision-free.</summary>
    public static string Build(string? root, params string?[] segments) =>
        new ResxKeyBuilder(root, segments).ToString();

    /// <summary>Builds a key and returns its C# accessor.</summary>
    public static string Accessor(string? root, params string?[] segments) =>
        new ResxKeyBuilder(root, segments).Accessor();
}
