using DeckForge.Core.Extensions;
using DeckForge.Core.Extensions.BuiltIn;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The extension loader.
/// </summary>
/// <remarks>
/// <para>
/// The interface, the eight hooks and the documented "Extensions settings page" all existed with
/// nothing behind them: no implementations, no discovery, no call site. These tests pin the parts
/// that make it an extension point rather than a declaration - that an extension is found, that its
/// hooks fire, and that a broken one cannot take the application down with it.
/// </para>
/// <para>
/// The isolation cases matter most. An extension is third-party code loading into the IDE, so
/// "one bad extension must not stop DeckForge starting" is a correctness property, not a nicety.
/// They are exercised through the real load path - the test assembly is loaded as if it were an
/// extension folder - so nothing here is a stand-in for the behaviour.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ExtensionServiceTests
{
    /// <summary>
    /// This assembly, loaded through the extension path. The test extensions inside it are found the
    /// same way a real one would be.
    /// </summary>
    private static string ThisAssembly => typeof(ExtensionServiceTests).Assembly.Location;

    private ExtensionService Service(
        IEnumerable<string>? disabled = null,
        IEnumerable<IDeckForgeExtension>? builtIn = null) =>
        new(new ServiceCollection().BuildServiceProvider(), disabled, builtIn);

    [Test]
    public async Task An_extension_is_found_and_loaded()
    {
        var found = await Service().LoadAsync(ThisAssembly);

        Assert.That(found.Select(f => f.Id), Does.Contain(nameof(GoodExtension)));
        Assert.That(found.Single(f => f.Id == nameof(GoodExtension)).State, Is.EqualTo(ExtensionState.Loaded));
    }

    [Test]
    public async Task A_registered_hook_is_invoked_and_returns_a_payload()
    {
        var service = Service();
        await service.LoadAsync(ThisAssembly);

        var results = await service.InvokeAsync(DeckForgeHooks.Commands, null);

        Assert.That(results, Is.Not.Empty, "A hook nothing answers is indistinguishable from a broken hook.");
        Assert.That(results.Select(r => r.Result), Does.Contain(nameof(GoodExtension)));
    }

    [Test]
    public async Task A_hook_an_extension_did_not_register_is_never_invoked()
    {
        var service = Service();
        await service.LoadAsync(ThisAssembly);

        // GoodExtension registers Commands only. Asking for the toolbar must reach nobody, or every
        // extension ends up coupled to every hook.
        var results = await service.InvokeAsync(DeckForgeHooks.Toolbar, null);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task A_disabled_extension_is_listed_but_not_loaded()
    {
        var found = await Service([nameof(GoodExtension)]).LoadAsync(ThisAssembly);

        var info = found.Single(f => f.Id == nameof(GoodExtension));
        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(ExtensionState.Disabled));
            Assert.That(info.Enabled, Is.False);
        });
    }

    [Test]
    public async Task A_disabled_extensions_hook_is_not_invoked()
    {
        var service = Service([nameof(GoodExtension)]);
        await service.LoadAsync(ThisAssembly);

        var results = await service.InvokeAsync(DeckForgeHooks.Commands, null);

        Assert.That(results, Is.Empty, "A disabled extension must not receive hooks.");
    }

    [Test]
    public async Task An_extension_that_throws_while_initialising_is_recorded_and_the_rest_still_load()
    {
        var found = await Service().LoadAsync(ThisAssembly);

        var failing = found.Single(f => f.Id == nameof(ThrowingExtension));
        Assert.Multiple(() =>
        {
            Assert.That(failing.State, Is.EqualTo(ExtensionState.Failed));
            Assert.That(failing.Error, Does.Contain("deliberate"), "A failure with no reason cannot be acted on.");
            Assert.That(found.Single(f => f.Id == nameof(GoodExtension)).State, Is.EqualTo(ExtensionState.Loaded),
                "One broken extension must not stop the others from loading.");
        });
    }

    [Test]
    public async Task A_hook_that_throws_is_contained_and_the_others_still_run()
    {
        var service = Service();
        await service.LoadAsync(ThisAssembly);

        // GoodExtension and ThrowingOnInvokeExtension both register Commands. The second throws.
        var results = await service.InvokeAsync(DeckForgeHooks.Commands, null);

        Assert.That(results.Select(r => r.Id), Does.Contain(nameof(GoodExtension)));
    }

    [Test]
    public async Task An_extension_that_declines_to_initialise_is_not_loaded_but_is_reported()
    {
        var found = await Service().LoadAsync(ThisAssembly);

        var declining = found.Single(f => f.Id == nameof(DecliningExtension));
        Assert.Multiple(() =>
        {
            Assert.That(declining.State, Is.EqualTo(ExtensionState.Failed));
            Assert.That(declining.Error, Does.Contain("declined").IgnoreCase);
        });
    }

    [Test]
    public async Task A_built_in_extension_is_registered_without_being_discovered()
    {
        // It lives in DeckForge.Core, which the scan skips on purpose, so it has to be handed in.
        var service = Service(builtIn: [new ExtensionDiagnosticsExtension()]);

        var found = await service.ScanAsync(Path.GetTempPath());

        Assert.That(found.Select(f => f.Id), Does.Contain("deckforge.extension-diagnostics"));
    }

    [Test]
    public void DeckForges_own_assemblies_are_not_treated_as_extensions()
    {
        // Loading them again would give two copies of every type, and any type test would match twice.
        var found = Service().ScanAsync(AppContext.BaseDirectory).GetAwaiter().GetResult();

        Assert.That(
            found.Select(f => Path.GetFileName(f.Assembly)),
            Has.None.StartsWith("DeckForge."),
            "DeckForge's own assemblies must be excluded from the scan.");
    }

    [Test]
    public void Scanning_a_directory_that_does_not_exist_is_not_an_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-extensions-" + Guid.NewGuid().ToString("N")[..8]);

        var found = Service().ScanAsync(missing).GetAwaiter().GetResult();

        Assert.That(found, Is.Not.Null);
    }

    [Test]
    public void Loading_a_file_that_is_not_a_managed_assembly_is_skipped_rather_than_fatal()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-an-assembly-" + Guid.NewGuid().ToString("N")[..8] + ".dll");
        File.WriteAllText(path, "this is not a PE image");

        try
        {
            var found = Service().LoadAsync(path).GetAwaiter().GetResult();
            Assert.That(found, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Switching_an_extension_off_and_on_takes_effect_without_a_restart()
    {
        // The constructor copied the disabled list, so editing settings left the service reading the
        // old set: every toggle did nothing until the app restarted, while the page claimed a rescan
        // would load it.
        var service = Service();
        await service.LoadAsync(ThisAssembly);

        service.SetDisabled(nameof(GoodExtension), disabled: true);
        Assert.That(service.DisabledIds, Does.Contain(nameof(GoodExtension)));

        service.SetDisabled(nameof(GoodExtension), disabled: false);
        Assert.That(service.DisabledIds, Does.Not.Contain(nameof(GoodExtension)));

        // And the whole set can be replaced, which is what loading settings at start-up does.
        service.SetDisabled([nameof(GoodExtension)]);
        Assert.That(service.DisabledIds, Does.Contain(nameof(GoodExtension)));

        service.SetDisabled([]);
        Assert.That(service.DisabledIds, Is.Empty);
    }

    [Test]
    public async Task An_extension_is_not_listed_twice_when_both_scan_roots_are_the_same_folder()
    {
        // appDirectory and the user folder can be one path, and the same assembly was then added to
        // the list twice - so the page showed a second row that never picked up a status change.
        var service = Service();

        var found = await service.ScanAsync(ExtensionService.UserExtensionDirectory);

        var ids = found.Select(f => f.Id).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), string.Join(", ", ids));
    }

    [Test]
    public async Task Loading_the_same_assembly_twice_does_not_duplicate_the_list()
    {
        var service = Service();

        await service.LoadAsync(ThisAssembly);
        var foundBefore = service.Found.Count;
        var loadedBefore = service.Loaded.Count;
        await service.LoadAsync(ThisAssembly);

        Assert.Multiple(() =>
        {
            Assert.That(service.Found.Count, Is.EqualTo(foundBefore),
                "A second load added a phantom row for an extension already listed.");
            Assert.That(service.Loaded.Count, Is.EqualTo(loadedBefore),
                "A second load registered a second instance of an already-loaded extension.");
        });
    }

    [Test]
    public void The_user_extension_folder_is_the_documented_location()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(ExtensionService.UserExtensionDirectory), Is.True);
            Assert.That(ExtensionService.UserExtensionDirectory, Does.Contain("DeckForge"));
            Assert.That(ExtensionService.UserExtensionDirectory, Does.EndWith("extensions"),
                "The path is shown to the user, so it has to be the one the docs name.");
        });
    }

    // The extensions under test. Nested so they cannot collide with a real one in the same assembly.

    private sealed class GoodExtension : IDeckForgeExtension
    {
        public string Id => nameof(GoodExtension);

        public string Name => "Good";

        public string Description => "Answers one hook.";

        public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Commands];

        public Task<object?> InvokeAsync(DeckForgeHooks hook, object? payload, CancellationToken ct = default) =>
            Task.FromResult<object?>(Id);
    }

    private sealed class ThrowingExtension : IDeckForgeExtension
    {
        public string Id => nameof(ThrowingExtension);

        public string Name => "Throwing";

        public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Commands];

        public Task<bool> InitializeAsync(IServiceProvider services, CancellationToken ct = default) =>
            throw new InvalidOperationException("deliberate failure");
    }

    [Test]
    public async Task An_extension_built_outside_the_app_is_discovered_and_answered()
    {
        // The rest of this fixture loads this test assembly, so every case is an extension that was
        // compiled next to the app and can see everything DeckForge can see. That cannot tell a third
        // party whether the contract is satisfiable from outside - and it cannot catch a change that
        // makes the interface reachable only from inside DeckForge.
        //
        // So this loads the real sample extension: its own project, under extensions/, referencing
        // only DeckForge.Core, and handed to the loader as a file on disk.
        var assembly = typeof(DeckForge.SampleExtension.SampleDiagnosticsExtension).Assembly.Location;
        var service = Service();

        var found = await service.LoadAsync(assembly);
        var results = await service.InvokeAsync(DeckForgeHooks.Diagnostics, null);

        Assert.Multiple(() =>
        {
            Assert.That(found.Select(f => f.Id), Does.Contain("deckforge.sample-diagnostics"),
                "An extension built as its own assembly was not discovered.");
            Assert.That(found.Single(f => f.Id == "deckforge.sample-diagnostics").State,
                Is.EqualTo(ExtensionState.Loaded));

            // The hook's answer came back as the extension's own type, which is the point: the app
            // carries a value it has never heard of across the boundary without throwing.
            Assert.That(
                results.Select(r => r.Result).OfType<DeckForge.SampleExtension.SampleReport>().Select(r => r.Summary),
                Has.Exactly(1).Contains("Sample extension running on"));
        });
    }


    private sealed class ThrowingOnInvokeExtension : IDeckForgeExtension
    {
        public string Id => nameof(ThrowingOnInvokeExtension);

        public string Name => "Throwing on invoke";

        public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Commands];

        public Task<object?> InvokeAsync(DeckForgeHooks hook, object? payload, CancellationToken ct = default) =>
            throw new InvalidOperationException("deliberate failure on invoke");
    }

    private sealed class DecliningExtension : IDeckForgeExtension
    {
        public string Id => nameof(DecliningExtension);

        public string Name => "Declining";

        public IReadOnlyList<DeckForgeHooks> Hooks { get; } = [DeckForgeHooks.Commands];

        public Task<bool> InitializeAsync(IServiceProvider services, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
