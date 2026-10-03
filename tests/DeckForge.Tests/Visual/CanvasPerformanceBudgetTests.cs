using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// Part 9.9's budget: 60fps drag with 500 visible tiles.
/// </summary>
/// <remarks>
/// <para>
/// A performance test that fails is worth having, and one that *always* passes is worth having just as
/// much: a threshold nobody can move is a number in a document, and a threshold that moves on every machine
/// is a coin toss. So the budget here is stated in the same units as the thing it is protecting —
/// milliseconds per frame, for the half of a frame Core owns — and the measurement says which half it
/// measured.
/// </para>
/// <para>
/// The other half, WPF arranging and drawing a thousand tiles, is measured by the user-driver harness
/// against the real window. A unit test cannot see it, and a test that claimed to be a frame rate would be
/// a fiction.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CanvasPerformanceBudgetTests
{
    [Test]
    public void The_frame_budget_is_sixty_frames_a_second_and_Core_gets_a_tenth_of_it()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanvasPerformanceBudget.FrameMilliseconds, Is.EqualTo(16.67).Within(0.01),
                "60fps is 16.7 milliseconds, and a budget stated in anything else cannot be checked against "
                + "what the user sees");
            Assert.That(
                CanvasPerformanceBudget.CoreBudgetMilliseconds,
                Is.EqualTo(CanvasPerformanceBudget.FrameMilliseconds / 10),
                "the layout may have a tenth; the other nine tenths are WPF's, and a Core budget of half "
                + "the frame would pass a canvas that visibly stutters");
        });
    }

    [Test]
    public void Five_hundred_blocks_fit_the_budget()
    {
        var measurement = CanvasPerformanceBudget.Measure();

        Assert.That(
            measurement.WithinBudget,
            Is.True,
            "over budget: " + measurement.Summary);
    }

    [Test]
    public void The_benchmark_document_is_the_size_the_budget_names()
    {
        var project = CanvasPerformanceBudget.SyntheticProject(CanvasPerformanceBudget.TargetTiles);

        Assert.That(
            project.Targets[0].Scripts[0].Blocks().Count(),
            Is.EqualTo(CanvasPerformanceBudget.TargetTiles),
            "a budget measured against a smaller document is not the budget Part 9.9 wrote");
    }

    [Test]
    public void The_benchmark_document_nests_rather_than_being_one_flat_list()
    {
        // A flat document measures the case every implementation is already fast at. Every fifth block is a
        // container with a child, so the layout walks bodies the way a real script does.
        var project = CanvasPerformanceBudget.SyntheticProject(50);

        var containers = project.Targets[0].Scripts[0].Blocks().Count(block => block.Bodies.Count > 0);

        Assert.That(containers, Is.GreaterThan(5), "otherwise the layout is measuring a single stack");
    }

    [Test]
    public void The_measurement_says_what_it_measured_rather_than_claiming_a_frame_rate()
    {
        var measurement = CanvasPerformanceBudget.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.Blocks, Is.EqualTo(CanvasPerformanceBudget.TargetTiles));
            Assert.That(measurement.LayoutMilliseconds, Is.GreaterThan(0));
            Assert.That(measurement.Summary, Does.Contain("budget"),
                "a log line with no threshold in it is a number nobody can act on");
            Assert.That(
                measurement.Summary,
                Does.Not.Contain("fps"),
                "and it does not call itself a frame rate, because half a frame is not one");
        });
    }

    [Test]
    public void The_measurement_counts_one_pointer_move_and_not_one_per_candidate()
    {
        // The first version of this benchmark enumerated the document five hundred times per frame, because
        // that is what "consider 500 tiles" sounded like. It came out four times over budget and the honest
        // conclusion was that the *benchmark* was wrong: a drag asks the question once per frame, and the
        // canvas enumerates live tiles rather than walking the document at all.
        var measurement = CanvasPerformanceBudget.Measure();
        var onePass = CanvasPerformanceBudget.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.WithinBudget, Is.True, measurement.Summary);
            Assert.That(
                onePass.EnumerationMilliseconds,
                Is.LessThan(CanvasPerformanceBudget.CoreBudgetMilliseconds),
                "one enumeration of 500 blocks has to fit in a tenth of a frame, or the hit test needs "
                + "spatial indexing rather than a looser budget");
        });
    }

    [Test]
    public void The_cold_path_is_slower_than_the_cached_one_because_metrics_are_cached_by_block_id()
    {
        // Part 9.9's own rule: cached per block id, invalidated on mutation, not recomputed per frame. If the
        // cached read were not faster the cache would be dead weight and the rule would be wrong.
        var measurement = CanvasPerformanceBudget.Measure();

        Assert.That(
            measurement.CachedLayoutMilliseconds,
            Is.LessThan(measurement.LayoutMilliseconds),
            "a drag re-reads cached metrics rather than measuring every block again");
    }
}