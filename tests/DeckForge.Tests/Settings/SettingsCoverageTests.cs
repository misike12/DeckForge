using System.Text.RegularExpressions;
using DeckForge.Core.Settings;
using NUnit.Framework;

namespace DeckForge.Tests.Settings;

/// <summary>
/// The two promises Part 20 makes that only hold if the App keeps them: that every key is on the Settings
/// page, and that none of them reaches the code generator.
/// </summary>
/// <remarks>
/// <para>
/// Both are checks on files rather than on objects, because the things being checked are the WPF
/// application and the emitter, and this project may not reference either. That is not a workaround dressed
/// up as a test - reading the markup and the source is the only way to assert a claim about them from
/// here, and it is how <c>XamlMarkupTests</c> already checks the pages.
/// </para>
/// <para>
/// The second one is the rule that is easiest to break by accident. "Settings that affect generated code
/// are never read by the emitter" is what keeps code generation a pure function of the document, and the
/// day an emitter reads <c>AppSettings</c> the same document emits different code on two machines and
/// nobody can say which file is wrong.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SettingsCoverageTests
{
    /// <summary>The App project's markup and source, if the tests are running beside it.</summary>
    /// <returns>The App project directory, or null when it cannot be found.</returns>
    /// <remarks>
    /// The same walk up from the test assembly's own folder that <c>XamlMarkupTests</c> uses. A published
    /// test package has no App source next to it, so every test here skips rather than fails in that case -
    /// skipping is the honest answer to "the thing I check is not present", and failing would report a
    /// defect that does not exist.
    /// </remarks>
    private static string? AppProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DeckForge.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Every .cs file under a project directory, skipping generated output.</summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <returns>The source files.</returns>
    private static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !file.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));

    [Test]
    public void Every_key_the_catalogue_offers_is_read_and_described_on_the_settings_page()
    {
        if (AppProject() is not { } app)
        {
            Assert.Ignore("The App project's source was not found beside the test assembly.");
            return;
        }

        var markup = File.ReadAllText(Path.Combine(app, "Pages", "SettingsPage.xaml"));

        var missing = SettingCatalog.RenderedSections
            .SelectMany(SettingCatalog.ForSection)
            .Where(descriptor => !Binds(descriptor.Id, markup))
            .Select(descriptor =>
                $"{descriptor.Id}: the page neither binds a value for it nor shows its catalogue description")
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "A key in the catalogue with no row on the page is a key Part 20 documents as available and "
            + "this product does not offer:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Test]
    public void A_row_that_shows_a_description_cannot_have_the_description_typed_into_the_markup()
    {
        if (AppProject() is not { } app)
        {
            Assert.Ignore("The App project's source was not found beside the test assembly.");
            return;
        }

        var markup = File.ReadAllText(Path.Combine(app, "Pages", "SettingsPage.xaml"));

        // The wording lives in SettingCatalog, and the page binds it. A sentence pasted into the markup is a
        // second copy, and the copy in XAML is the copy nobody re-reads - which is how a settings page ends
        // up promising a feature a later commit removed.
        var descriptions = SettingCatalog.All
            .Where(descriptor => markup.Contains(descriptor.Description, StringComparison.Ordinal))
            .Select(descriptor => descriptor.Id)
            .ToList();

        Assert.That(
            descriptions,
            Is.Empty,
            "These descriptions are written into the markup as well as into the catalogue. Bind them from "
            + "the view model's *Info properties instead:" + Environment.NewLine
            + string.Join(Environment.NewLine, descriptions));
    }

    [Test]
    public void The_code_generator_never_reads_the_settings()
    {
        var codeGen = AppProject() is { } app
            ? Path.Combine(app, "..", "DeckForge.CodeGen")
            : null;

        if (codeGen is null || !Directory.Exists(codeGen))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(codeGen))
            {
                codeGen = Path.Combine(directory.FullName, "src", "DeckForge.CodeGen");
                directory = directory.Parent;
            }
        }

        if (codeGen is null || !Directory.Exists(codeGen))
        {
            Assert.Ignore("The code generator's source was not found beside the test assembly.");
            return;
        }

        var offenders = new List<string>();

        foreach (var file in SourceFiles(codeGen))
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "AppSettings", "SettingCatalog", "Core.Settings" })
            {
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(forbidden)}\b"))
                {
                    offenders.Add($"{Path.GetFileName(file)} mentions {forbidden}");
                }
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Part 20's first rule is that settings which affect generated code are never read by the "
            + "emitter, so the same document emits the same C# on every machine. These do read them:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void The_settings_view_model_writes_the_reduced_motion_key_back()
    {
        if (AppProject() is not { } app)
        {
            Assert.Ignore("The App project's source was not found beside the test assembly.");
            return;
        }

        var source = File.ReadAllText(Path.Combine(app, "ViewModels", "SettingsViewModel.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain("s.VisualReduceMotion = value"),
                "a setting the page offers and nothing writes is a control that renders and does nothing");

            Assert.That(
                source,
                Does.Contain("MotionPolicy.Describe"),
                "and the sentence under the radios is computed, so the page and the window cannot disagree "
                + "about what is in effect");
        });
    }

    /// <summary>Whether the markup both binds a value for a key and shows its catalogue description.</summary>
    /// <param name="id">The key's stable name.</param>
    /// <param name="markup">The page's markup.</param>
    /// <returns>True when both bindings are present.</returns>
    /// <remarks>
    /// The value binding is accepted with or without an explicit <c>Mode=TwoWay</c>, because
    /// <c>CheckBox.IsChecked</c> and <c>TextBox.Text</c> already default to two-way and half the rows on
    /// the page lean on that. Requiring the attribute would have forced seven redundant bindings into the
    /// markup to satisfy a test, which is how tests start being written for.
    /// </remarks>
    private static bool Binds(string id, string markup) =>
        (markup.Contains($"{{Binding {id},", StringComparison.Ordinal)
            || markup.Contains($"{{Binding {id}}}", StringComparison.Ordinal))
        && markup.Contains($"{{Binding {id}Info.", StringComparison.Ordinal);
}