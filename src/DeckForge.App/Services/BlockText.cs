using DeckForge.Core.Visual;

namespace DeckForge.App.Services;

/// <summary>
/// The workspace's block-label translations, for the view models that build blocks.
/// </summary>
/// <remarks>
/// <para>
/// A static, and deliberately so. Part 21 wants block labels localized, and roughly thirty places build a
/// <see cref="BlockNodeViewModel"/> - the palette, the canvas, a body's statements, the inspector's preview
/// - none of which should have to be handed a lookup to say a word. Threading it through every constructor
/// would have made the feature look like a change to the block editor rather than to a string, which is
/// exactly the kind of coupling this design has been refusing.
/// </para>
/// <para>
/// The same pattern as <see cref="ShellMessenger"/>: the shell tells the app something global, and nobody
/// passes it on. It defaults to <see cref="NoTranslations"/>, so a build with no localization loaded is the
/// ordinary case and nothing has to check.
/// </para>
/// <para>
/// Only <see cref="Use"/> should assign it, and only the page that knows which workspace is open.
/// </para>
/// </remarks>
public static class BlockText
{
    private static IBlockTextLookup _current = NoTranslations.Instance;

    /// <summary>Where block labels currently get their words.</summary>
    public static IBlockTextLookup Current => _current;

    /// <summary>Whether any translation is loaded, which is what a translator's build wants to assert.</summary>
    public static bool Localized => _current is not NoTranslations;

    /// <summary>
    /// Point the block labels at one workspace's localization directory.
    /// </summary>
    /// <remarks>
    /// A null or missing directory is not an error: it hands back the English fallback, so a workspace that
    /// has never been translated needs no branch anywhere else.
    /// </remarks>
    /// <param name="directory">Where the workspace's <c>Strings*.resx</c> live, or null.</param>
    /// <param name="culture">The culture to read, or null for the neutral file alone.</param>
    /// <returns>The lookup now in force, so a caller can rebuild what it has already drawn.</returns>
    public static ResxTextLookup Use(string? directory, string? culture = null)
    {
        var lookup = new ResxTextLookup(directory, culture ?? ResxTextLookup.CurrentCultureName);
        _current = lookup;
        return lookup;
    }

    /// <summary>Go back to the catalogue's English, for when no workspace is open.</summary>
    public static void Reset() => _current = NoTranslations.Instance;
}
