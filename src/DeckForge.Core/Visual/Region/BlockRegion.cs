namespace DeckForge.Core.Visual;

/// <summary>
/// The marker-wrapped region of a generated action file: everything DeckForge owns, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Part 24.1 starts at <c>BlockCompiler.ExtractRegion</c>, which lives in CodeGen because the <em>writer</em>
/// is there. The <em>reader</em> has to be in Core, and Core is below CodeGen — so this is the reader's own
/// copy of the two marker strings. That is duplication, and it is the kind that rots: change one and the
/// round-trip silently reads nothing, reports nothing and hands back an empty document, which looks exactly
/// like a region that was never written.
/// </para>
/// <para>
/// So the duplication is pinned by a test instead of by discipline: <c>BlockRegionReaderTests</c> asserts
/// these two constants equal <c>BlockCompiler.BeginMarker</c> and <c>BlockCompiler.EndMarker</c>, which
/// only compiles because the test project references both assemblies. A second copy with a test on it is
/// not the same risk as a second copy without one.
/// </para>
/// </remarks>
public static class BlockRegion
{
    /// <summary>The line that opens the region.</summary>
    public const string BeginMarker = "// <macrodeck-blocks>";

    /// <summary>The line that closes it.</summary>
    public const string EndMarker = "// </macrodeck-blocks>";

    /// <summary>The <c>vis-</c> code a region that cannot be identified is refused with.</summary>
    /// <remarks>
    /// <c>vis-region-conflict</c>, and the reuse is deliberate rather than a shortage. Appendix F gives it
    /// the trigger "more than one marker region, or a region owned by another document", and that is
    /// precisely the question the reader asks: <em>which</em> region in this file is one DeckForge may read?
    /// Zero is the degenerate case of the same failure — the writer and the reader are two halves of one
    /// contract about marker pairs, so "I cannot tell you which region is mine" is one answer in one code
    /// whichever way the counting went wrong. Appendix F.1 already notes that this code is reported as a
    /// sentence rather than as a diagnostic, because a refused save has nowhere to put a
    /// <see cref="VisualDiagnostic"/>; a refused <em>read</em> has exactly the same problem and the same
    /// answer, which is a code on a result rather than a code on a diagnostic.
    /// </remarks>
    public const string RegionConflictCode = "vis-region-conflict";

    /// <summary>
    /// The text between the markers, or a refusal naming why there is none.
    /// </summary>
    /// <param name="csharpSource">A generated action file, or just its region.</param>
    /// <remarks>
    /// <para>
    /// Zero markers, two markers, or an end before a begin are all the same refusal, and all three are
    /// refusals rather than empty successes: an empty region and a file with no region produce different
    /// documents and a caller that cannot tell them apart will save one over the other.
    /// </para>
    /// <para>
    /// The middle of the file only — the markers, not everything around them. Hand-written code outside the
    /// markers is the user's, which is the same rule the writer splices by, and it is why a region can be
    /// read at all: nothing here has to understand a method.
    /// </para>
    /// </remarks>
    public static RegionExtraction Extract(string? csharpSource)
    {
        if (string.IsNullOrWhiteSpace(csharpSource))
        {
            return RegionExtraction.Missing("There is no code to read: the file was empty.");
        }

        var begins = AllIndexesOf(csharpSource, BeginMarker);
        var ends = AllIndexesOf(csharpSource, EndMarker);

        if (begins.Count == 0 && ends.Count == 0)
        {
            return RegionExtraction.Missing(
                $"No block region was found. DeckForge writes one between {BeginMarker} and "
                + $"{EndMarker}, and this file has neither, so there is nothing to open as blocks.");
        }

        if (begins.Count != 1 || ends.Count != 1 || ends[0] < begins[0])
        {
            return RegionExtraction.Missing(
                $"This file has {begins.Count} '{BeginMarker}' and {ends.Count} '{EndMarker}' lines, "
                + "so which region is DeckForge's cannot be told. Nothing was changed.");
        }

        var body = csharpSource[(begins[0] + BeginMarker.Length)..ends[0]];

        return RegionExtraction.Read(body.Trim());
    }

    private static List<int> AllIndexesOf(string source, string marker)
    {
        var found = new List<int>();
        var index = source.IndexOf(marker, StringComparison.Ordinal);

        while (index >= 0)
        {
            found.Add(index);
            index = source.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return found;
    }
}

/// <summary>
/// What <see cref="BlockRegion.Extract"/> produced: the region's text, or why there is none.
/// </summary>
/// <param name="Text">The region's text, or null when it could not be identified.</param>
/// <param name="Problem">A sentence naming what was wrong, or null.</param>
/// <param name="Code">
/// The <c>vis-</c> code for the refusal, or null. The same namespace as the validator's and
/// <see cref="DropPlan"/>'s, for the reason <see cref="DropPlan"/> gives.
/// </param>
/// <remarks>
/// A record rather than a string-or-null because the three refusals a caller can act on — no markers, two
/// regions, an inverted pair — have nothing in common but the fact that they are not a region, and the
/// message is the only place that can say which one happened.
/// </remarks>
public sealed record RegionExtraction(string? Text, string? Problem, string? Code)
{
    /// <summary>Whether a region was found.</summary>
    public bool Found => Text is not null;

    /// <summary>A region, trimmed of the whitespace around the markers.</summary>
    public static RegionExtraction Read(string text) => new(text, null, null);

    /// <summary>No region, and why.</summary>
    public static RegionExtraction Missing(string problem) =>
        new(null, problem, BlockRegion.RegionConflictCode);
}

/// <summary>
/// A run of lines the subset parser did not recognise, kept exactly as they were written.
/// </summary>
/// <param name="Line">The region's one-based line the run starts on.</param>
/// <param name="Lines">The lines themselves, verbatim and in order.</param>
/// <remarks>
/// <para>
/// Part 24.1.4's rule is "anything it cannot recognise is preserved, never lost", and this type is where
/// "preserved" is kept. It is also anchored by line, because a preserved run is only useful if the reader
/// can say <em>where</em> in the region it came from: a caller that wants the document and the preserved
/// text needs to put the two back in the same order they were in, and the lines themselves carry no
/// position of their own.
/// </para>
/// <para>
/// The same lines also appear in the document as an opaque block, because a document is the thing that
/// gets saved and a list on a result is not. See <see cref="BlockRegionReader"/> for why that block is
/// <c>legacy.unsupported</c>.
/// </para>
/// </remarks>
public sealed record OpaqueRegion(int Line, IReadOnlyList<string> Lines)
{
    /// <summary>The lines as one string, which is how they are carried in the document.</summary>
    public string Text => string.Join("\n", Lines);

    /// <summary>How many lines went unrecognised, for the diagnostic and the message.</summary>
    public int Count => Lines.Count;
}