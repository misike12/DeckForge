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
    public List<ValidationIssue> Issues { get; } = [];

    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

    public bool HasInfo => Issues.Any(i => i.Severity == ValidationSeverity.Info);

    /// <summary>True when nothing at all was reported.</summary>
    public bool IsClean => Issues.Count == 0;

    /// <summary>Errors alone decide this, which is how the tool's own <c>valid</c> is computed.</summary>
    public bool Ok => !HasErrors;

    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);

    public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);

    public static ValidationResult Success() => new();

    public void Add(string code, ValidationSeverity severity, string message, string? field = null) =>
        Issues.Add(new ValidationIssue(code, severity, message, field));

    public void AddRange(IEnumerable<ValidationIssue> issues) => Issues.AddRange(issues);

    public void Clear() => Issues.Clear();

    /// <summary>The findings, most severe first, which is the order a reader wants them in.</summary>
    public IReadOnlyList<ValidationIssue> Ordered =>
        [.. Issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Code, StringComparer.Ordinal)];

    public override string ToString()
    {
        if (Issues.Count == 0)
        {
            return "0 error(s), 0 warning(s)";
        }

        // Counted by severity rather than by subtraction. The previous version computed warnings
        // as Issues.Count - errors, which counted every Info finding as a warning and so could
        // disagree with HasWarnings on the same result.
        var summary = $"{ErrorCount} error(s), {WarningCount} warning(s)";
        return InfoCount > 0 ? $"{summary}, {InfoCount} note(s)" : summary;
    }
}
