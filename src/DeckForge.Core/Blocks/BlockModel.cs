using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Blocks;

/// <summary>
/// Statement blocks for action executors (the Scratch-style canvas model).
/// </summary>
/// <remarks>
/// <para>
/// A block compiles to plain C# statements inside <c>// &lt;macrodeck-blocks&gt;</c> markers in
/// the action file. The compiler guarantees exactly three names in scope, and the action
/// generator emits all three:
/// </para>
/// <list type="bullet">
/// <item><c>context</c> - the <c>ActionExecutionContext</c> parameter of
/// <c>ExecuteAsync</c>.</item>
/// <item><c>_logger</c> - the Serilog <c>ILogger</c> the executor was constructed with.</item>
/// <item><c>_integration</c> - the <c>IIntegrationContext</c> captured during
/// <c>InitializeAsync</c>, which is where <c>Deck</c>, <c>Notifications</c>, <c>Widgets</c>,
/// <c>Variables</c>, <c>Scripts</c> and <c>Events</c> actually live. They are not on
/// <c>ActionExecutionContext</c>, which is why an earlier compiler emitted
/// <c>context.Deck?.OpenFolder(...)</c> and could never compile.</item>
/// </list>
/// </remarks>
public sealed class BlockProgram
{
    /// <summary>Schema version of the persisted canvas, so a future format change is detectable.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Target action local id, e.g. "log-message".</summary>
    public string TargetActionId { get; set; } = "";

    public List<BlockStatement> Statements { get; set; } = [];

    /// <summary>File name, relative to the plugin project, that <c>TargetActionId</c> lives in.</summary>
    public string TargetFile { get; set; } = "LogMessageAction.cs";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LogBlock), "log")]
[JsonDerivedType(typeof(SetVariableBlock), "set-variable")]
[JsonDerivedType(typeof(IfBlock), "if")]
[JsonDerivedType(typeof(ReturnResultBlock), "return-result")]
[JsonDerivedType(typeof(DelayBlock), "delay")]
[JsonDerivedType(typeof(HttpRequestBlock), "http-request")]
[JsonDerivedType(typeof(NotifyBlock), "notify")]
[JsonDerivedType(typeof(NavigateBlock), "navigate")]
[JsonDerivedType(typeof(GoToParentBlock), "go-to-parent")]
[JsonDerivedType(typeof(GoBackBlock), "go-back")]
[JsonDerivedType(typeof(ChangeProfileBlock), "change-profile")]
[JsonDerivedType(typeof(RunScriptBlock), "run-script")]
[JsonDerivedType(typeof(PublishEventBlock), "publish-event")]
[JsonDerivedType(typeof(SetVariableValueBlock), "set-variable-value")]
[JsonDerivedType(typeof(ReadVariableBlock), "read-variable")]
[JsonDerivedType(typeof(ShowModalBlock), "show-modal")]
[JsonDerivedType(typeof(InvalidateIconBlock), "invalidate-icon")]
[JsonDerivedType(typeof(ThrowBlock), "throw")]
public abstract class BlockStatement
{
    /// <summary>Plain-language summary shown on the canvas tile.</summary>
    /// <remarks>
    /// A property, not a method. The canvas bound <c>Text="{Binding Describe()}"</c>, and WPF binds to
    /// properties - there is no <c>Describe()</c> property to find, so every statement row rendered as
    /// an empty pill. The program looked empty however many blocks it held, and there was no error to
    /// point at it.
    /// </remarks>
    public abstract string Description { get; }
}

/// <summary>
/// Writes a Serilog message, substituting any <c>{name}</c> holes from the configured
/// parameters.
/// </summary>
/// <remarks>
/// Holes are resolved from <c>context.Parameters</c> rather than from a bare identifier. The
/// earlier compiler emitted the parameter name as if it were already a local, so a canvas
/// containing a log block with a parameter produced <c>CS0103</c> on the very first save.
/// </remarks>
public sealed class LogBlock : BlockStatement
{
    public string Template { get; set; } = "Action ran";

    /// <summary>Optional parameter appended as a structured value after the rendered message.</summary>
    public string? Parameter { get; set; }

    /// <summary>Log level: Verbose, Debug, Information, Warning, Error.</summary>
    public string Level { get; set; } = "Information";

    public override string Description => Parameter is null ? $"Log \"{Template}\"" : $"Log \"{Template}\" ({Parameter})";
}

/// <summary>Reads a configured parameter into a typed local.</summary>
public sealed class SetVariableBlock : BlockStatement
{
    public string VariableName { get; set; } = "value";

    /// <summary>Parameter name from context.Parameters, or null to use <see cref="Literal"/>.</summary>
    public string? FromParameter { get; set; }

    public string? Literal { get; set; }

    /// <summary>string | number | bool</summary>
    public string Type { get; set; } = "string";

    /// <summary>When set, a blank result returns this error instead of continuing.</summary>
    public bool Required { get; set; }

    public override string Description
    {
        get
        {
            var source = FromParameter is not null ? $"param {FromParameter}" : $"\"{Literal}\"";
            return $"Set {VariableName} = {source}";
        }
    }
}

/// <summary>A conditional with both branches.</summary>
public sealed class IfBlock : BlockStatement
{
    public string LeftVariable { get; set; } = "";

    /// <summary>
    /// == != &gt; &lt; &gt;= &lt;= isEmpty isNotEmpty isAvailable isNotAvailable contains notContains.
    /// </summary>
    public string Operator { get; set; } = "==";

    public string? RightLiteral { get; set; }

    public List<BlockStatement> Then { get; set; } = [];

    public List<BlockStatement> Else { get; set; } = [];

    public override string Description => $"If {LeftVariable} {Operator} \"{RightLiteral ?? string.Empty}\"";
}

/// <summary>ActionResult.Success / Failed(code, message) / Accepted(message).</summary>
public sealed class ReturnResultBlock : BlockStatement
{
    /// <summary>success | failed | accepted</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>An <c>ActionErrorCodes</c> member name, used when <see cref="Outcome"/> is failed.</summary>
    public string? ErrorCode { get; set; }

    public string Message { get; set; } = "";

    public override string Description => Outcome switch
    {
        "failed" => $"Fail ({ErrorCode ?? "ProviderError"})",
        "accepted" => "Accept (pending)",
        _ => "Succeed",
    };
}

/// <summary>await Task.Delay(ms, context.CancellationToken).</summary>
public sealed class DelayBlock : BlockStatement
{
    public int Milliseconds { get; set; } = 500;

    public override string Description => $"Wait {Milliseconds} ms";
}

/// <summary>HttpClient GET with cancellation; the response body lands in a variable.</summary>
public sealed class HttpRequestBlock : BlockStatement
{
    public string Url { get; set; } = "";

    public string IntoVariable { get; set; } = "response";

    /// <summary>Optional bearer token, read from a configured parameter.</summary>
    public string? BearerTokenParameter { get; set; }

    public override string Description => $"GET {Url}";
}

/// <summary>Raises a host notification. Fire-and-forget, and never throws into the caller.</summary>
public sealed class NotifyBlock : BlockStatement
{
    public string Title { get; set; } = "Done";

    public string Message { get; set; } = "The action finished.";

    /// <summary>Info | Warning | Error</summary>
    public string Level { get; set; } = "Info";

    /// <summary>Optional key, which makes a repeat replace the previous notification.</summary>
    public string? Key { get; set; }

    public override string Description => $"Notify \"{Title}\"";
}

/// <summary>Opens a folder on the pressing client.</summary>
public sealed class NavigateBlock : BlockStatement
{
    public string FolderId { get; set; } = "";

    public override string Description => $"Open folder {FolderId}";
}

/// <summary>Goes to the parent folder on the pressing client.</summary>
public sealed class GoToParentBlock : BlockStatement
{
    public override string Description => "Go to parent folder";
}

/// <summary>Returns to the previously shown folder on the pressing client.</summary>
public sealed class GoBackBlock : BlockStatement
{
    public override string Description => "Go back";
}

/// <summary>Switches to a profile's start folder.</summary>
public sealed class ChangeProfileBlock : BlockStatement
{
    public string ProfileId { get; set; } = "";

    public override string Description => $"Switch to profile {ProfileId}";
}

/// <summary>Runs a host script through <c>context.Scripts</c>.</summary>
public sealed class RunScriptBlock : BlockStatement
{
    public string ScriptId { get; set; } = "";

    /// <summary>Input name to parameter name, one <c>scriptInput=parameter</c> per line.</summary>
    public string Inputs { get; set; } = "";

    public override string Description => $"Run script {ScriptId}";
}

/// <summary>Publishes an event occurrence through <c>context.Events</c>.</summary>
public sealed class PublishEventBlock : BlockStatement
{
    public string EventId { get; set; } = "";

    /// <summary>Payload parameter name to value, one <c>name=value</c> per line.</summary>
    public string Payload { get; set; } = "";

    public override string Description => $"Publish {EventId}";
}

/// <summary>Writes a shared host variable through <c>context.Variables</c>.</summary>
public sealed class SetVariableValueBlock : BlockStatement
{
    public string VariableName { get; set; } = "";

    /// <summary>Value to write, or a configured parameter name when <see cref="UseParameter"/> is set.</summary>
    public string Value { get; set; } = "";

    public bool UseParameter { get; set; }

    public override string Description => $"Set variable {VariableName}";
}

/// <summary>Reads a shared host variable into a local, via <c>context.Variables</c>.</summary>
public sealed class ReadVariableBlock : BlockStatement
{
    public string VariableName { get; set; } = "";

    public string IntoVariable { get; set; } = "variableValue";

    public override string Description => $"Read variable {VariableName} into {IntoVariable}";
}

/// <summary>
/// Opens a modal on the pressing client through <c>context.Ui</c>.
/// </summary>
/// <remarks>
/// A modal names a <em>view</em> the plugin's own <c>IUiProvider</c> serves, rather than carrying
/// text. <c>ModalDefinition</c> has exactly three members - a required <c>ViewId</c>, an optional
/// localized <c>Title</c> and an optional <c>Data</c> map - so a block cannot invent a body or
/// button labels. Anything the modal shows is built by the provider when the host opens the
/// <c>dialog</c> surface.
/// </remarks>
public sealed class ShowModalBlock : BlockStatement
{
    /// <summary>The provider's dialog id. The plugin must serve a matching view.</summary>
    public string ViewId { get; set; } = "";

    /// <summary>The heading the host renders above the provider's content.</summary>
    public string Title { get; set; } = "";

    /// <summary>Context handed to the provider verbatim, one <c>name=value</c> per line.</summary>
    public string Data { get; set; } = "";

    public override string Description => $"Open dialog \"{ViewId}\"";
}

/// <summary>Asks the host to re-fetch an action's icon.</summary>
public sealed class InvalidateIconBlock : BlockStatement
{
    public string ActionId { get; set; } = "";

    public override string Description => $"Invalidate icon {ActionId}";
}

/// <summary>Throws, which the host turns into a failed invocation.</summary>
public sealed class ThrowBlock : BlockStatement
{
    public string Message { get; set; } = "The action failed.";

    public override string Description => $"Throw \"{Message}\"";
}

/// <summary>Serialization for the persisted canvas.</summary>
public static class BlockProgramJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(BlockProgram program) => JsonSerializer.Serialize(program, CreateOptions());

    public static BlockProgram Deserialize(string json) =>
        JsonSerializer.Deserialize<BlockProgram>(json, CreateOptions()) ?? new BlockProgram();

    /// <summary>Reads a canvas, returning false rather than throwing on malformed input.</summary>
    /// <remarks>
    /// <c>NotSupportedException</c> is caught alongside <see cref="JsonException"/> because a
    /// <see cref="BlockStatement"/> list is polymorphic and carries its own type discriminator: a file that
    /// has a <c>statements</c> array whose entries name no known kind is JSON, and structurally this shape,
    /// and still not something this reader can produce. Catching only <see cref="JsonException"/> let that
    /// escape — which was unreachable while the only caller was handed a file it had already decided was a
    /// legacy canvas, and stopped being unreachable the moment anything went <em>looking</em> for one. A
    /// file called <c>something.blocks.json</c> that turns out to be somebody's own JSON is the ordinary
    /// case in a plugin directory, and taking the Visual page down over it would be a worse answer than
    /// saying the file is not one.
    /// </remarks>
    public static bool TryDeserialize(string json, out BlockProgram program)
    {
        try
        {
            program = Deserialize(json);
            return true;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            program = new BlockProgram();
            return false;
        }
    }

    /// <summary>The file name a canvas for <paramref name="program"/> is persisted under.</summary>
    public static string FileNameFor(BlockProgram program) =>
        SanitizeFileName(program.TargetActionId) + ".blocks.json";

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var result = new string(chars).Trim('-');
        return result.Length == 0 ? "program" : result;
    }
}
