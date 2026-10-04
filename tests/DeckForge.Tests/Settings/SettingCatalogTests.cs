using System.Globalization;
using System.Reflection;
using DeckForge.Core.Settings;
using NUnit.Framework;

namespace DeckForge.Tests.Settings;

/// <summary>
/// Part 20's sixteen keys, and the promises the catalogue makes about each one.
/// </summary>
/// <remarks>
/// <para>
/// Part 20.1 is this repository's own record that the sixteen keys in the table were a design rather than
/// a product, and that reading the table as a description is how a contributor writes
/// <c>Settings.VisualSnapRadius</c> and discovers it does not compile. The keys exist now, which makes the
/// table true; these tests are what stop it quietly becoming untrue again.
/// </para>
/// <para>
/// Most of the tests here are about agreement rather than behaviour. A default lives in three places -
/// the property initialiser, the catalogue, and eventually the file on disk - and the catalogue's whole
/// purpose is that the page and the validator read one of them instead of two. That only works if they are
/// the same, so the agreement is asserted rather than assumed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SettingCatalogTests
{
    /// <summary>
    /// The sixteen keys, in the order Part 20's table lists them.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the catalogue, because a test that builds its expectation from
    /// the thing it is checking asserts nothing. This list is Part 20 transcribed, and it is the thing
    /// that would catch a key being dropped or a seventeenth being invented.
    /// </remarks>
    private static readonly string[] ExpectedIds =
    [
        "VisualSnapEnabled",
        "VisualSnapRadius",
        "VisualSnapToGrid",
        "VisualGridSize",
        "VisualDefaultZoom",
        "VisualMinimapVisible",
        "VisualPaletteShowDeprecated",
        "VisualAutosaveSeconds",
        "VisualReduceMotion",
        "VisualBlockTextSize",
        "VisualStageAllowNetwork",
        "VisualStageStepDelayMs",
        "VisualBreakpoints",
        "VisualCannedResponses",
        "VisualShowBlockCode",
        "VisualOnboardingSeen",
    ];

    [Test]
    public void The_catalogue_holds_the_sixteen_keys_Part_20_names_and_in_that_order()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingCatalog.All, Has.Count.EqualTo(16), "Part 20's table has sixteen rows.");
            Assert.That(
                SettingCatalog.Ids,
                Is.EqualTo(ExpectedIds),
                "same keys, same order, so the table and the file can be read side by side");
        });
    }

    [Test]
    public void Every_key_has_a_label_a_description_and_a_default_that_matches_its_kind()
    {
        var problems = new List<string>();

        foreach (var descriptor in SettingCatalog.All)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                problems.Add($"{descriptor.Id}: no label, so the row would render blank");
            }

            if (string.IsNullOrWhiteSpace(descriptor.Description))
            {
                problems.Add($"{descriptor.Id}: no description, which is one of the four things a key must have");
            }

            switch (descriptor.Kind)
            {
                // A toggle's default is written by the factory as "true" or "false", so it cannot fail
                // to parse and there is nothing here to check.

                case SettingKind.Number when !descriptor.IsInRange(descriptor.DefaultNumber):
                    problems.Add(
                        $"{descriptor.Id}: default {descriptor.Default} is outside its own range "
                        + $"{descriptor.RangeText}");
                    break;

                case SettingKind.Choice when !Enum.TryParse(descriptor.Default, ignoreCase: true, out MotionPreference _):
                    problems.Add($"{descriptor.Id}: default '{descriptor.Default}' is not a MotionPreference");
                    break;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                problems,
                Is.Empty,
                "A key missing one of its four attributes is a key that cannot be rendered, defaulted or "
                + "validated consistently:" + Environment.NewLine + string.Join(Environment.NewLine, problems));

            Assert.That(
                SettingCatalog.Ids.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(SettingCatalog.All.Count),
                "and no id appears twice, because the second one would silently lose to the first");
        });
    }

    [Test]
    public void Every_default_of_the_model_is_the_default_the_catalogue_records()
    {
        // The whole point of the catalogue. `new AppSettings()` is what a user gets on first run and what a
        // corrupt file falls back to, so if these two ever disagree, the page's hint ("off, or 15-600 s")
        // and the value in the file are describing two different products.
        var fresh = new AppSettings();
        var mismatched = new List<string>();

        foreach (var descriptor in SettingCatalog.All)
        {
            var property = typeof(AppSettings).GetProperty(descriptor.Id, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                mismatched.Add($"{descriptor.Id}: AppSettings has no property with that name");
                continue;
            }

            var actual = property.GetValue(fresh);

            // A number compared as text, so 16 stays 16 and 13.5 stays 13.5 in the failure message
            // instead of "13.5 differs from 13.5".
            var rendered = actual is IFormattable formattable && actual is not bool
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : actual?.ToString();

            string? expected = descriptor.Kind switch
            {
                SettingKind.Toggle => descriptor.DefaultToggle.ToString(),
                SettingKind.Number => descriptor.DefaultNumber.ToString(CultureInfo.InvariantCulture),
                SettingKind.Choice => descriptor.Default,
                _ => null,
            };

            if (descriptor.Kind is SettingKind.Toggle or SettingKind.Number && !Equals(rendered, expected))
            {
                mismatched.Add($"{descriptor.Id}: model says {rendered}, catalogue says {expected}");
            }
        }

        Assert.That(
            mismatched,
            Is.Empty,
            "A key whose model default and catalogue default differ will validate one value and show "
            + "another:" + Environment.NewLine + string.Join(Environment.NewLine, mismatched));
    }

    [Test]
    public void A_number_inside_its_range_is_left_exactly_as_it_is()
    {
        var radius = SettingCatalog.Find("VisualSnapRadius")!;

        Assert.Multiple(() =>
        {
            Assert.That(radius.IsInRange(40), Is.True);
            Assert.That(radius.Clamp(40), Is.EqualTo(40));
            Assert.That(radius.Clamp(16), Is.EqualTo(16), "inclusive at the bottom");
            Assert.That(radius.Clamp(96), Is.EqualTo(96), "inclusive at the top, because 96 is a magnet radius");
        });
    }

    [Test]
    public void A_number_outside_its_range_is_brought_to_the_nearest_value_that_works()
    {
        var radius = SettingCatalog.Find("VisualSnapRadius")!;

        Assert.Multiple(() =>
        {
            Assert.That(radius.Clamp(400), Is.EqualTo(96), "clamped, not reset: the user chose a big magnet");
            Assert.That(radius.Clamp(1), Is.EqualTo(16));
            Assert.That(radius.Clamp(double.NaN), Is.EqualTo(radius.DefaultNumber),
                "a NaN sails through Math.Clamp unchanged, so it has to be caught before the range check");
        });
    }

    [Test]
    public void A_value_in_the_gap_between_two_allowed_ranges_lands_on_the_nearer_bound()
    {
        // Autosave is the one key whose documented values are not contiguous: 0, or 15 to 600. Modelling it
        // as a single 0-600 range would accept one second, and one second of autosave rewrites the canvas
        // faster than a person can stop dragging.
        var autosave = SettingCatalog.Find("VisualAutosaveSeconds")!;

        Assert.Multiple(() =>
        {
            Assert.That(autosave.Ranges, Has.Count.EqualTo(2), "off is one range and 15-600 is the other");
            Assert.That(autosave.IsInRange(1), Is.False, "one second is not an allowed interval");
            Assert.That(autosave.Clamp(7), Is.EqualTo(0),
                "closer to off than to 15, and turning autosave on unasked is the worse of the two");
            Assert.That(autosave.Clamp(12), Is.EqualTo(15), "closer to 15 than to off");
            Assert.That(autosave.Clamp(700), Is.EqualTo(600));
            Assert.That(autosave.Clamp(30), Is.EqualTo(30), "and a real interval is untouched");
        });
    }

    [Test]
    public void The_page_gets_its_keys_in_the_sections_it_renders_them_in()
    {
        // Written out rather than compared with the catalogue against itself, because a check that derives
        // its expectation from the thing it is checking asserts nothing. This is Part 20's order, minus the
        // two keys that are deliberately not offered and minus the one that moved to Accessibility.
        Assert.Multiple(() =>
        {
            Assert.That(
                SettingCatalog.ForSection(SettingSection.Visual).Select(descriptor => descriptor.Id),
                Is.EqualTo(new[]
                {
                    "VisualSnapEnabled",
                    "VisualSnapRadius",
                    "VisualSnapToGrid",
                    "VisualGridSize",
                    "VisualDefaultZoom",
                    "VisualMinimapVisible",
                    "VisualPaletteShowDeprecated",
                    "VisualAutosaveSeconds",
                    "VisualBlockTextSize",
                    "VisualShowBlockCode",
                    "VisualOnboardingSeen",
                }),
                "the canvas's own settings, in the order Part 20 lists them");

            Assert.That(
                SettingCatalog.ForSection(SettingSection.Accessibility).Select(descriptor => descriptor.Id),
                Is.EqualTo(new[] { "VisualReduceMotion" }),
                "and the one key that is actually read");

            Assert.That(
                SettingCatalog.ForSection(SettingSection.Simulator).Select(descriptor => descriptor.Id),
                Is.EqualTo(new[] { "VisualStageAllowNetwork", "VisualStageStepDelayMs" }));

            Assert.That(
                SettingCatalog.ForSection(SettingSection.Internal),
                Is.Empty,
                "and nothing from the internal section, because those two have no editor worth offering");

            Assert.That(
                SettingCatalog.RenderedSections.Sum(section => SettingCatalog.ForSection(section).Count),
                Is.EqualTo(14),
                "fourteen rows, which is the sixteen minus the two stored-only keys");
        });
    }

    [Test]
    public void The_two_keys_that_belong_to_a_document_are_stored_and_not_offered()
    {
        var storedOnly = SettingCatalog.StoredOnlyKeys.Select(descriptor => descriptor.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(
                storedOnly,
                Is.EqualTo(new[] { "VisualBreakpoints", "VisualCannedResponses" }),
                "both are listed on the page by name, in one sentence, so 'stored but not shown' is visible");

            foreach (var descriptor in SettingCatalog.StoredOnlyKeys)
            {
                Assert.That(
                    typeof(AppSettings).GetProperty(descriptor.Id),
                    Is.Not.Null,
                    $"{descriptor.Id} is not offered, but it is still a real property and a real part of the file");
            }
        });
    }

    [Test]
    public void An_unknown_key_is_reported_rather_than_ignored()
    {
        var info = SettingCatalog.InfoFor("VisualSomethingFromANewerDeckForge");

        Assert.Multiple(() =>
        {
            Assert.That(SettingCatalog.Find("VisualSomethingFromANewerDeckForge"), Is.Null);
            Assert.That(
                info.Id,
                Is.EqualTo("VisualSomethingFromANewerDeckForge"),
                "the fallback names the id that did not match, because that string is the diagnosis");
            Assert.That(info.Description, Does.Contain("VisualSomethingFromANewerDeckForge"));
        });
    }

    [Test]
    public void The_known_key_returns_its_own_row_text()
    {
        var descriptor = SettingCatalog.Find("VisualReduceMotion")!;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ToInfo(), Is.EqualTo(SettingCatalog.InfoFor("VisualReduceMotion")));
            Assert.That(descriptor.ToInfo().Name, Is.EqualTo(descriptor.Name));
            Assert.That(descriptor.ToInfo().Range, Is.EqualTo(descriptor.RangeText));
        });
    }

    [Test]
    public void The_range_hint_is_derived_rather_than_written_out_twice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingCatalog.Find("VisualSnapRadius")!.RangeText, Is.EqualTo("16–96 px"));
            Assert.That(SettingCatalog.Find("VisualSnapEnabled")!.RangeText, Is.EqualTo("on or off"));
            Assert.That(SettingCatalog.Find("VisualAutosaveSeconds")!.RangeText, Is.EqualTo("0 or 15–600 s"));
            Assert.That(SettingCatalog.Find("VisualReduceMotion")!.RangeText, Is.EqualTo("one of the listed choices"));
        });
    }
}