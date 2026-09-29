using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace DeckForge.SdkInventory;

/// <summary>
/// Prints the public surface of a Macro Deck assembly as markdown.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the block catalog in <c>visual.md</c> is only trustworthy if every block's
/// mapping was read off the real assembly rather than remembered. The SDK pin moves
/// (<c>MacroDeckSdkInfo.DefaultVersion</c>, bumped on every Macro Deck release), so the inventory has
/// to be cheap to regenerate rather than a one-off transcription that rots.
/// </para>
/// <para>
/// It loads the assembly by path with <see cref="Assembly.LoadFrom(string)"/> and reflects over it,
/// which means this tool has no compile-time dependency on the SDK at all. That is the point: it can
/// dump any version of the assembly, including one this build has never referenced.
/// </para>
/// <para>
/// Usage: <c>SdkInventory &lt;assembly-path&gt; [namespace-prefix] [--members-only-for &lt;substring&gt;]</c>
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(
                "usage: SdkInventory <assembly-path> [namespace-prefix] [--members-only-for <substring>]");
            return 2;
        }

        var path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"not found: {path}");
            return 2;
        }

        var prefix = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        var memberFilter = IndexOf(args, "--members-only-for") is var index && index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : null;

        AssemblyLoadContext.Default.Resolving += (_, name) => Probe(name, path);

        var assembly = Assembly.LoadFrom(path);
        var types = ExportedTypes(assembly)
            .Where(type => prefix is null
                || (type.Namespace ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(type => type.Namespace ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        var output = new StringBuilder();
        output.AppendLine($"# {assembly.GetName().Name} {assembly.GetName().Version}");
        output.AppendLine();
        output.AppendLine($"Assembly: `{path}`");
        output.AppendLine($"Exported types: {types.Count}");
        output.AppendLine();

        foreach (var group in types.GroupBy(type => type.Namespace ?? "(global)"))
        {
            output.AppendLine($"## {group.Key}");
            output.AppendLine();

            foreach (var type in group)
            {
                Describe(output, type, memberFilter);
            }
        }

        Console.Out.Write(output.ToString());
        return 0;
    }

    /// <summary>
    /// Finds a dependency of the assembly under inspection in the NuGet cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflecting over an assembly loaded by path does not load its dependencies, and the SDK's own
    /// types reference siblings (<c>MacroDeck.Localization</c>, <c>MacroDeck.Ui.Model</c>, ...) that
    /// are not beside it in the cache. Without a resolver, <see cref="Assembly.GetExportedTypes"/> over
    /// <c>MacroDeck.Sdk.dll</c> throws before returning a single type - which is exactly what happened
    /// the first time this tool was run.
    /// </para>
    /// <para>
    /// The cache layout is <c>&lt;root&gt;/&lt;package&gt;/&lt;version&gt;/lib/&lt;tfm&gt;/&lt;name&gt;.dll</c>, so the probe walks
    /// version directories in descending order and returns the newest match. Any directory supplied
    /// on the command line is probed first, so a caller with a local copy wins.
    /// </para>
    /// </remarks>
    private static Assembly? Probe(AssemblyName name, string targetPath)
    {
        if (name.Name is not { } fileName)
        {
            return null;
        }

        var beside = Path.Combine(Path.GetDirectoryName(targetPath) ?? ".", fileName + ".dll");
        if (File.Exists(beside))
        {
            return Assembly.LoadFrom(beside);
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var packageDirectory = Path.Combine(root, fileName.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory))
        {
            return null;
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(packageDirectory)
                     .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            var libDirectory = Path.Combine(versionDirectory, "lib");
            if (!Directory.Exists(libDirectory))
            {
                continue;
            }

            foreach (var frameworkDirectory in Directory.EnumerateDirectories(libDirectory))
            {
                var candidate = Path.Combine(frameworkDirectory, fileName + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The types that loaded, tolerating the ones that did not.
    /// </summary>
    /// <remarks>
    /// <see cref="Assembly.GetExportedTypes"/> throws outright when a single type's dependencies are
    /// missing, which is exactly what happens when an assembly is loaded without its full dependency
    /// closure - and a dump that fails entirely because of one unreachable type is useless. The
    /// partial list is reported instead, and the exception is surfaced on stderr so a gap is visible
    /// rather than silent.
    /// </remarks>
    private static IEnumerable<Type> ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = ex.Types.Where(type => type is not null).Select(type => type!).ToList();
            Console.Error.WriteLine(
                $"{ex.Types.Length - loaded.Count} of {ex.Types.Length} types could not be resolved; "
                + "the dump covers the rest.");
            return loaded;
        }
        catch (FileNotFoundException ex)
        {
            // A dependency the probe could not find. Reporting it and dumping nothing would make the
            // tool useless exactly when the SDK adds a new sibling assembly, so the failure is named.
            Console.Error.WriteLine($"a dependency could not be resolved: {ex.Message}");
            return [];
        }
    }

    private static int IndexOf(string[] args, string value)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Describe(StringBuilder output, Type type, string? memberFilter)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class"
            : "class";

        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && !type.IsEnum)
        {
            bases.Add(Simple(baseType));
        }

        var interfaces = type.GetInterfaces().Select(Simple).Distinct().OrderBy(name => name, StringComparer.Ordinal);
        bases.AddRange(interfaces);

        var suffix = bases.Count == 0 ? string.Empty : " : " + string.Join(", ", bases);
        output.AppendLine($"### {kind} {type.Name}{suffix}");
        output.AppendLine();

        if (type.IsEnum)
        {
            var values = string.Join(", ", Enum.GetNames(type));
            output.AppendLine($"Values: {values}");
            output.AppendLine();
            return;
        }

        var showMembers = memberFilter is null
            || type.Name.Contains(memberFilter, StringComparison.OrdinalIgnoreCase);

        if (!showMembers)
        {
            var count = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Count(member => member is MethodInfo { IsSpecialName: false } or PropertyInfo or EventInfo or FieldInfo);
            output.AppendLine($"{count} declared member(s) — pass --members-only-for {type.Name} to expand.");
            output.AppendLine();
            return;
        }

        foreach (var line in MemberLines(type))
        {
            output.AppendLine($"- {line}");
        }

        output.AppendLine();
    }

    private static IEnumerable<string> MemberLines(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Fields matter here: ActionErrorCodes is a static class of string constants, not the enum its
        // name suggests, and the nine error codes a failed action may report live in those constants.
        // A dump that skipped fields would have shown an empty type and the codes would have been
        // transcribed from memory - which is the exact failure this tool exists to prevent.
        foreach (var field in type.GetFields(flags).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var value = field.IsLiteral
                ? $" = {FormatConstant(field.GetRawConstantValue())}"
                : field.IsInitOnly ? " (readonly)" : string.Empty;
            yield return $"field `{Simple(field.FieldType)} {field.Name}{value}`";
        }

        foreach (var property in type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var accessors = (property.CanRead, property.CanWrite) switch
            {
                (true, true) => "get; set;",
                (true, false) => "get;",
                _ => "set;",
            };
            yield return $"property `{Simple(property.PropertyType)} {property.Name} {{ {accessors} }}`";
        }

        // Operators are kept. They are special-name methods, so a plain `!IsSpecialName` filter drops
        // them - and an implicit conversion operator is part of a public surface in the way that
        // matters most here: whether a raw string literal may be passed where a LocalizedText is
        // expected is decided entirely by an op_Implicit this tool would otherwise hide. Property
        // accessors and event add/remove are the special names that genuinely add nothing.
        foreach (var method in type.GetMethods(flags)
                     .Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal))
                     .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var parameters = string.Join(", ", Parameters(method));
            yield return $"method `{Simple(method.ReturnType)} {method.Name}({parameters})`";
        }

        foreach (var @event in type.GetEvents(flags).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            yield return $"event `{Simple(@event.EventHandlerType!)} {@event.Name}`";
        }

        static IEnumerable<string> Parameters(MethodInfo method) =>
            method.GetParameters().Select(p => $"{Simple(p.ParameterType)} {p.Name}");
    }

    /// <summary>
    /// A constant as readable text, without quotes around strings.
    /// </summary>
    /// <remarks>
    /// <see cref="object.ToString"/> on a <c>const string</c> yields the raw text, which is what a
    /// reader comparing this dump against generated code wants to see. <c>null</c> is reported as
    /// <c>&lt;null&gt;</c> so it is not mistaken for the four-character string "null".
    /// </remarks>
    private static string FormatConstant(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        char character => $"'{character}'",
        _ => value.ToString() ?? "<unprintable>",
    };

    /// <summary>A short, readable name for a type, including generic arguments.</summary>
    private static string Simple(Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            var args = string.Join(", ", type.GetGenericArguments().Select(Simple));
            return $"{name}<{args}>";
        }

        if (type.IsArray)
        {
            return $"{Simple(type.GetElementType()!)}[]";
        }

        return type.Name;
    }
}
