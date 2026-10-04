namespace DeckForge.Core.Visual.Providers;

/// <summary>
/// How a set of providers is loaded, and what the host can check about them.
/// </summary>
/// <param name="GlyphExists">
/// Whether a rail glyph is one the icon library defines. Null skips that one check, which is the correct
/// behaviour for a consumer with no icon library and the wrong behaviour for the App — the reason it is
/// passed in rather than hard-coded is that <c>WPF-UI</c> may not be referenced from Core, and hard-coding
/// nothing would have left providers without a guarantee the built-in rows have.
/// </param>
/// <param name="DisabledProviderIds">
/// Providers the user has switched off. A disabled provider contributes nothing: no blocks, no categories,
/// and its id still counts as a namespace so a document that uses one reports a missing provider rather than
/// an unknown kind.
/// </param>
public sealed record VisualBlockProviderOptions(
    Func<string, bool>? GlyphExists = null,
    IReadOnlySet<string>? DisabledProviderIds = null);

/// <summary>
/// Every block a document may name: the built-in catalogue, plus whatever the loaded providers contribute.
/// </summary>
/// <remarks>
/// <para>
/// The registry is a <em>superset</em>, never a replacement. With no providers loaded it holds exactly
/// <see cref="BlockCatalog.All"/> and <see cref="BlockCatalog.Categories"/> — the same instances, in the
/// same order — which is what "the default catalogue stays exactly as it is" has to mean if it is to be a
/// property of the code rather than a promise. Anything else would make the palette depend on whether an
/// extension happened to be enabled, which is the kind of difference a user reports as "the blocks moved".
/// </para>
/// <para>
/// <strong>Contributed blocks go through the same rules the built-ins go through.</strong>
/// <see cref="BlockProviderRules"/> is not advisory for them: a kind that collides, or is not namespaced by
/// its provider, is refused outright and never reaches <see cref="Blocks"/>. Everything else is registered
/// with its findings attached, so a palette row with a hostile colour or a label naming a slot that does not
/// exist is visible to the App rather than silently drawn.
/// </para>
/// <para>
/// <strong>Nothing here loads an assembly.</strong> The container hands over
/// <c>IEnumerable&lt;IVisualBlockProvider&gt;</c> and the registry decides what they may contribute;
/// <see cref="VisualBlockProviderLoader"/> is the separate step that finds the types, for the same reason
/// <see cref="Extensions.ExtensionService"/> owns discovery and this owns policy: a test can hand over a
/// provider written five minutes ago without producing an assembly first.
/// </para>
/// </remarks>
public sealed class VisualBlockRegistry
{
    private readonly Dictionary<string, IVisualBlockProvider> _owners;

    private VisualBlockRegistry(
        IReadOnlyList<IVisualBlockProvider> providers,
        IReadOnlyList<BlockCategoryDescriptor> categories,
        IReadOnlyList<BlockDescriptor> blocks,
        IReadOnlyList<BlockProviderFinding> findings)
    {
        Providers = providers;
        Categories = categories;
        Blocks = blocks;
        Findings = findings;

        _owners = new Dictionary<string, IVisualBlockProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            foreach (var block in provider.Blocks)
            {
                _owners[block.Kind] = provider;
            }
        }
    }

    /// <summary>The providers whose blocks are registered, in rail order.</summary>
    public IReadOnlyList<IVisualBlockProvider> Providers { get; }

    /// <summary>The built-in categories, then the providers', in the order the rail shows them.</summary>
    public IReadOnlyList<BlockCategoryDescriptor> Categories { get; }

    /// <summary>The built-in rows, then the contributed ones, in palette order.</summary>
    public IReadOnlyList<BlockDescriptor> Blocks { get; }

    /// <summary>Everything the rules found, whether or not it cost a block its place in the palette.</summary>
    public IReadOnlyList<BlockProviderFinding> Findings { get; }

    /// <summary>The built-in catalogue and nothing else: what a DeckForge with no extensions enabled has.</summary>
    public static VisualBlockRegistry BuiltIn { get; } = new(
        [],
        BlockCatalog.Categories,
        BlockCatalog.All,
        []);

    /// <summary>
    /// Loads a set of providers into the built-in catalogue.
    /// </summary>
    /// <param name="providers">
    /// Whatever the container resolved, or whatever a test built. A provider that throws while declaring
    /// itself is contained here rather than allowed to escape.
    /// </param>
    /// <param name="options">The glyph check, and the ids the user has switched off.</param>
    /// <remarks>
    /// Providers are ordered by <see cref="IVisualBlockProvider.Order"/> and then by id, so the palette two
    /// providers build together is the same whichever order the container enumerated them in — a container
    /// gives no ordering promise, and a rail whose order changed when an unrelated service was registered
    /// would be a bug report about something nobody touched.
    /// </remarks>
    public static VisualBlockRegistry Load(
        IEnumerable<IVisualBlockProvider>? providers,
        VisualBlockProviderOptions? options = null)
    {
        options ??= new VisualBlockProviderOptions();

        var enabled = new List<IVisualBlockProvider>();
        var findings = new List<BlockProviderFinding>();

        foreach (var provider in providers ?? [])
        {
            if (provider is null)
            {
                continue;
            }

            if (options.DisabledProviderIds?.Contains(provider.Id) is true)
            {
                continue;
            }

            if (!WellFormed(provider))
            {
                // A provider whose Id or its declarations throw is third-party code that just failed in a
                // way this cannot describe. Recording it and carrying on is the position ExtensionService
                // takes, for the same reason: one bad extension must not stop DeckForge from starting.
                findings.Add(new BlockProviderFinding(
                    provider.GetType().Name,
                    provider.GetType().Name,
                    "The provider threw while declaring itself, so none of its blocks were registered.",
                    IsRefusal: true));
                continue;
            }

            enabled.Add(provider);
        }

        var categories = new List<BlockCategoryDescriptor>(BlockCatalog.Categories);
        var blocks = new List<BlockDescriptor>(BlockCatalog.All);

        // Grown as each provider is accepted, rather than seeded with every provider's rows up front. Seeding
        // with all of them made every provider collide with itself - its own first block looked taken -
        // and the registry then added the block anyway, so the finding and the palette disagreed.
        var takenKinds = new HashSet<string>(BlockCatalog.All.Select(row => row.Kind), StringComparer.Ordinal);
        var takenCategories = BlockCatalog.Categories.Select(row => row.Category).ToHashSet();
        var ordered = Ordered(enabled).ToList();

        foreach (var provider in ordered)
        {
            findings.AddRange(BlockProviderRules.Check(
                provider.Id,
                provider.Categories,
                provider.Blocks,
                takenKinds,
                takenCategories,
                options.GlyphExists));

            foreach (var category in provider.Categories.Where(category => takenCategories.Add(category.Category)))
            {
                categories.Add(category);
            }

            foreach (var block in provider.Blocks.Where(block => takenKinds.Add(block.Kind)))
            {
                blocks.Add(block);
            }
        }

        return new VisualBlockRegistry(ordered, categories, blocks, findings);
    }

    /// <summary>
    /// The providers in the order the rail shows them: by <see cref="IVisualBlockProvider.Order"/>, then by
    /// id.
    /// </summary>
    /// <remarks>
    /// Sorted in one place and called twice rather than written twice, because the two uses have to agree:
    /// the findings are reported against the palette that was actually built, and an ordering that differed
    /// between the check and the build would report a finding about a block in a rail that does not exist.
    /// </remarks>
    private static IEnumerable<IVisualBlockProvider> Ordered(IEnumerable<IVisualBlockProvider> providers) =>
        providers.OrderBy(provider => provider.Order).ThenBy(provider => provider.Id, StringComparer.Ordinal);

    /// <summary>
    /// The descriptor for a kind, from the catalogue or from a provider.
    /// </summary>
    /// <remarks>
    /// The registry's answer wins over <see cref="BlockCatalog.Find"/>'s for a contributed kind, because the
    /// catalogue does not have one and the registry does. A built-in kind resolves to the catalogue's own
    /// instance, not a copy, because the canvas caches descriptors by reference.
    /// </remarks>
    public BlockDescriptor? Find(string? kind) =>
        kind is null ? null : Blocks.FirstOrDefault(row => string.Equals(row.Kind, kind, StringComparison.Ordinal));

    /// <summary>Whether anything in this registry knows the kind.</summary>
    public bool Knows(string? kind) => Find(kind) is not null;

    /// <summary>The provider that contributed a kind, or null when the catalogue has it.</summary>
    public IVisualBlockProvider? OwnerOf(string? kind) =>
        kind is not null && _owners.TryGetValue(kind, out var provider) ? provider : null;

    /// <summary>The built-in rows of one category, plus any a provider contributed to it.</summary>
    public IReadOnlyList<BlockDescriptor> InCategory(BlockCategory category) =>
        [.. Blocks.Where(block => block.Category == category)];

    /// <summary>
    /// The contributed blocks a document names but this registry cannot supply.
    /// </summary>
    /// <param name="project">The document to check.</param>
    /// <remarks>
    /// <para>
    /// <c>vis-provider-missing</c>, from Appendix F: "a block contributed by an extension that is not
    /// loaded", hinted "Enable &lt;extension&gt; on the Extensions page." The provider id is the kind's
    /// first segment, which is why ids are namespaced — the diagnostic can name the thing to enable without
    /// anybody keeping a list of which provider supplied which block.
    /// </para>
    /// <para>
    /// Only kinds with a dot are reported. An unknown undotted kind is a kind this build does not know at
    /// all, which is a different finding with a different answer, and calling it a missing provider would
    /// send the user to the Extensions page for a block that was never anybody's.
    /// </para>
    /// </remarks>
    public IReadOnlyList<VisualDiagnostic> MissingProviders(VisualProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var findings = new List<VisualDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in project.Blocks())
        {
            if (Knows(block.Kind) || !reported.Add(block.Kind))
            {
                continue;
            }

            var provider = ProviderFrom(block.Kind);
            if (provider is null)
            {
                continue;
            }

            findings.Add(new VisualDiagnostic(
                "vis-provider-missing",
                VisualSeverity.Error,
                block.Id,
                $"\"{block.Kind}\" comes from the extension {provider}, which is not loaded.",
                $"Enable {provider} on the Extensions page, or the block cannot be emitted."));
        }

        return findings;
    }

    /// <summary>
    /// The provider id a contributed kind names, when the kind looks like one.
    /// </summary>
    /// <remarks>
    /// Read from the id rather than from a table of known providers, because the point is to report a block
    /// whose provider is *absent* — a table could only name the ones that are present.
    /// </remarks>
    private static string? ProviderFrom(string kind)
    {
        var dot = kind.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? kind[..dot] : null;
    }

    private static bool WellFormed(IVisualBlockProvider provider)
    {
        try
        {
            return provider.Id.Trim().Length > 0
                && provider.Categories is not null
                && provider.Blocks is not null;
        }
        catch (Exception error) when (error is not StackOverflowException)
        {
            return false;
        }
    }
}