using System.Text.Json;
using System.Text.Json.Serialization;
using DeckForge.Core.Blocks;
using DeckForge.Core.Visual.Migrations;

namespace DeckForge.Core.Visual;

/// <summary>What happened when a document was read.</summary>
public enum VisualLoadOutcome
{
    /// <summary>Read as this format.</summary>
    Loaded,

    /// <summary>Read as a legacy <c>.blocks.json</c> and migrated. The caller should offer to keep the original.</summary>
    LoadedFromLegacy,

    /// <summary>
    /// Read, and marked read-only because the file was written by a newer DeckForge. The document is
    /// real and must not be written back.
    /// </summary>
    /// <remarks>
    /// A third outcome for the newer-schema case, alongside <see cref="UnsupportedSchema"/>, and the
    /// reason is that Part 22.1's table says a version above this one is <em>read-only</em> rather than
    /// unreadable. The first implementation returned nothing at all, which left the page drawing the
    /// sample document and telling the user the canvas had not been read — true, and useless, because the
    /// one thing a user with a newer file wants is to see what is in it.
    /// </remarks>
    LoadedReadOnly,

    /// <summary>Not JSON at all.</summary>
    NotJson,

    /// <summary>
    /// Written by a newer DeckForge, and the caller asked not to be handed one. It must never be written
    /// back.
    /// </summary>
    UnsupportedSchema,

    /// <summary>JSON, and structurally this format, but not usable. The file is left untouched.</summary>
    Corrupt,
}

/// <summary>
/// Reads and writes <see cref="VisualProject"/> as JSON.
/// </summary>
/// <remarks>
/// <para>
/// The format is hand-written rather than attribute-driven, because the design commits to a shape:
/// <c>{"kind": "http.get", "id": "b2", "inputs": {"url": {"slot": {…}}}, "fields": {…}}</c>. Attributes
/// would also emit every computed property, and would write empty collections and explicit nulls —
/// noise in a file that is meant to be read, diffed and reviewed by a human.
/// </para>
/// <para>
/// Legacy detection is <em>structural</em>, not by version number. <c>BlockProgram.CurrentVersion</c>
/// is 1 and so is this format's, so a version check would happily read a legacy file as an empty
/// document: the two share a field name and nothing else. Looking for <c>statements</c> versus
/// <c>targets</c> is what actually distinguishes them.
/// </para>
/// </remarks>
public static class VisualProjectJson
{
    /// <summary>The file name, relative to <c>.deckforge/visual/</c>.</summary>
    public const string FileName = "project.visual.json";

    public static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new BlockJsonConverter(),
            new BlockInputJsonConverter(),
        },
    };

    /// <summary>Writes a document as JSON, filling in anything the format requires but the object lacks.</summary>
    /// <remarks>
    /// Normalised on the way out as well as on the way in, because a document built in memory — a fresh
    /// one, or one from the sample — has procedure ids the file format requires but nothing has assigned.
    /// Writing without filling them would produce a file that reads back differently from what was
    /// written, which is the exact failure <c>VisualStore.IsDirty</c> would then report on every check.
    /// </remarks>
    public static string Serialize(VisualProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        project.EnsureProcedureIds();

        return JsonSerializer.Serialize(project, CreateOptions());
    }

    /// <summary>Reads a document, throwing when the text is not one.</summary>
    /// <exception cref="JsonException">The text is not a visual document.</exception>
    public static VisualProject Deserialize(string json) =>
        TryLoad(json, out var project, out var message) == VisualLoadOutcome.Loaded
            ? project
            : throw new JsonException(message);

    /// <summary>
    /// Reads a document, reporting what kind of read it was instead of throwing.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="project">The document, or an empty one on failure.</param>
    /// <param name="message">A sentence naming what happened, suitable for the status line.</param>
    /// <returns>What kind of read this was.</returns>
    /// <remarks>
    /// <para>
    /// The page needs to tell four situations apart, and they lead to four different actions: load it,
    /// migrate it and keep the original aside, refuse and explain, or leave the file alone. A method
    /// that returns a document or null cannot carry that, which is why this returns an outcome.
    /// </para>
    /// <para>
    /// <strong>This is the reader that owns the file.</strong> A version newer than this build's is
    /// refused here rather than handed over, because everything downstream of a load — the emitter, the
    /// interpreter, the writer — is entitled to assume the document is this build's vocabulary.
    /// <see cref="TryLoadReadOnly"/> is the other half: it shows a newer file without pretending to own
    /// it.
    /// </para>
    /// </remarks>
    public static VisualLoadOutcome TryLoad(string json, out VisualProject project, out string message) =>
        Read(json, acceptNewerSchema: false, out project, out message);

    /// <summary>
    /// Reads a document this build may open but must never write back, which is what a file from a newer
    /// DeckForge is.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="project">
    /// The document, with <see cref="VisualProject.ReadOnly"/> set when the file is newer. Empty on
    /// failure.
    /// </param>
    /// <param name="message">A sentence naming what happened, suitable for the status line.</param>
    /// <returns>
    /// <see cref="VisualLoadOutcome.LoadedReadOnly"/> for a newer file, and otherwise exactly what
    /// <see cref="TryLoad"/> would have returned.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A second entry point rather than a changed one, deliberately. <see cref="TryLoad"/> answers "can
    /// this build own this file?" — which is what the writer needs to know, and what a test asserting a
    /// refusal has always meant. This one answers "can this build show it?" A single method with a
    /// parameter would have had to pick one of those as the default, and the default is the one that gets
    /// forgotten: the page wants to display the file, and a reader whose happy path refuses to display it
    /// is a reader somebody works around.
    /// </para>
    /// <para>
    /// The two differ in exactly one respect, and <see cref="VisualProject.ReadOnly"/> is what carries it:
    /// the document is parsed either way, but only here is the result handed over. Nothing is
    /// downgraded, nothing is dropped, and <c>Version</c> still reads back as whatever the file said —
    /// the document is the newer document, not a translation of it into this build's vocabulary.
    /// </para>
    /// </remarks>
    public static VisualLoadOutcome TryLoadReadOnly(string json, out VisualProject project, out string message) =>
        Read(json, acceptNewerSchema: true, out project, out message);

    /// <summary>
    /// The one reader, parameterised by whether a newer schema is handed over or refused.
    /// </summary>
    private static VisualLoadOutcome Read(
        string json,
        bool acceptNewerSchema,
        out VisualProject project,
        out string message)
    {
        project = new VisualProject();
        message = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            message = $"The canvas file could not be read as JSON ({ex.Message}). It has been left alone.";
            return VisualLoadOutcome.NotJson;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                message = "The canvas file is not a document. It has been left alone.";
                return VisualLoadOutcome.Corrupt;
            }

            // Legacy: a BlockProgram. It has statements at the root and no targets anywhere.
            if (root.TryGetProperty("statements", out _) && !root.TryGetProperty("targets", out _))
            {
                if (!BlockProgramJson.TryDeserialize(json, out var legacy))
                {
                    message = "The saved blocks file could not be read. It has been left alone.";
                    return VisualLoadOutcome.Corrupt;
                }

                project = BlocksV1Migration.Migrate(legacy);
                message =
                    $"Migrated {legacy.Statements.Count} block(s) from the old Blocks canvas. "
                    + "The original file was kept beside it.";
                return VisualLoadOutcome.LoadedFromLegacy;
            }

            var newerSchema = false;
            if (root.TryGetProperty("version", out var version)
                && version.TryGetInt32(out var number)
                && number > VisualProject.CurrentVersion)
            {
                // One sentence for both outcomes. The refusal and the read-only open are the same fact
                // told to two different callers, and a caller that had to choose between them would be
                // choosing which sentence a user reads — so the text names the versions, says read-only,
                // and says the file will not be overwritten, whichever way the branch below goes.
                message =
                    $"This canvas was written by a newer DeckForge (format {number}, this build reads "
                    + $"{VisualProject.CurrentVersion}). It is open read-only and will not be overwritten.";

                if (!acceptNewerSchema)
                {
                    return VisualLoadOutcome.UnsupportedSchema;
                }

                newerSchema = true;
            }

            VisualProject? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<VisualProject>(json, CreateOptions());
            }
            catch (JsonException ex)
            {
                message = $"The canvas file could not be read ({ex.Message}). It has been left alone.";
                return VisualLoadOutcome.Corrupt;
            }

            if (loaded is null)
            {
                message = "The canvas file is empty. It has been left alone.";
                return VisualLoadOutcome.Corrupt;
            }

            // An input with two members set is not decidable from the file, and guessing which the user
            // meant would silently change their program. It is refused, by id, so it can be fixed. A
            // read-only open is refused for it too: handing over a document we already know is wrong is
            // worse than handing over none.
            foreach (var block in loaded.Blocks())
            {
                foreach (var (slot, input) in block.Inputs.Where(pair => pair.Value.IsAmbiguous))
                {
                    message =
                        $"Block {block.Id} ({block.Kind}) has more than one value in its '{slot}' slot, "
                        + "which cannot be resolved. The file has been left alone.";
                    return VisualLoadOutcome.Corrupt;
                }
            }

            loaded.ReadOnly = newerSchema;
            project = loaded;
            project.EnsureProcedureIds();

            message = newerSchema ? message : string.Empty;
            return newerSchema ? VisualLoadOutcome.LoadedReadOnly : VisualLoadOutcome.Loaded;
        }
    }

    /// <summary>
    /// Writes a <see cref="Block"/> in the documented shape, omitting everything that is empty.
    /// </summary>
    /// <remarks>
    /// The order of the members is fixed — kind, id, then the collections — so two documents that mean
    /// the same thing are identical text, which is what makes a sidecar pleasant to review in a diff.
    /// </remarks>
    private sealed class BlockJsonConverter : JsonConverter<Block>
    {
        public override Block Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A block must be an object.");
            }

            var block = new Block();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var name = reader.GetString();
                reader.Read();

                switch (name)
                {
                    case "kind":
                        block.Kind = reader.GetString() ?? string.Empty;
                        break;
                    case "id":
                        block.Id = reader.GetString() ?? string.Empty;
                        break;
                    case "disabled":
                        block.Disabled = reader.TokenType == JsonTokenType.True;
                        break;
                    case "comment":
                        block.Comment = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "inputs":
                        block.Inputs = JsonSerializer.Deserialize<Dictionary<string, BlockInput>>(ref reader, options) ?? [];
                        break;
                    case "fields":
                        block.Fields = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options) ?? [];
                        break;
                    case "bodies":
                        block.Bodies = JsonSerializer.Deserialize<Dictionary<string, List<Block>>>(ref reader, options) ?? [];
                        break;
                    default:
                        // An unknown member is skipped rather than refused: a document written by a
                        // slightly newer build should still open, and its unrecognised data is reported
                        // by the validator instead of being an error here.
                        reader.Skip();
                        break;
                }
            }

            return block;
        }

        public override void Write(Utf8JsonWriter writer, Block value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind);
            writer.WriteString("id", value.Id);

            if (value.Inputs.Count > 0)
            {
                writer.WritePropertyName("inputs");
                JsonSerializer.Serialize(writer, value.Inputs, options);
            }

            if (value.Fields.Count > 0)
            {
                writer.WritePropertyName("fields");
                JsonSerializer.Serialize(writer, value.Fields, options);
            }

            if (value.Bodies.Count > 0)
            {
                writer.WritePropertyName("bodies");
                JsonSerializer.Serialize(writer, value.Bodies, options);
            }

            if (value.Disabled)
            {
                writer.WriteBoolean("disabled", true);
            }

            if (!string.IsNullOrEmpty(value.Comment))
            {
                writer.WriteString("comment", value.Comment);
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>Writes a slot's contents as a single-key object naming what it holds.</summary>
    private sealed class BlockInputJsonConverter : JsonConverter<BlockInput>
    {
        public override BlockInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A slot value must be an object.");
            }

            var input = new BlockInput();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var name = reader.GetString();
                reader.Read();

                switch (name)
                {
                    case "slot":
                        input.Block = JsonSerializer.Deserialize<Block>(ref reader, options);
                        break;
                    case "var":
                        input.Variable = reader.GetString();
                        break;
                    case "text":
                        input.Text = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "number":
                        input.Number = reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();
                        break;
                    case "bool":
                        input.Boolean = reader.TokenType == JsonTokenType.Null ? null : reader.GetBoolean();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return input;
        }

        public override void Write(Utf8JsonWriter writer, BlockInput value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            switch (value.Kind)
            {
                case BlockInputKind.Block:
                    writer.WritePropertyName("slot");
                    JsonSerializer.Serialize(writer, value.Block, options);
                    break;
                case BlockInputKind.Variable:
                    writer.WriteString("var", value.Variable);
                    break;
                case BlockInputKind.Text:
                    writer.WriteString("text", value.Text);
                    break;
                case BlockInputKind.Number:
                    writer.WriteNumber("number", value.Number!.Value);
                    break;
                case BlockInputKind.Boolean:
                    writer.WriteBoolean("bool", value.Boolean!.Value);
                    break;
                case BlockInputKind.Empty:
                default:
                    break;
            }

            writer.WriteEndObject();
        }
    }
}
