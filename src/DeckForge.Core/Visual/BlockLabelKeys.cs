using System.Globalization;

namespace DeckForge.Core.Visual;

/// <summary>
/// Where a block's user-facing strings come from.
/// </summary>
/// <remarks>
/// §21: "block labels are user-facing strings and must go through the plugin's own localization, which
/// DeckForge already has a manager and a validator for". So the lookup is an interface with an
/// implementation the App supplies, and the *keys* are Core's — because a key that only the App knows is a key
/// the validator cannot check and the generator cannot emit.
/// </remarks>
public interface IBlockTextLookup
{
    /// <summary>
    /// The text for a key, or null when this lookup has nothing for it.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string, because "no translation" and "a translation of nothing" are
    /// different and the caller has to fall back in only the first case.
    /// </remarks>
    /// <param name="key">The §21.1 key.</param>
    string? Find(string key);
}

/// <summary>
/// A lookup that has no translations, so every label falls back to the catalogue's English.
/// </summary>
/// <remarks>
/// The default on purpose. A build with no localization loaded is the normal build, and a page whose labels
/// come out blank because nothing was registered is worse than a page in English.
/// </remarks>
public sealed class NoTranslations : IBlockTextLookup
{
    /// <summary>The shared instance, because it holds nothing.</summary>
    public static NoTranslations Instance { get; } = new();

    public string? Find(string key) => null;
}

/// <summary>
/// The resx keys a block's strings live at, and the rule for turning a catalogue row into them.
/// </summary>
/// <remarks>
/// <para>
/// Part 21.1's table, generated rather than written: <c>Blocks.&lt;Category&gt;.&lt;BlockId&gt;</c>,
/// <c>…​.Slot.&lt;slot&gt;</c>, <c>…​.Menu.&lt;option&gt;</c>, <c>Blocks.Category.&lt;Category&gt;</c> and
/// <c>Blocks.Hat.&lt;HatId&gt;</c>.
/// </para>
/// <para>
/// <see cref="LabelId"/> turns <c>deck.open-folder</c> into <c>DeckOpenFolder</c>, which is what makes the
/// keys *stable*: a resx key that contained the catalogue's dotted kind would change the moment somebody
/// renamed a block, and every translation would silently become an orphan in a file nobody reads.
/// </para>
/// </remarks>
public static class BlockLabelKeys
{
    /// <summary>The prefix every key shares.</summary>
    public const string Prefix = "Blocks";

    /// <summary>A block's label key.</summary>
    /// <param name="descriptor">The block's row.</param>
    public static string Label(BlockDescriptor descriptor) => $"{Prefix}.{descriptor.Category}.{LabelId(descriptor.Kind)}";

    /// <summary>A slot's label key.</summary>
    /// <param name="descriptor">The block's row.</param>
    /// <param name="slot">The slot.</param>
    public static string Slot(BlockDescriptor descriptor, SlotDescriptor slot) =>
        $"{Label(descriptor)}.Slot.{Id(slot.Name)}";

    /// <summary>A menu option's key.</summary>
    /// <param name="descriptor">The block's row.</param>
    /// <param name="option">The option's key, as the catalogue spells it.</param>
    public static string Menu(BlockDescriptor descriptor, string option) =>
        $"{Label(descriptor)}.Menu.{Id(option)}";

    /// <summary>A category's name key.</summary>
    /// <param name="category">The category.</param>
    public static string Category(BlockCategory category) => $"{Prefix}.Category.{category}";

    /// <summary>A hat's description key.</summary>
    /// <param name="descriptor">The hat's row.</param>
    public static string Hat(BlockDescriptor descriptor) => $"{Prefix}.Hat.{LabelId(descriptor.Kind)}";

    /// <summary>
    /// The identifier half of a key: PascalCase, with the category's dots removed.
    /// </summary>
    /// <param name="kind">A catalogue kind such as <c>deck.open-folder</c>.</param>
    /// <remarks>
    /// PascalCase and stable, for the reason above: a key that moves is a translation that is lost without
    /// anything reporting a problem.
    /// </remarks>
    public static string LabelId(string kind) => string.Concat(
        kind.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(Id));

    /// <summary>One segment of a key, PascalCased.</summary>
    /// <param name="segment">A kind part, a slot name or a menu option.</param>
    public static string Id(string segment) =>
        string.Concat(segment.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            part.Length == 0
                ? string.Empty
                : char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..]));

    /// <summary>
    /// A label, its slot's, or the catalogue's own English.
    /// </summary>
    /// <remarks>
    /// The fallback is the rule that makes this safe to adopt: an untranslated block is still a block, and a
    /// missing key must never produce an empty label on the canvas — which would look like a rendering fault
    /// rather than a missing translation.
    /// </remarks>
    /// <param name="lookup">Where translations come from.</param>
    /// <param name="key">The key to resolve.</param>
    /// <param name="english">What to say when the lookup has nothing.</param>
    public static string Text(IBlockTextLookup lookup, string key, string english) =>
        lookup.Find(key) is { Length: > 0 } translated ? translated : english;
}
