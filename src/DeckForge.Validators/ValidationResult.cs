namespace DeckForge.Validators;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One validation finding. Codes mirror the macrodeck-plugin CLI's kebab-case ids.</summary>
public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Message,
    string? Field = null);

/// <summary>Result of validating one artifact (manifest, resx file, project).</summary>
public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; init; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);
    public bool Ok => !HasErrors;

    public static ValidationResult Success() => new();

    public void Add(string code, ValidationSeverity severity, string message, string? field = null) =>
        Issues.Add(new ValidationIssue(code, severity, message, field));

    public override string ToString()
    {
        if (Issues.Count == 0)
        {
            return "0 error(s), 0 warning(s)";
        }
        var errors = Issues.Count(i => i.Severity == ValidationSeverity.Error);
        var warnings = Issues.Count - errors;
        return $"{errors} error(s), {warnings} warning(s)";
    }
}
