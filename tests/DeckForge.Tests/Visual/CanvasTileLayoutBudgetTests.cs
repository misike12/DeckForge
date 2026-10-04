using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DeckForge.Core.Visual;
using NUnit.Framework;

namespace DeckForge.Tests.Visual;

/// <summary>
/// The WPF half of §28.3's drag budget: what arranging and drawing five hundred tiles costs.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanvasPerformanceBudget</c> measures Core's half - the cached metric reads and one hit-test pass -
/// and §28.5 records the other half as unmeasured. This is the other half.
/// </para>
/// <para>
/// <b>What is measured.</b> The layout-and-draw cost of a <em>tile-shaped element</em>: real
/// <see cref="Border"/>, <see cref="TextBlock"/>, <see cref="Ellipse"/> and <see cref="StackPanel"/>
/// elements in a real visual tree on a real <see cref="Canvas"/>, measured with <c>Measure</c> and
/// <c>Arrange</c> and rasterised through <see cref="RenderTargetBitmap"/>. It is deliberately not
/// <c>BlockTile</c> - that class lives in <c>src/DeckForge.App</c>, which this test project does not
/// reference, and reaching it would mean asserting against a control whose bindings, converters,
/// <c>OnRender</c> geometry and data template all come from the app. A tile-shaped element is the honest
/// stand-in: the same element kinds, the same nesting, the same layout-rounding and pixel-snapping
/// settings, driven by the same synthetic document <see cref="CanvasPerformanceBudget"/> builds for the
/// Core half - so the two halves are measured over one document rather than two that happen to be the
/// same size.
/// </para>
/// <para>
/// <b>Why it is not a frame rate.</b> <see cref="RenderTargetBitmap"/> is a software rasteriser. Drawing
/// to it is not the path a tile takes on screen, which is a DirectX compositor upload the GPU does. Its
/// absolute cost is therefore a property of the machine running the test, and a threshold tight enough to
/// mean "60fps" would be a threshold about the CPU rather than about the app. So the number is reported
/// and asserted against a deliberately loose budget, and the one assertion that is tight is a ratio,
/// which is machine-independent.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CanvasTileLayoutBudgetTests
{
    /// <summary>The surface a deck-sized canvas is drawn into.</summary>
    private static readonly Size Surface = new(1920, 1080);

    /// <summary>
    /// How long arranging and drawing the tile canvas may take, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twenty frames, and the multiplication is the point rather than an accident of arithmetic. On the
    /// machine this was written on - a four-core i5-3570 - the best-of-twelve figure is roughly 90ms:
    /// about 17ms of arrange and 70ms of software draw. Twenty frames is 333ms, which is three and a
    /// half times that, and the headroom is for the machine this test ends up running on rather than the
    /// one it was written on: a shared CI box, a laptop at its thermal limit, or an antivirus scanning
    /// the output.
    /// </para>
    /// <para>
    /// What the budget catches is a <em>shape</em> change, which is what actually goes wrong here: a tile
    /// that stops being virtualised, a background that stops being a frozen brush, a per-tile visual that
    /// accumulates. Those multiply the cost; they do not add ten per cent to it. A budget that tried to
    /// assert a frame instead would fail on a faster machine's noise and pass on a slower machine's
    /// regression, which is worse than no assertion at all.
    /// </para>
    /// </remarks>
    public const double TileBudgetMilliseconds = CanvasPerformanceBudget.FrameMilliseconds * 20;

    /// <summary>Rounds thrown away before anything is recorded.</summary>
    /// <remarks>
    /// JIT, the first touch of every generic layout path WPF has, the font cache, and the first
    /// collection of the render target's pixel buffer. The first pass over this tree measured 195ms of
    /// arrange against 5ms once warm - a factor of thirty-nine that has nothing to do with the canvas.
    /// </remarks>
    private const int WarmupRounds = 3;

    /// <summary>Rounds recorded, of which the best is kept.</summary>
    /// <remarks>
    /// <para>
    /// The best, not the mean. A mean of nine samples is a statement about the machine as much as about
    /// the code: one descheduled thread, one GC, one background compile, and the mean moves by more than
    /// the difference between a good layout and a bad one. The minimum is the closest available estimate
    /// of the work itself, and it is the number that is stable - the arrange pass over this tree varied
    /// between 16ms and 45ms across runs while its minimum sat within a few per cent of itself.
    /// </para>
    /// <para>
    /// The render target is allocated once and cleared between rounds for the same reason. A fresh
    /// 1920x1080 Pbgra32 bitmap is 8.3MB, which is large-object-heap traffic on every single round, and
    /// allocating one per round was the largest source of variance in the first version of this test.
    /// </para>
    /// </remarks>
    private const int MeasuredRounds = 9;

    /// <summary>
    /// How much more a tile may cost per tile at the full size than at a quarter of it.
    /// </summary>
    /// <remarks>
    /// The tight assertion in this fixture, and the reason it is a ratio. The per-tile draw cost measured
    /// here is flat - about 0.14ms per tile at 125 tiles and at 500 - which is the property that makes
    /// "500 visible tiles" a number worth having: cost is linear in what is on screen, so the canvas
    /// degrades predictably and viewport culling is the only lever that matters. Two would catch a
    /// super-linear blow-up (a per-tile visual that accumulates, a missing cache, a measure pass that
    /// re-measures the whole tree) while tolerating the cache effects a four-core box shows. It cannot
    /// fail because the machine is slow: both sides of the ratio move together.
    /// </remarks>
    private const double PerTileGrowthAllowance = 2.0;

    private static IReadOnlyDictionary<int, TileMeasurement> _measured = null!;

    /// <summary>What one size of tile canvas cost.</summary>
    /// <param name="Tiles">How many tile-shaped elements were arranged and drawn.</param>
    /// <param name="Elements">How many WPF elements those tiles were made of.</param>
    /// <param name="ColdMilliseconds">The first arrange, with nothing warm.</param>
    /// <param name="ArrangeMilliseconds">The best warm arrange, which is what a drag frame pays.</param>
    /// <param name="DrawMilliseconds">The best software draw of the arranged tree.</param>
    public sealed record TileMeasurement(
        int Tiles,
        int Elements,
        double ColdMilliseconds,
        double ArrangeMilliseconds,
        double DrawMilliseconds)
    {
        /// <summary>Arrange and draw together, which is the half of a frame this fixture measures.</summary>
        public double TotalMilliseconds => ArrangeMilliseconds + DrawMilliseconds;

        /// <summary>
        /// What one tile costs to draw, with the fixed cost of the surface itself taken out.
        /// </summary>
        /// <remarks>
        /// An empty 1920x1080 surface still costs something to clear and present, and that cost is the
        /// same for one tile and for five hundred. Subtracting it is what makes the number comparable
        /// across sizes, and therefore what makes a ratio between two sizes meaningful.
        /// </remarks>
        public double DrawMillisecondsPerTile(float emptySurfaceMilliseconds) =>
            Math.Max(0, DrawMilliseconds - emptySurfaceMilliseconds) / Math.Max(1, Tiles);

        /// <summary>The figures as one line, for the test log.</summary>
        public string Summary =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Tiles} tiles ({Elements} elements): arrange {ArrangeMilliseconds:F2}ms, "
                + $"software draw {DrawMilliseconds:F2}ms, total {TotalMilliseconds:F2}ms "
                + $"(budget {TileBudgetMilliseconds:F0}ms; cold first arrange {ColdMilliseconds:F0}ms)");
    }

    [OneTimeSetUp]
    public void MeasureEverySizeOnce()
    {
        // Every test below reads the same measurements, on purpose: a timing fixture that measures
        // separately for each assertion pays the cold cost several times over and gives each assertion
        // a different number to be judged by.
        var sizes = new[] { CanvasPerformanceBudget.TargetTiles, 250, 125, 1 };
        _measured = OnSta(() =>
        {
            var results = new Dictionary<int, TileMeasurement>();
            foreach (var tiles in sizes)
            {
                results[tiles] = Measure(tiles);
            }

            return (IReadOnlyDictionary<int, TileMeasurement>)results;
        });

        foreach (var measurement in _measured.Values.OrderByDescending(measurement => measurement.Tiles))
        {
            TestContext.Out.WriteLine("  " + measurement.Summary);

            // Progress as well as Out: a budget whose figure only appears in a trx is a figure nobody
            // reads, and the number is the reason this fixture exists.
            TestContext.Progress.WriteLine("  tile budget: " + measurement.Summary);
        }
    }

    [Test]
    public void The_test_host_can_measure_the_wpf_half_at_all()
    {
        // This fixture compiles against PresentationFramework, which is why the test project targets
        // net10.0-windows with UseWPF: a plain net10.0 project does not reference it, and the probe that
        // established that is the reason the target framework was changed rather than the measurement
        // faked. What can still go wrong at run time is a host without the Windows Desktop framework, so
        // that is what this checks - and it checks it by drawing, because a tree that measures without
        // rendering would leave the draw half of the budget unmeasured while every assertion passed.
        var one = _measured[1];

        Assert.Multiple(() =>
        {
            Assert.That(typeof(Border).Assembly.GetName().Name, Is.EqualTo("PresentationFramework"),
                "This fixture is measuring WPF; if the host has loaded the type from somewhere else, the "
                + "timings are not WPF's.");
            Assert.That(one.Tiles, Is.EqualTo(1));
            Assert.That(one.Elements, Is.GreaterThan(1), "a tile-shaped element is a tree, not a leaf");
            Assert.That(one.DrawMilliseconds, Is.GreaterThan(0),
                "nothing was drawn, so the draw half of the budget is not being measured at all");
            Assert.That(one.Summary, Does.Contain("budget"),
                "a measurement with no threshold in it is a number nobody can act on");
        });
    }

    [Test]
    public void Arranging_and_drawing_five_hundred_tile_shaped_elements_fits_a_stated_budget()
    {
        var measurement = _measured[CanvasPerformanceBudget.TargetTiles];

        Assert.That(
            measurement.Tiles,
            Is.EqualTo(CanvasPerformanceBudget.TargetTiles),
            "the budget names 500 visible tiles, and a canvas measured at another size is a different "
            + "budget");

        Assert.That(
            measurement.TotalMilliseconds,
            Is.LessThan(TileBudgetMilliseconds),
            "over budget: " + measurement.Summary
            + ". The budget is twenty frames and the comment on it says why it is that loose: a software "
            + "rasteriser's cost is the test machine's, not the app's, and a tight threshold here would "
            + "fail on a slow box and pass on a fast one.");
    }

    [Test]
    public void The_tile_canvas_is_the_size_the_budget_names_and_is_not_one_flat_list()
    {
        // The same two properties CanvasPerformanceBudgetTests holds the Core benchmark to, held here as
        // well: the tile count is the budget's, and every fifth tile wraps a child. A flat canvas measures
        // the case every layout is already fast at.
        var measurement = _measured[CanvasPerformanceBudget.TargetTiles];

        Assert.Multiple(() =>
        {
            Assert.That(measurement.Tiles, Is.EqualTo(CanvasPerformanceBudget.TargetTiles));
            Assert.That(measurement.Elements, Is.GreaterThan(CanvasPerformanceBudget.TargetTiles * 3),
                "a tile is a border, a header row, a menu, a dot and - for a container - a child tile; "
                + "fewer than three elements each means the tree is not tile-shaped");
            Assert.That(measurement.ColdMilliseconds, Is.GreaterThan(measurement.ArrangeMilliseconds),
                "the first arrange has to be the expensive one, or the warm-up is not warming anything "
                + "and the recorded figure is still paying for JIT");
        });
    }

    [Test]
    public void The_cost_of_a_tile_does_not_grow_with_the_canvas()
    {
        // The assertion that is allowed to be tight, because it is a ratio: both sides of it scale with
        // the machine, so it fails when the layout or the draw becomes super-linear in the number of
        // tiles and not when the machine has a bad afternoon.
        var full = _measured[CanvasPerformanceBudget.TargetTiles];
        var quarter = _measured[125];
        var empty = _measured[1];

        var fullPerTile = full.DrawMillisecondsPerTile((float)empty.DrawMilliseconds);
        var quarterPerTile = quarter.DrawMillisecondsPerTile((float)empty.DrawMilliseconds);
        var ratio = quarterPerTile == 0 ? double.PositiveInfinity : fullPerTile / quarterPerTile;

        Assert.That(
            ratio,
            Is.LessThan(PerTileGrowthAllowance),
            string.Create(
                CultureInfo.InvariantCulture,
                $"a tile costs {fullPerTile:F4}ms to draw at 500 tiles and {quarterPerTile:F4}ms at 125 - "
                + $"{ratio:F2}x. Cost per tile has to stay flat for '500 visible tiles' to be a number "
                + $"worth having; growth means something in the tile is accumulating. {full.Summary}"));
    }

    [Test]
    public void The_measurement_says_what_it_measured_rather_than_claiming_a_frame_rate()
    {
        var measurement = _measured[CanvasPerformanceBudget.TargetTiles];

        Assert.Multiple(() =>
        {
            Assert.That(measurement.Summary, Does.Contain("software draw"),
                "the draw half goes through a software rasteriser and the summary has to say so");
            Assert.That(measurement.Summary, Does.Not.Contain("fps"),
                "and it does not call itself a frame rate, because a software raster is not one");
            Assert.That(measurement.ArrangeMilliseconds, Is.GreaterThan(0));
        });
    }

    // ---- the measurement ----------------------------------------------------------------------------

    /// <summary>Measures one canvas size. Runs on a single-threaded-apartment thread.</summary>
    private static TileMeasurement Measure(int tiles)
    {
        // Brushed objects have thread affinity, so the palette is built here rather than in a field.
        var fill = Frozen(Color.FromRgb(0x4C, 0x97, 0xFF));
        var menu = Frozen(Color.FromRgb(0xDD, 0xE8, 0xFF));
        var ink = Frozen(Colors.Black);
        var stroke = Frozen(Colors.Black);
        var accent = Frozen(Colors.IndianRed);

        var canvas = new Canvas { Width = Surface.Width, Height = Surface.Height };
        var index = 0;
        foreach (var block in CanvasPerformanceBudget.SyntheticProject(tiles).Targets[0].Scripts[0].Blocks())
        {
            var tile = Tile(block.Bodies.Count > 0, fill, menu, ink, stroke, accent, depth: 0);
            Canvas.SetLeft(tile, (index % Columns) * ColumnWidth);
            Canvas.SetTop(tile, (index / Columns) * RowHeight);
            canvas.Children.Add(tile);
            index++;
        }

        var elements = CountElements(canvas);

        var cold = Stopwatch.StartNew();
        canvas.Measure(Surface);
        canvas.Arrange(new Rect(Surface));
        cold.Stop();

        var target = new RenderTargetBitmap(
            (int)Surface.Width, (int)Surface.Height, 96, 96, PixelFormats.Pbgra32);

        var arrange = new List<double>(MeasuredRounds);
        var draw = new List<double>(MeasuredRounds);

        for (var round = 0; round < WarmupRounds + MeasuredRounds; round++)
        {
            Nudge(canvas);

            var clock = Stopwatch.StartNew();
            canvas.Measure(Surface);
            canvas.Arrange(new Rect(Surface));
            var arranged = clock.Elapsed.TotalMilliseconds;

            // Cleared, not reallocated: see MeasuredRounds.
            target.Clear();
            target.Render(canvas);
            var drawn = clock.Elapsed.TotalMilliseconds - arranged;

            if (round >= WarmupRounds)
            {
                arrange.Add(arranged);
                draw.Add(drawn);
            }
        }

        return new TileMeasurement(
            canvas.Children.Count,
            elements,
            cold.Elapsed.TotalMilliseconds,
            arrange.Min(),
            draw.Min());
    }

    private const int Columns = 20;
    private const double ColumnWidth = 190;
    private const double RowHeight = 56;

    /// <summary>Moves every tile one pixel, which is what a drag does to the canvas.</summary>
    /// <remarks>
    /// Moving a tile invalidates its arrange, which is the whole reason the warm arrange costs anything:
    /// a canvas nobody has touched is measured once and never again. Without this the arrange half of
    /// the budget would read as zero and every threshold on it would be meaningless.
    /// </remarks>
    private static void Nudge(Canvas canvas)
    {
        foreach (UIElement child in canvas.Children)
        {
            Canvas.SetLeft(child, Canvas.GetLeft(child) + 1);
        }
    }

    /// <summary>
    /// One tile-shaped element: a bordered, layout-rounded container with a header row and, for a
    /// container block, a body holding one more tile.
    /// </summary>
    /// <param name="container">Whether this block wraps a body.</param>
    /// <param name="fill">The tile's fill.</param>
    /// <param name="menu">The inline menu's fill.</param>
    /// <param name="ink">Label ink.</param>
    /// <param name="stroke">The outline and the menu's border.</param>
    /// <param name="accent">The comment dot.</param>
    /// <param name="depth">How deep this tile sits, so nesting terminates.</param>
    /// <remarks>
    /// The shape follows <c>BlockTile.xaml</c>: a rounded, outlined root; a header holding the label's
    /// words, the inline menu and the comment dot; and an <c>ItemsControl</c>-shaped body for a container.
    /// What it does not have is the app's bindings, converters, data templates and <c>OnRender</c>
    /// geometry - which is the difference between measuring a tile-shaped element and measuring
    /// <c>BlockTile</c>, and the reason the test is named for what it measures.
    /// </remarks>
    private static Border Tile(
        bool container,
        Brush fill,
        Brush menu,
        Brush ink,
        Brush stroke,
        Brush accent,
        int depth)
    {
        var root = new Border
        {
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Padding = new Thickness(6, 3, 6, 3),
            Width = depth == 0 ? 184 : 168,
        };

        var grid = new Grid();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = "when action runs",
            Foreground = ink,
            Margin = new Thickness(0, 0, 4, 0),
        });
        header.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(2, 0, 2, 0),
            Background = menu,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "Information", Foreground = ink },
        });
        header.Children.Add(new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = accent,
            Margin = new Thickness(3, 0, 0, 0),
        });
        grid.Children.Add(header);

        if (container && depth < 4)
        {
            var body = new StackPanel { Margin = new Thickness(1, 2, 1, 0) };
            body.Children.Add(Tile(false, fill, menu, ink, stroke, accent, depth + 1));
            grid.Children.Add(body);
        }

        root.Child = grid;
        return root;
    }

    /// <summary>A frozen brush, so the rasteriser can share it across every tile.</summary>
    /// <remarks>
    /// Frozen because a brush that is not frozen is a per-use resource: WPF clones it for every element
    /// that references it, which turns one brush into five hundred and changes what is being measured from
    /// "the cost of a tile" into "the cost of a tile plus the cost of copying its paint". An app that got
    /// this wrong would look slow here for a reason that has nothing to do with layout.
    /// </remarks>
    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static int CountElements(DependencyObject root)
    {
        var total = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            total++;
            total += CountElements(VisualTreeHelper.GetChild(root, index));
        }

        return total;
    }

    /// <summary>
    /// Runs something on a single-threaded-apartment thread and brings the answer back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF requires an STA thread the moment a <see cref="FrameworkElement"/> is constructed - the first
    /// version of this fixture threw <c>InvalidOperationException: The calling thread must be STA</c> and
    /// proved it. NUnit's <c>[Apartment]</c> attribute would fix that too, but only on an adapter that
    /// honours it, and a test that constructs its own thread behaves the same under every host.
    /// </para>
    /// <para>
    /// The value comes back rather than the assertion, so NUnit still records the result against the test
    /// that asked for it rather than against a worker thread.
    /// </para>
    /// </remarks>
    private static T OnSta<T>(Func<T> body)
    {
        var result = default(T)!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }
}