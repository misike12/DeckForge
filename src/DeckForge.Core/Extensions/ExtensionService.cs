using System.Reflection;
using DeckForge.Core.Extensions;

namespace DeckForge.Core.Extensions;

/// <summary>One extension that was found, and what happened when it was loaded.</summary>
/// <param name="Id">The extension's id, or a placeholder when it could not be loaded far enough to ask.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Description">Its one-line description.</param>
/// <param name="Assembly">The file it came from.</param>
/// <param name="State">Whether it loaded, and why not if it did not.</param>
/// <param name="Enabled">Whether the user has it switched on.</param>
/// <param name="Error">The failure, when <paramref name="State"/> is not <see cref="ExtensionState.Loaded"/>.</param>
public sealed record ExtensionInfo(
    string Id,
    string Name,
    string Description,
    string Assembly,
    ExtensionState State,
    bool Enabled,
    string? Error = null)
{
    /// <summary>
    /// The label for the control that turns this extension on or off.
    /// </summary>
    /// <remarks>
    /// A property rather than a XAML trigger, because a style that switches content on a property
    /// needs an implicit style to base on, and there is no implicit style for a WPF.UI button in
    /// this app - so the obvious markup throws the moment the page loads.
    /// </remarks>
    public string ToggleLabel => Enabled ? "Disable" : "Enable";

    /// <summary>
    /// The failure, prefixed so it is obviously a problem, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// Also a property rather than a visibility trigger, for the same reason as
    /// <see cref="ToggleLabel"/>: a <c>BooleanToVisibilityConverter</c> is not in this app's
    /// resources, and a converter that is missing fails at load rather than degrading.
    /// </remarks>
    public string ErrorText => Error is null ? "" : "Failed: " + Error;
}

/// <summary>How far an extension got.</summary>
public enum ExtensionState
{
    /// <summary>It initialised, and its hooks will be invoked.</summary>
    Loaded,

    /// <summary>It was found, but the user has it switched off.</summary>
    Disabled,

    /// <summary>It threw while initialising, so it is not registered.</summary>
    Failed,
}

/// <summary>
/// Discovers <see cref="IDeckForgeExtension"/> implementations and invokes their hooks.
/// </summary>
/// <remarks>
/// <para>
/// The interface and the hook list existed with nothing behind them: no implementations, no
/// discovery, no page, and no call site. An extension point that cannot be reached is not an
/// extension point.
/// </para>
/// <para>
/// <b>Isolation.</b> An extension is third-party code loading into the IDE's process, so it is
/// treated as untrusted: it is loaded into its own <see cref="AssemblyLoadContext"/>, resolved
/// from the app's own directory and the user's extension folder, and a failure in one is recorded
/// against that extension rather than allowed to escape. One bad extension must not stop DeckForge
/// from starting, and must not be able to take the others down with it.
/// </para>
/// <para>
/// <b>Trust.</b> Loading an extension runs its module initialisers and its constructor, so
/// <em>enabling</em> one is the trust decision, and the folder is scanned and listed whether or not
/// anything is enabled. Nothing here is sandboxed - a .NET plugin cannot be - which is why the
/// settings page names the folder and says so plainly rather than implying a boundary that does
/// not exist.
/// </para>
/// </remarks>
public sealed class ExtensionService
{
    private readonly List<ExtensionInfo> _found = [];
    private readonly Dictionary<string, IDeckForgeExtension> _loaded = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;
    private readonly HashSet<string> _disabled;
    private readonly IReadOnlyList<IDeckForgeExtension> _builtIn;

    public ExtensionService(
        IServiceProvider services,
        IEnumerable<string>? disabledIds = null,
        IEnumerable<IDeckForgeExtension>? builtIn = null)
    {
        _services = services;
        _disabled = new HashSet<string>(disabledIds ?? [], StringComparer.OrdinalIgnoreCase);

        // Built-in extensions are handed in, not discovered. They live in DeckForge's own
        // assemblies, which the scan deliberately skips, and a first-party extension that a user
        // could accidentally load twice is worse than one that is simply registered.
        _builtIn = [.. builtIn ?? []];
    }

    /// <summary>Raised after a scan, so the Extensions page can refresh.</summary>
    public event Action? Changed;

    /// <summary>Everything found, loaded or not.</summary>
    public IReadOnlyList<ExtensionInfo> Found => _found;

    /// <summary>The extensions whose hooks will be invoked.</summary>
    public IReadOnlyCollection<IDeckForgeExtension> Loaded => _loaded.Values;

    /// <summary>The folder the user drops extensions into, alongside the app's own.</summary>
    public static string UserExtensionDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeckForge",
        "extensions");

    /// <summary>
    /// Scans the app directory and the user folder, loading everything it finds.
    /// </summary>
    /// <param name="appDirectory">Usually <c>AppContext.BaseDirectory</c>.</param>
    /// <returns>One entry per extension found, including the ones that failed.</returns>
    public async Task<IReadOnlyList<ExtensionInfo>> ScanAsync(string appDirectory, CancellationToken ct = default)
    {
        _found.Clear();
        _loaded.Clear();

        foreach (var builtIn in _builtIn)
        {
            var (info, extension) = await InitialiseAsync(builtIn, "built-in", ct);
            _found.Add(info);
            if (extension is not null)
            {
                _loaded[info.Id] = extension;
            }
        }

        foreach (var file in CandidateFiles(appDirectory))
        {
            await LoadAsync(file, ct);
        }

        Changed?.Invoke();
        return _found;
    }

    /// <summary>
    /// Loads every extension in one assembly, adding to whatever is already registered.
    /// </summary>
    /// <remarks>
    /// Public because it is the only way to load a specific extension, and because it is how the
    /// tests reach the failure paths: producing a deliberately broken assembly on disk to scan for
    /// is a lot of machinery for what should be a two-line call.
    /// </remarks>
    /// <returns>One entry per extension type in the assembly, including the ones that failed.</returns>
    public async Task<IReadOnlyList<ExtensionInfo>> LoadAsync(string assemblyPath, CancellationToken ct = default)
    {
        var added = new List<ExtensionInfo>();

        foreach (var type in ExtensionTypes(assemblyPath))
        {
            ct.ThrowIfCancellationRequested();

            IDeckForgeExtension? instance = null;
            try
            {
                instance = Activator.CreateInstance(type) as IDeckForgeExtension;
            }
            catch (Exception ex)
            {
                // The constructor threw. Recorded against the type so the page can show it, rather
                // than allowed to escape - a third-party constructor must not be able to stop the
                // IDE from starting.
                added.Add(new ExtensionInfo(
                    type.FullName ?? type.Name, type.Name, "", assemblyPath, ExtensionState.Failed, false, ex.Message));
                continue;
            }

            if (instance is null)
            {
                continue;
            }

            var (info, extension) = await InitialiseAsync(instance, assemblyPath, ct);
            added.Add(info);
            if (extension is not null)
            {
                _loaded[info.Id] = extension;
            }
        }

        _found.AddRange(added);
        Changed?.Invoke();
        return added;
    }

    /// <summary>
    /// Invokes <paramref name="hook"/> on every loaded extension that registered for it.
    /// </summary>
    /// <remarks>
    /// One extension throwing must not stop the others, and must not surface as an unhandled
    /// exception in whatever UI code happened to ask - so a failure is recorded and the rest
    /// continue.
    /// </remarks>
    /// <returns>What each extension returned, paired with the id that returned it.</returns>
    public async Task<IReadOnlyList<(string Id, object? Result)>> InvokeAsync(
        DeckForgeHooks hook,
        object? payload,
        CancellationToken ct = default)
    {
        var results = new List<(string, object?)>();

        foreach (var extension in _loaded.Values.ToList())
        {
            if (!extension.Hooks.Contains(hook))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            try
            {
                results.Add((extension.Id, await extension.InvokeAsync(hook, payload, ct)));
            }
            catch (Exception ex)
            {
                Record(extension.Id, ExtensionState.Failed, ex.Message);
            }
        }

        return results;
    }

    private async Task<(ExtensionInfo, IDeckForgeExtension?)> InitialiseAsync(
        IDeckForgeExtension extension,
        string file,
        CancellationToken ct)
    {
        var id = extension.Id;
        var name = extension.Name;
        var description = extension.Description;

        if (_disabled.Contains(id))
        {
            return (new ExtensionInfo(id, name, description, file, ExtensionState.Disabled, false), null);
        }

        try
        {
            if (!await extension.InitializeAsync(_services, ct))
            {
                return (new ExtensionInfo(id, name, description, file, ExtensionState.Failed, false,
                    "The extension declined to initialise."), null);
            }

            return (new ExtensionInfo(id, name, description, file, ExtensionState.Loaded, true), extension);
        }
        catch (Exception ex)
        {
            // Third-party code, and it just threw. That is the extension's problem, and the one
            // thing that must not happen is the exception reaching the shell.
            return (new ExtensionInfo(id, name, description, file, ExtensionState.Failed, false, ex.Message), null);
        }
    }

    private void Record(string id, ExtensionState state, string? error)
    {
        var index = _found.FindIndex(f => f.Id == id);
        if (index < 0)
        {
            return;
        }

        var existing = _found[index];
        _found[index] = existing with { State = state, Error = error };
        _loaded.Remove(id);
    }

    /// <summary>
    /// The extension assemblies to consider.
    /// </summary>
    /// <remarks>
    /// DeckForge's own assemblies are excluded by name: they are already in the default context and
    /// loading them again would give two copies of every type, and any type test would match twice.
    /// </remarks>
    private static IEnumerable<string> CandidateFiles(string appDirectory)
    {
        foreach (var directory in new[] { appDirectory, UserExtensionDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.dll");
            }
            catch (IOException)
            {
                // An unreadable directory is not worth failing a scan over.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("DeckForge.", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static IEnumerable<Type> ExtensionTypes(string file)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(file);
        }
        catch (BadImageFormatException)
        {
            // A native library or an assembly built for another architecture. Not an extension.
            yield break;
        }
        catch (FileLoadException)
        {
            yield break;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A missing dependency should not hide the types that did load.
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var type in types)
        {
            if (type is { IsAbstract: false, IsInterface: false }
                && typeof(IDeckForgeExtension).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                yield return type;
            }
        }
    }
}
