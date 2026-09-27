using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckForge.Core.Blocks;

/// <summary>
/// Statement blocks for action executors (the Scratch-style canvas model). The executor's
/// parameters, cancellation token and logger are in scope by contract; blocks compile to
/// plain C# statements inside // <macrodeck-blocks> markers in the action file.
/// </summary>
public sealed class BlockProgram
{
    public int Version { get; set; } = 1;

    /// <summary>Target action local id, e.g. "log-message".</summary>
    public string TargetActionId { get; set; } = "";

    public List<BlockStatement> Statements { get; set; } = [];
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
public abstract class BlockStatement
{
    /// <summary>Plain-language summary shown on the canvas tile.</summary>
    public abstract string Describe();
}

/// <summary>_logger.Information(message, param)</summary>
public sealed class LogBlock : BlockStatement
{
    public string Template { get; set; } = "Action ran";
    public string? Parameter { get; set; }

    public override string Describe() => $"Log \"{Template}\"";
}

/// <summary>Reads a parameter into a typed local.</summary>
public sealed class SetVariableBlock : BlockStatement
{
    public string VariableName { get; set; } = "value";
    /// <summary>Parameter name from context.Parameters, or a literal.</summary>
    public string? FromParameter { get; set; }
    public string? Literal { get; set; }
    /// <summary>string | number | bool</summary>
    public string Type { get; set; } = "string";

    public override string Describe()
    {
        var source = FromParameter is not null ? $"param {FromParameter}" : $"\"{Literal}\"";
        return $"Set {VariableName} = {source}";
    }
}

public sealed class IfBlock : BlockStatement
{
    public string LeftVariable { get; set; } = "";
    /// <summary>== != > < >= <= isEmpty isNotEmpty</summary>
    public string Operator { get; set; } = "==";
    public string? RightLiteral { get; set; }
    public List<BlockStatement> Then { get; set; } = [];

    public override string Describe() => $"If {LeftVariable} {Operator} {RightLiteral ?? "?"}";
}

/// <summary>ActionResult.Success / Failed(code, message) / Accepted(message).</summary>
public sealed class ReturnResultBlock : BlockStatement
{
    /// <summary>success | failed | accepted</summary>
    public string Outcome { get; set; } = "success";
    /// <summary>ActionErrorCodes name used for failed.</summary>
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = "";

    public override string Describe() => Outcome switch
    {
        "failed" => $"Fail ({ErrorCode})",
        "accepted" => "Accept (pending)",
        _ => "Succeed",
    };
}

/// <summary>await Task.Delay(ms, context.CancellationToken).</summary>
public sealed class DelayBlock : BlockStatement
{
    public int Milliseconds { get; set; } = 500;

    public override string Describe() => $"Wait {Milliseconds} ms";
}

/// <summary>HttpClient GET with cancellation; response body into a variable.</summary>
public sealed class HttpRequestBlock : BlockStatement
{
    public string Url { get; set; } = "";
    public string IntoVariable { get; set; } = "response";

    public override string Describe() => $"GET {Url}";
}

/// <summary>Host notification via context.Notifications.Notify (fire-and-forget).</summary>
public sealed class NotifyBlock : BlockStatement
{
    public string Title { get; set; } = "Done";
    public string Message { get; set; } = "The action finished.";

    public override string Describe() => $"Notify \"{Title}\"";
}

/// <summary>Deck navigation: open a folder by id on the pressing client.</summary>
public sealed class NavigateBlock : BlockStatement
{
    public string FolderId { get; set; } = "";

    public override string Describe() => $"Open folder {FolderId}";
}

public static class BlockProgramJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(BlockProgram program) => JsonSerializer.Serialize(program, Options);

    public static BlockProgram Deserialize(string json) =>
        JsonSerializer.Deserialize<BlockProgram>(json, Options) ?? new BlockProgram();
}
