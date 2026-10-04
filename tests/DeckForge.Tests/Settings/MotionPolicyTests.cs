using DeckForge.Core.Settings;
using NUnit.Framework;

namespace DeckForge.Tests.Settings;

/// <summary>
/// Part 19's reduced motion, reduced to the rule that can be decided without a window.
/// </summary>
/// <remarks>
/// <para>
/// Part 20.1 records that the feature was never built and why: there are no animations in the block editor
/// and no durations to shorten, so "reduced motion" had nothing to shorten. That is the honest starting
/// position and this is the half of the answer that has no window in it - the preference, the rule that
/// resolves it against the operating system, and the words the Settings page shows.
/// </para>
/// <para>
/// The other half - stopping the motion WPF and WPF-UI produce on their own - needs a live
/// <c>System.Windows</c> and lives in the App project, which this project deliberately does not reference.
/// What that means for coverage is stated in the delivery notes rather than glossed over: the rule is
/// tested here, the application of it is not.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MotionPolicyTests
{
    [Test]
    public void The_default_follows_the_system()
    {
        // A default of Always would take motion away from every user who has expressed no preference; a
        // default of Never would ignore the operating system setting a user has already chosen. Part 20's
        // rule - "each is added with a default that preserves today's behaviour" - points at the system.
        var fresh = new AppSettings();

        Assert.Multiple(() =>
        {
            Assert.That(fresh.VisualReduceMotion, Is.EqualTo(MotionPreference.System));
            Assert.That(
                MotionPolicy.IsReduced(MotionPreference.System, systemAsksToReduce: true),
                Is.True,
                "a machine asking for reduced motion is answered");
            Assert.That(
                MotionPolicy.IsReduced(MotionPreference.System, systemAsksToReduce: false),
                Is.False,
                "and a machine that does not is left alone");
        });
    }

    [Test]
    public void Always_reduces_motion_whatever_the_system_says()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MotionPolicy.IsReduced(MotionPreference.Always, systemAsksToReduce: true), Is.True);
            Assert.That(MotionPolicy.IsReduced(MotionPreference.Always, systemAsksToReduce: false), Is.True,
                "checked before the system value is looked at, so an explicit answer is never overruled");
        });
    }

    [Test]
    public void Never_keeps_motion_whatever_the_system_says()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MotionPolicy.IsReduced(MotionPreference.Never, systemAsksToReduce: true), Is.False);
            Assert.That(MotionPolicy.IsReduced(MotionPreference.Never, systemAsksToReduce: false), Is.False);
        });
    }

    [TestCase(MotionPreference.System, true)]
    [TestCase(MotionPreference.System, false)]
    [TestCase(MotionPreference.Always, true)]
    [TestCase(MotionPreference.Always, false)]
    [TestCase(MotionPreference.Never, true)]
    [TestCase(MotionPreference.Never, false)]
    public void Every_choice_resolves_to_something_and_says_what_it_resolved_to(
        MotionPreference preference,
        bool systemAsksToReduce)
    {
        // The sentence is what makes the resolution visible. Three radio buttons reading System, Always and
        // Never tell a user nothing about which is in effect, and "Always" next to a system that allows
        // motion looks like it has been ignored.
        var description = MotionPolicy.Describe(preference, systemAsksToReduce);

        Assert.Multiple(() =>
        {
            Assert.That(description, Is.Not.Empty);
            Assert.That(
                description,
                Does.Contain(MotionPolicy.IsReduced(preference, systemAsksToReduce) ? "Reduced" : "Full motion"));
            Assert.That(
                description,
                preference == MotionPreference.System
                    ? Does.Contain(systemAsksToReduce ? "the system asks" : "the system does not ask")
                    : Does.Contain("whatever the system says"),
                "and it says who decided");
        });
    }

    [Test]
    public void Every_choice_has_a_label_and_a_sentence_that_explains_it()
    {
        var problems = MotionPolicy.Options
            .Where(option =>
                string.IsNullOrWhiteSpace(option.Label)
                || string.IsNullOrWhiteSpace(option.Description))
            .Select(option => option.Value.ToString())
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(
                problems,
                Is.Empty,
                "a label is the part a user reads and the part nobody re-checks; 'Never' on its own is a "
                + "complete non-sentence to somebody who has not read Part 20:" + Environment.NewLine
                + string.Join(", ", problems));

            Assert.That(
                MotionPolicy.Options.Select(option => option.Value).Distinct().Count(),
                Is.EqualTo(MotionPolicy.Options.Count),
                "and no choice is offered twice");
        });
    }

    [Test]
    public void The_page_can_preselect_the_stored_choice()
    {
        Assert.Multiple(() =>
        {
            foreach (var option in MotionPolicy.Options)
            {
                Assert.That(MotionPolicy.OptionFor(option.Value), Is.EqualTo(option));
            }

            Assert.That(
                MotionPolicy.OptionFor((MotionPreference)99),
                Is.EqualTo(MotionPolicy.Options[0]),
                "a value from a hand-edited file falls back to following the system, which is the answer "
                + "that follows the user rather than overruling them");
        });
    }

    [Test]
    public void The_catalogue_points_the_motion_row_at_the_same_three_choices()
    {
        var descriptor = SettingCatalog.Find(nameof(AppSettings.VisualReduceMotion))!;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Id, Is.EqualTo(nameof(AppSettings.VisualReduceMotion)),
                "the key's stable name is its property name, so a rename is a compile error rather than a "
                + "silently orphaned setting");
            Assert.That(descriptor.Section, Is.EqualTo(SettingSection.Accessibility),
                "and it is the one key that belongs on the accessibility row of the page");
            Assert.That(descriptor.Default, Is.EqualTo(nameof(MotionPreference.System)));
            Assert.That(descriptor.Kind, Is.EqualTo(SettingKind.Choice));
        });
    }
}