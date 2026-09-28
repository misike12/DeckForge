using System.Reflection;
using DeckForge.Core.Code;
using DeckForge.Core.Widgets;
using MacroDeck.Ui.Components;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// The widget node catalog, against the real Macro Deck UI assemblies.
/// </summary>
/// <remarks>
/// <para>
/// The catalog's 24 node types and 235 properties were transcribed by hand from
/// <c>MacroDeck.Ui</c> and nothing checked the transcription. The compile test that would have
/// caught a wrong name never compiled anything: it wrote the generated widget to the solution root,
/// outside every <c>.csproj</c>'s glob, and reported success on a build that had not seen the file.
/// </para>
/// <para>
/// So the errors were real and invisible at once. This fixes the blind spot: every property is
/// checked against the CLR type by reflection, and a name that is not settable fails here rather
/// than in a user's plugin.
/// </para>
/// </remarks>
[TestFixture]
public sealed class UiNodeCatalogTests
{
    private static readonly Assembly Ui = typeof(UiTextRun).Assembly;

    private static Type Resolve(string className) =>
        Ui.GetExportedTypes().FirstOrDefault(t => t.Name == className)
        ?? throw new AssertionException($"{className} does not exist in {Ui.GetName().Name}.");

    private static HashSet<string> Settable(Type type) => new(
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => p.Name),
        StringComparer.Ordinal);

    [Test]
    public void Every_node_type_exists_in_the_sdk()
    {
        var missing = UiNodeCatalog.All
            .Where(n => Ui.GetExportedTypes().All(t => t.Name != n.ClassName))
            .Select(n => $"{n.WireType} -> {n.ClassName}")
            .ToList();

        Assert.That(missing, Is.Empty, "The catalog names a class the SDK does not have: " + string.Join(", ", missing));
    }

    [Test]
    public void Every_declared_property_is_settable_on_the_type_it_is_declared_for()
    {
        // The catalog's Name is the wire name, as it appears in Macro Deck's profile documents; the
        // CLR property is its Pascal-case equivalent. Checking the wire name directly would report
        // all 235 as missing, which is how the transcription gap stayed invisible.
        var problems = new List<string>();

        foreach (var node in UiNodeCatalog.All)
        {
            var settable = Settable(Resolve(node.ClassName));
            foreach (var property in node.Properties)
            {
                var clr = CSharpCode.ToPascal(property.Name);
                if (!settable.Contains(clr))
                {
                    problems.Add($"{node.WireType} ({node.ClassName}): '{property.Name}' would emit '{clr}', which is not a property");
                }
            }
        }

        Assert.That(
            problems,
            Is.Empty,
            $"{problems.Count} catalog properties do not exist on the SDK type:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Take(30)));
    }

    [Test]
    public void Every_property_kind_matches_the_clr_property_type()
    {
        // The catalog's Kind decides which literal the generator emits. A property typed
        // `UiValue<bool>` declared as ValueNumber gets `Prop = 1`, which is a CS0029 - the generated
        // widget assigned an int to a bool-valued property. Checking the name alone cannot catch it.
        // The catalog has two numeric kinds, Number and ValueNumber, and the generator emits the
        // same literal for both - the difference is only how the SDK wraps the value. So the check
        // compares the class of literal, not the kind: flag, number, or text. Comparing Number with
        // ValueNumber would report every one of the 235 properties and say nothing.
        static string ExpectedLiteral(Type type)
        {
            if (type.Name is "UiSize" or "UiLength" or "UiPercent")
            {
                return "number";
            }

            var core = type.IsGenericType && type.Name.StartsWith("UiValue`", StringComparison.Ordinal)
                ? type.GetGenericArguments()[0]
                : type;

            return core.Name switch
            {
                "Boolean" => "flag",
                "Double" or "Single" or "Int32" or "Int64" => "number",
                "String" or "UiText" => "text",

                // Types the SDK builds from a string, so the catalog's Text kind is right for them.
                "UiBackgroundValue" => "text",

                // Rendered from a DSL call rather than a literal, which RenderProperty handles by
                // name. Reported as neither flag, number nor text so they are not second-guessed
                // here; the compile test is what proves they render.
                "UiResource" => "special",
                _ when core.IsGenericType && core.Name.StartsWith("IReadOnlyList", StringComparison.Ordinal)
                    => "special",
                _ => core.Name,
            };
        }

        static string ActualLiteral(UiPropertyKind kind) => kind switch
        {
            UiPropertyKind.Flag => "flag",
            UiPropertyKind.Number or UiPropertyKind.ValueNumber => "number",
            _ => "text",
        };

        var problems = new List<string>();

        foreach (var node in UiNodeCatalog.All)
        {
            var clr = Resolve(node.ClassName);
            foreach (var declared in node.Properties)
            {
                var property = clr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(p => p.Name == CSharpCode.ToPascal(declared.Name));
                if (property is null)
                {
                    continue; // reported by the name check
                }

                var expected = ExpectedLiteral(property.PropertyType);
                if (expected is "special")
                {
                    continue;
                }

                var actual = ActualLiteral(declared.Kind);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{node.WireType} ({node.ClassName}).{declared.Name} is {property.PropertyType.Name}, "
                        + $"so it takes a {expected} literal but the catalog declares {actual}");
                }
            }
        }

        Assert.That(
            problems,
            Is.Empty,
            $"{problems.Count} property kinds do not match the SDK type:" + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Take(30)));
    }

    [Test]
    public void Every_property_of_a_type_is_declared_exactly_once()
    {
        // The catalog composes its property lists from shared groups, so a group included twice
        // produces a duplicate assignment in the object initializer - which is a compile error.
        var duplicates = UiNodeCatalog.All
            .SelectMany(n => n.Properties.GroupBy(p => p.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"{n.WireType}: {g.Key} x{g.Count()}"))
            .ToList();

        Assert.That(duplicates, Is.Empty, string.Join(", ", duplicates));
    }

    [Test]
    public void Every_container_type_can_actually_hold_children()
    {
        // `Children` is init-only, so a node type without it cannot be given any.
        var problems = UiNodeCatalog.All
            .Where(n => n.IsContainer && !Settable(Resolve(n.ClassName)).Contains("Children"))
            .Select(n => $"{n.WireType} ({n.ClassName}) is marked as a container but has no Children")
            .ToList();

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    [Test]
    public void Every_leaf_type_is_marked_as_not_a_container()
    {
        // The reverse error is as bad: a container the designer will not let you nest into.
        var problems = UiNodeCatalog.All
            .Where(n => !n.IsContainer && Settable(Resolve(n.ClassName)).Contains("Children"))
            .Select(n => $"{n.WireType} ({n.ClassName}) accepts children but is marked as a leaf")
            .ToList();

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    [Test]
    public void The_twenty_four_node_types_have_distinct_wire_types_and_classes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiNodeCatalog.All.Select(n => n.WireType).Distinct().Count(),
                Is.EqualTo(UiNodeCatalog.All.Count), "Two catalog entries share a wire type.");
            Assert.That(UiNodeCatalog.All.Select(n => n.ClassName).Distinct().Count(),
                Is.EqualTo(UiNodeCatalog.All.Count), "Two catalog entries share a class.");
        });
    }
}
