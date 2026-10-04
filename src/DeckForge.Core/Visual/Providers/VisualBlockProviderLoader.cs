using System.Reflection;

namespace DeckForge.Core.Visual.Providers;

/// <summary>
/// Finds <see cref="IVisualBlockProvider"/> implementations in extension assemblies.
/// </summary>
/// <remarks>
/// <para>
/// The same seam as <see cref="Extensions.ExtensionService"/>, on purpose: the folder is the same, the
/// opt-in is the same, and the trust position is the one Part 23.3 states plainly — loading an extension
/// runs third-party code in this process with this process's permissions, and a .NET assembly cannot be
/// sandboxed. A block provider is a <em>higher</em>-trust contribution than a page, because its emitter
/// writes into a user's source tree.
/// </para>
/// <para>
/// <strong>Separate from the registry on purpose.</strong> The registry decides what a provider may
/// contribute; this finds the types. Splitting them is what lets a test hand the registry a provider written
/// five minutes ago without producing an assembly to load it from, and it keeps the policy testable without
/// a file system.
/// </para>
/// <para>
/// DeckForge's own assemblies are skipped by name, exactly as <c>ExtensionService</c> does: they are
/// already in the default context, loading them again would give two copies of every type, and a
/// <c>GetTypes()</c> would then match twice.
/// </para>
/// </remarks>
public static class VisualBlockProviderLoader
{
    /// <summary>The folder a user drops extensions into, alongside the app's own.</summary>
    /// <remarks>
    /// The same place <c>ExtensionService.UserExtensionDirectory</c> names, and the same value rather than a
    /// second definition of it: a user who found one folder documented and did not find the other would be
    /// right to be annoyed, and only one of the two would be looked in.
    /// </remarks>
    public static string UserExtensionDirectory => Extensions.ExtensionService.UserExtensionDirectory;

    /// <summary>
    /// Every provider in an assembly, whether or not its descriptors survive the rules.
    /// </summary>
    /// <remarks>
    /// Discovery does not validate — <see cref="VisualBlockRegistry.Load"/> does, and a finding it produced
    /// is a thing the App can show. Doing both here would mean a malformed provider vanished with no
    /// explanation, which is the failure mode the findings list exists to prevent.
    /// </remarks>
    public static IReadOnlyList<IVisualBlockProvider> FromAssembly(string assemblyPath)
    {
        var found = new List<IVisualBlockProvider>();

        foreach (var type in ProviderTypes(assemblyPath))
        {
            try
            {
                if (Activator.CreateInstance(type) is IVisualBlockProvider provider)
                {
                    found.Add(provider);
                }
            }
            catch (Exception error) when (error is not StackOverflowException)
            {
                // A constructor that threw is this provider's problem. Skipped rather than allowed to
                // escape, because a third-party type must not be able to stop DeckForge from starting.
            }
        }

        return found;
    }

    /// <summary>
    /// Every provider in a directory, in the order the files are named.
    /// </summary>
    /// <param name="directory">Usually <c>AppContext.BaseDirectory</c>.</param>
    /// <remarks>
    /// Sorted by file name so a scan is reproducible. An unreadable directory yields nothing rather than
    /// throwing: a scan is a best-effort look, and failing the whole page over one permission would be a
    /// worse answer than a palette with no contributed blocks in it.
    /// </remarks>
    public static IReadOnlyList<IVisualBlockProvider> FromDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.dll").OrderBy(file => file, StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<IVisualBlockProvider>();
        foreach (var file in files)
        {
            // DeckForge's own assemblies, skipped for the reason the class summary gives.
            if (Path.GetFileNameWithoutExtension(file).StartsWith("DeckForge.", StringComparison.Ordinal))
            {
                continue;
            }

            found.AddRange(FromAssembly(file));
        }

        return found;
    }

    private static IEnumerable<Type> ProviderTypes(string assemblyPath)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception error) when (error is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            // A native library, or a file that is not there. Not a provider.
            yield break;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            // A missing dependency should not hide the types that did load.
            types = [.. error.Types.Where(type => type is not null).Cast<Type>()];
        }
        catch (Exception error) when (error is not StackOverflowException)
        {
            yield break;
        }

        foreach (var type in types)
        {
            if (type is { IsAbstract: false, IsInterface: false }
                && typeof(IVisualBlockProvider).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                yield return type;
            }
        }
    }
}