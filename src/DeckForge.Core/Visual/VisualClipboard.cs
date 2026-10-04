using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Visual;

/// <summary>
/// The clipboard envelope: a versioned text payload holding one run of blocks.
/// </summary>
/// <remarks>
/// <para>
/// Part 25.2 asked for "a <c>VisualClipboard</c> envelope with a format version and the same subset as
/// <c>script.json</c>", and §28.5 recorded that nothing implemented it. This is that, in Core, because
/// everything interesting about it is a question about the document rather than about the clipboard: what
/// a refusal says, and whether a payload from a future version is refused rather than half-understood.
/// </para>
/// <para>
/// The payload is <em>text</em>, not a private clipboard format. Two consequences, both deliberate. A
/// user can paste a run into a text editor and read it, which is the only way to get a stack out of a
/// document the editor cannot open. And a payload from a newer build has to be recognisably newer rather
/// than silently mangled, which is what the version field buys.
/// </para>
/// <para>
/// The same text is the <c>.dfblock</c> file, so export and import are the clipboard's own two functions
/// with a file in between rather than a second serialisation format that could disagree with the first.
/// </para>
/// </remarks>
public static class VisualClipboard
{
    /// <summary>The value of the <c>format</c> field, so a paste can tell ours from anything else.</summary>
    public const string Format = "deckforge-block";

    /// <summary>The version this build writes, and the newest it reads.</summary>
    public const int Version = 1;

    /// <summary>The extension a single run is exported as.</summary>
    public const string Extension = ".dfblock";

    /// <summary>The field a run lives in, named once so the cheap check and the reader cannot drift.</summary>
    private const string RunField = "\"run\"";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // camelCase, because this text is a documented file format as well as a clipboard payload: it has
        // to read the same in a text editor, in a diff, and in anything somebody writes to read one. It
        // also keeps the payload consistent with canvas.json, which is camelCase for the same reason.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Writes one run as a clipboard payload.</summary>
    /// <param name="run">The blocks, in order. An empty run still writes a well-formed envelope.</param>
    public static string Write(IReadOnlyList<Block> run) => JsonSerializer.Serialize(
        new Envelope
        {
            Format = Format,
            Version = Version,
            Kind = run.FirstOrDefault()?.Kind,
            Run = [.. run],
        },
        Options);

    /// <summary>Reads a clipboard payload, or explains why it will not.</summary>
    /// <param name="text">Whatever was on the clipboard.</param>
    /// <remarks>
    /// Every refusal carries a <c>vis-</c> code in one namespace with the validator's, for the reason
    /// <see cref="DropPlan"/> gives: two vocabularies of codes is one too many for a caller to handle,
    /// and a wrong code here was invisible.
    /// </remarks>
    public static ClipboardReadResult Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ClipboardReadResult.Refused("There is nothing on the clipboard to paste.", "clipboard-empty");
        }

        Envelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(text, Options);
        }
        catch (JsonException error)
        {
            // The shape of the failure, not the whole exception message: somebody who pasted the wrong
            // thing needs to know it was not a block, not to be shown a JSON line number.
            return ClipboardReadResult.Refused(
                $"That is not a block - the text is {Describe(error)}.",
                "clipboard-malformed");
        }

        if (envelope is null || !string.Equals(envelope.Format, Format, StringComparison.Ordinal))
        {
            return ClipboardReadResult.Refused(
                "That is not a DeckForge block. Copy one from the canvas and paste it again.",
                "clipboard-foreign");
        }

        // A newer payload is refused outright, which is the entire point of carrying a version. It may
        // name a block kind or a field this build has never heard of, and importing it "successfully"
        // would leave a document that fails to compile with an error nobody can connect back to the paste.
        // Older is read, because that is what a version field is for.
        if (envelope.Version != Version)
        {
            return ClipboardReadResult.Refused(
                envelope.Version > Version
                    ? $"That block was copied by a newer DeckForge - format {envelope.Version}, and this build reads {Version}."
                    : $"That block is in format {envelope.Version}, which this build does not know.",
                "clipboard-version");
        }

        if (envelope.Run is not { Count: > 0 } source)
        {
            return ClipboardReadResult.Refused(
                "That block is empty - there is nothing in it to paste.",
                "clipboard-empty");
        }

        return ClipboardReadResult.Ok([.. source.Select(block => block.Clone(FreshId))]);
    }

    /// <summary>
    /// Whether some text is worth attempting a paste of, so ordinary text says "nothing to paste" rather
    /// than quoting a parse error at somebody who copied a paragraph.
    /// </summary>
    /// <param name="text">Whatever was on the clipboard.</param>
    public static bool LooksLikeABlock(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains(Format, StringComparison.Ordinal)
        && text.Contains(RunField, StringComparison.Ordinal);

    /// <summary>
    /// A fresh block id.
    /// </summary>
    /// <remarks>
    /// Handed to <see cref="Block.Clone"/> so that duplicate and paste re-identify by the same rule. The
    /// clone recurses into inputs and bodies, which is the part that matters: a pasted <c>repeat</c> whose
    /// inner blocks kept their ids would collide with the document one level down, and would surface as a
    /// duplicate id in generated code rather than as an obvious paste bug.
    /// </remarks>
    private static string FreshId() => $"blk-{Guid.NewGuid().ToString("n")[..8]}";

    private static string Describe(JsonException error) => error.Message.Contains('}', StringComparison.Ordinal)
        ? "in the wrong shape"
        : "not readable as JSON";

    private sealed class Envelope
    {
        public string? Format { get; set; }

        public int Version { get; set; }

        public string? Kind { get; set; }

        public List<Block>? Run { get; set; }
    }
}

/// <summary>
/// What a clipboard read produced: a run of freshly identified blocks, or a refusal with a code.
/// </summary>
/// <param name="Run">The blocks, with new ids, or empty when the read was refused.</param>
/// <param name="Problem">Why it was refused, or null.</param>
/// <param name="Code">The <c>vis-</c> code for that refusal, or null.</param>
public sealed record ClipboardReadResult(IReadOnlyList<Block> Run, string? Problem, string? Code)
{
    /// <summary>A readable payload.</summary>
    public static ClipboardReadResult Ok(IReadOnlyList<Block> run) => new(run, null, null);

    /// <summary>A refusal, with the code that goes in the diagnostics table.</summary>
    public static ClipboardReadResult Refused(string problem, string code) => new([], problem, code);

    /// <summary>
    /// The run as a drag payload, ready for <see cref="DropPlan"/>.
    /// </summary>
    /// <remarks>
    /// A payload rather than a direct insert, deliberately. Paste is a drop: the same rules decide where a
    /// pasted stack lands as decide where a dragged one does, so a run cannot arrive somewhere a drag
    /// would have been refused - which is how "paste put it inside a boolean slot" would have happened.
    /// <c>Source</c> stays null because the blocks are not in the document, and that is what tells
    /// <see cref="DropPlan"/> to insert rather than move.
    /// </remarks>
    public bool HasRun => Problem is null && Run.Count > 0;

    /// <summary>The run as a drag payload.</summary>
    public DragPayload ToDragPayload() => new(
        Source: null,
        First: Run.FirstOrDefault(),
        Run: Run,
        Kind: Run.FirstOrDefault()?.Kind ?? string.Empty,
        IsPalette: false,
        IsSingle: false);
}
