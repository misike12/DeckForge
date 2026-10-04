namespace DeckForge.Core.Visual;

/// <summary>
/// What a PNG export should include, besides the blocks themselves.
/// </summary>
/// <remarks>
/// <para>
/// A record of numbers and text, in Core, because every decision Part 25.3 asks for is one a test can make:
/// at what scale, how much padding, and whether there is a caption line. The only part of an export that
/// cannot live here is the rasterisation itself — a <c>RenderTargetBitmap</c> is a WPF type and an encoder
/// is a third-party one, and putting either in Core is what §28.5's "13 - PNG export" row refused to do.
/// </para>
/// <para>
/// Nothing in here is a brush, a colour struct or a bitmap. Colours are the <c>#RRGGBB</c> strings
/// <see cref="BlockThemePalette"/> already computes for the App's theme, so the export's colours are the
/// canvas's colours and the contrast §28.2 asks for is measured on the numbers that actually get drawn.
/// </para>
/// </remarks>
public sealed record PngExportOptions
{
    /// <summary>
    /// The scale an export is written at, twice the workspace size.
    /// </summary>
    /// <remarks>
    /// 2 rather than 1 because the point of a raster is to be pasted somewhere bigger than the editor: a
    /// screenshot of the canvas at 100% is one crisp image at exactly the size it was taken, while a
    /// 2x render is still legible in a chat client that has scaled it to half size. It costs four times the
    /// pixels and no layout decisions.
    /// </remarks>
    public const double DefaultScale = 2;

    /// <summary>The smallest scale an export will be written at.</summary>
    /// <remarks>
    /// A quarter, for the same reason <see cref="CanvasView.MinZoom"/> is: below it the labels stop being
    /// words. An export has no zoom control to argue with, so the bound is a constant rather than a clamp on
    /// a gesture.
    /// </remarks>
    public const double MinScale = 0.25;

    /// <summary>The largest scale an export will be written at.</summary>
    /// <remarks>
    /// Eight. Past it, a script a few thousand units wide asks for a surface wider than a rasteriser will
    /// allocate, and the honest answer is a smaller export rather than an allocation failure — which the
    /// caller reports by name instead.
    /// </remarks>
    public const double MaxScale = 8;

    /// <summary>The scale asked for, before <see cref="ClampedScale"/>.</summary>
    /// <remarks>
    /// Kept unclamped so a caller that wrote a scale it does not want can see it in the plan, which reports
    /// both this and what was used.
    /// </remarks>
    public double Scale { get; init; } = DefaultScale;

    /// <summary>
    /// Workspace units left empty around the drawing.
    /// </summary>
    /// <remarks>
    /// Zero by default rather than a comfortable margin: an export that adds space nobody asked for is an
    /// export whose size cannot be predicted from its contents, and a negative padding is read as none
    /// rather than as "crop it".
    /// </remarks>
    public double Padding { get; init; }

    /// <summary>
    /// The line written above the blocks, naming what they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null means the default, which is <see cref="PngExportPlan.CaptionFor"/> — the script's name in the
    /// target it belongs to. Part 25.3 asks for "a caption line naming the target", so the default has to be
    /// that; a caller who genuinely wants no caption passes <see cref="string.Empty"/>, and a caller who
    /// wants different words passes those.
    /// </para>
    /// <para>
    /// The caption sits on a plate drawn behind it. An export has a transparent background (Part 25.3), so
    /// ink with nothing behind it is readable on a light chat client and invisible on a dark one — which is
    /// a defect the user only discovers after pasting it somewhere.
    /// </para>
    /// </remarks>
    public string? Caption { get; init; }

    /// <summary>Whether the dark block palette is used.</summary>
    /// <remarks>
    /// Dark by default, and not as a preference: the export has no background to be dark <em>against</em>,
    /// so the palette that guarantees 4.5:1 for near-white ink on every category fill is the one that works
    /// on the widest range of surfaces. Both palettes are measured; this is the one that reads on a dark
    /// chat client, which is where an exported script usually ends up.
    /// </remarks>
    public bool Dark { get; init; } = true;

    /// <summary>
    /// The canvas's current zoom and pan, accepted and deliberately ignored.
    /// </summary>
    /// <remarks>
    /// It is a parameter so that a caller holding a view does not have to work out that it has nothing to
    /// pass, and so that what it is for can be pinned by a test rather than argued: Part 25.3 requires an
    /// export that "works for a script that is scrolled off screen", and a plan that read the pan would fail
    /// that for exactly the blocks the user scrolled away from. The plan is built in workspace units, so
    /// there is nothing in it for a pan to move.
    /// </remarks>
    public CanvasView? View { get; init; }

    /// <summary>The scale actually used, clamped to what a raster can be asked for.</summary>
    public double ClampedScale => Math.Clamp(Scale, MinScale, MaxScale);

    /// <summary>A padding that is a number a caller can add geometry with.</summary>
    /// <remarks>
    /// Negative padding clamped rather than obeyed, for the reason the scale is: it would crop the outermost
    /// block's edge off the export, and an export that quietly cuts a block in half is worse than one with no
    /// margin.
    /// </remarks>
    public double ClampedPadding => Math.Max(0, Padding);

    /// <summary>
    /// The caption a column is given: what the caller asked for, or the one naming its target.
    /// </summary>
    /// <param name="fallback">The caption to use when none was asked for.</param>
    /// <remarks>
    /// One place decides what null means, because "null means the default" and "null means nothing" are
    /// both reasonable and only one of them can be true at a time.
    /// </remarks>
    public string CaptionOr(string fallback) => Caption ?? fallback;
}