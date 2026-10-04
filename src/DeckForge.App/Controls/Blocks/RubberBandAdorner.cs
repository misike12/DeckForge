using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Draws the rubber band: the rectangle a drag on empty canvas is making.
/// </summary>
/// <remarks>
/// <para>
/// An adorner, following <see cref="DragAdorner"/> exactly, and for the same reason. The band is not part of
/// the document and is not part of any tile's layout: putting an element in the tree would mean the canvas
/// had to measure around it and would have to be told to stop doing so the moment the pointer came back.
/// An adorner draws outside the tree entirely, so the canvas cannot tell a band is happening and the
/// document is only touched on the release.
/// </para>
/// <para>
/// Two rectangles rather than one: a translucent fill so the blocks under it are visibly inside the
/// selection, and a hard outline so the band's edge is findable at the moment of the release. A fill alone
/// on a canvas of pale blocks is a rectangle the eye cannot place, and the whole point is that the user can
/// see what they are about to select.
/// </para>
/// </remarks>
public sealed class RubberBandAdorner : Adorner
{
    private readonly Func<BlockBand?> _band;
    private readonly Func<IReadOnlyList<string>> _preview;

    /// <summary>Builds an adorner that reads the band and the preview from the caller.</summary>
    /// <param name="target">The element to draw over, which is the drag surface.</param>
    /// <param name="band">
    /// The band while a drag is in progress, or null when there is none. A function rather than a value so
    /// the adorner keeps no state of its own: the drag state lives on the workspace and an adorner that
    /// cached it would draw a rectangle for a drag that had already ended.
    /// </param>
    /// <param name="preview">
    /// The ids the band currently covers, for the line in the middle of the rectangle. It updates on every
    /// pointer move, so the user is told what is about to happen before letting go rather than after.
    /// </param>
    public RubberBandAdorner(
        UIElement target,
        Func<BlockBand?> band,
        Func<IReadOnlyList<string>> preview)
        : base(target)
    {
        _band = band;
        _preview = preview;
        IsHitTestVisible = false;
    }

    /// <summary>How transparent the inside of the band is.</summary>
    /// <remarks>
    /// Faint, because the blocks under it have to stay readable — the user is checking whether the band
    /// caught the block they meant, and a fill that hides the blocks defeats the gesture.
    /// </remarks>
    private const double FillOpacity = 0.12;

    /// <summary>How thick the band's edge is.</summary>
    private const double EdgeThickness = 1;

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        if (_band() is not { } band)
        {
            return;
        }

        var accent = Application.Current?.TryFindResource("Liquid.AccentBrush") as Brush ?? Brushes.White;
        var rectangle = new Rect(band.X, band.Y, band.Width, band.Height);

        context.PushOpacity(FillOpacity);
        context.DrawRectangle(accent, null, rectangle);
        context.Pop();

        // A dotted edge rather than a solid one, because a solid outline on top of a block's own border
        // reads as a second border on that block rather than as the band's edge.
        var edge = new Pen(accent, EdgeThickness) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        context.DrawRectangle(null, edge, rectangle);

        var count = _preview().Count;
        if (count == 0 || band.Width < 60 || band.Height < 28)
        {
            // No text where it would not fit: a clipped "12 blocks" inside a small band is worse than none,
            // and the count is repeated in the header's message line on the release anyway.
            return;
        }

        var text = new FormattedText(
            count == 1 ? "1 block" : $"{count} blocks",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface("Segoe UI"),
            11,
            accent,
            1.0);

        context.DrawText(text, new Point(band.X + 4, band.Y + 3));
    }
}
