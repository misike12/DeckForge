using System.Text.RegularExpressions;

namespace DeckForge.Tests;

/// <summary>
/// The diagnostics catalogue in <c>visual.md</c> Appendix F, read from the document.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is the promise - a new finding is either added to it or renamed to something already in it -
/// so a test that checks against a *copy* of it is checking that the copy still matches itself. It did, for
/// months, while the product reported fourteen codes the copy did not contain and the copy listed ten nothing
/// reports.
/// </para>
/// <para>
/// Read off disk for the same reason <c>XamlMarkupTests</c> reads the markup: the document is part of the
/// repository and the test host can reach it, and a stale table then fails a test rather than quietly
/// misleading whoever adds the next diagnostic.
/// </para>
/// </remarks>
internal static class AppendixF
{
    /// <summary>Every code the document declares, in either table.</summary>
    public static IReadOnlySet<string> Codes()
    {
        var path = FindDocument();
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"`(vis-[a-z0-9-]+)`"))
        {
            codes.Add(match.Groups[1].Value);
        }

        return codes;
    }

    private static string FindDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "visual.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "visual.md was not found above the test output, so Appendix F cannot be read.");
    }
}