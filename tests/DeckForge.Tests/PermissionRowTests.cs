using DeckForge.Core.Plugins;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the permission checkbox model on the Manifest page.
/// </summary>
/// <remarks>
/// The model is exercised directly rather than through WPF. The bug these cover was a counter that
/// was incremented from a checkbox event which also fired when the program set the state, so the
/// number on screen drifted upward every time the page was opened - and the only way to reproduce
/// that was to reason about the XAML event wiring. The wiring is now Click rather than
/// Checked/Unchecked, which is asserted separately against the page markup.
/// </remarks>
[TestFixture]
public sealed class PermissionRowTests
{
    private static (PermissionRow Row, List<(string Name, bool Enabled)> Toggles) Build(string name = "host:config")
    {
        var toggles = new List<(string, bool)>();
        var row = new PermissionRow(name, false, (n, e) => toggles.Add((n, e)));
        return (row, toggles);
    }

    [Test]
    public void Clicking_a_row_records_the_new_state()
    {
        var (row, toggles) = Build();

        row.SetEnabled(true);

        Assert.Multiple(() =>
        {
            Assert.That(toggles, Is.EqualTo(new[] { ("host:config", true) }));
            Assert.That(row.Enabled, Is.True, "The row did not follow the click, so the one-way binding would snap back.");
        });
    }

    [Test]
    public void Clicking_the_same_value_twice_reports_both_clicks()
    {
        // The counter is recomputed from the rows now, so a repeated click cannot make it drift.
        var (row, toggles) = Build();

        row.SetEnabled(true);
        row.SetEnabled(true);
        row.SetEnabled(false);

        Assert.That(toggles, Has.Count.EqualTo(3));
        Assert.That(row.Enabled, Is.False);
    }

    [Test]
    public void Rebinding_does_not_fire_the_toggle()
    {
        // This is the load path. It must never report a toggle, or opening the page would rewrite
        // the manifest it just read.
        var toggles = new List<(string, bool)>();
        var row = new PermissionRow("host:devices", false, (n, e) => toggles.Add((n, e)));

        row.Rebind(true, (n, e) => toggles.Add((n, e)));

        Assert.Multiple(() =>
        {
            Assert.That(toggles, Is.Empty);
            Assert.That(row.Enabled, Is.True);
        });
    }

    [Test]
    public void A_group_counts_its_rows_rather_than_adding_up_events()
    {
        var group = new PermissionGroupVM(PermissionGroup.Host);
        var toggled = new List<(string, bool)>();
        foreach (var row in group.Rows)
        {
            row.Rebind(false, (n, e) => toggled.Add((n, e)));
        }

        Assert.That(group.EnabledCount, Is.Zero);

        var first = group.Rows[0];
        first.SetEnabled(true);
        group.Recount();
        Assert.That(group.EnabledCount, Is.EqualTo(1));

        first.SetEnabled(true);
        group.Recount();
        Assert.That(group.EnabledCount, Is.EqualTo(1), "A repeated click changed the count.");

        first.SetEnabled(false);
        group.Recount();
        Assert.That(group.EnabledCount, Is.Zero);
    }

    [Test]
    public void Refresh_recomputes_the_count_from_the_manifest()
    {
        var group = new PermissionGroupVM(PermissionGroup.Publishing);
        var names = group.Rows.Select(r => r.Name).ToList();
        Assume.That(names, Is.Not.Empty);

        group.Refresh(new HashSet<string>(names), (_, _) => { });
        Assert.That(group.EnabledCount, Is.EqualTo(names.Count));

        group.Refresh(new HashSet<string>(), (_, _) => { });
        Assert.That(group.EnabledCount, Is.Zero);
    }

    [Test]
    public void A_custom_permission_explains_itself_rather_than_showing_nothing()
    {
        var row = new PermissionRow("host:something-deckforge-does-not-know", true, (_, _) => { }, isCustom: true);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsCustom, Is.True);
            Assert.That(row.Explanation, Does.Contain("Custom permission"));
        });
    }

    [Test]
    public void A_known_permission_is_explained_from_the_catalog()
    {
        var known = PermissionCatalog.All[0];

        var row = new PermissionRow(known.Name, false, (_, _) => { });

        Assert.That(row.Explanation, Is.Not.Empty);
        Assert.That(row.IsCustom, Is.False);
    }
}
