namespace DeckForge.Core.Plugins;

/// <summary>
/// The Macro Deck SDK / CLI versions DeckForge generates against. Macro Deck prereleases
/// publish both `beta` and `preview` tags and SemVer orders beta BEFORE preview, so a
/// floating `3.0.0-*` resolves to the older preview line - generated projects pin an exact
/// version instead. Update <see cref="DefaultVersion"/> when a new Macro Deck release ships.
/// </summary>
public static class MacroDeckSdkInfo
{
    /// <summary>Latest confirmed Macro Deck SDK release (user-verified 2026-09-27).</summary>
    public const string DefaultVersion = "3.0.0-beta.14";

    /// <summary>macrodeck-plugin CLI version matching the SDK (installed with --prerelease).</summary>
    public const string DefaultCliVersion = "3.0.0-beta.14";

    /// <summary>.NET major the Macro Deck 3 host bundles and plugins target.</summary>
    public const string DefaultDotnetVersion = "10.0";

    /// <summary>compatibility.macroDeck range written by `macrodeck-plugin new`.</summary>
    public const string DefaultMacroDeckRange = ">=3.0.0-0";
}
