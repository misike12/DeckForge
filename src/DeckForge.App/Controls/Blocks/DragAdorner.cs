using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DeckForge.App.ViewModels.Visual;
using DeckForge.Core.Visual;

namespace DeckForge.App.Controls.Blocks;

/// <summary>
/// Draws the drag: the ghost following the pointer and the indicator where it would land.
/// </summary>
/// <remarks>
/// <para>
/// An adorner rather than an element in the canvas, for three reasons that all came from the same place —
/// the dragged block is a *view* of a run that is still in the document, so putting it in the tree would
/// mean a second copy of it in a live <c>ItemsControl</c>, and the drop would then have to work out which
    /// copy the user was pointing at. An adorner draws outside the tree entirely, so the canvas cannot
/// tell that a drag is happening, and the document is only touched at the drop.
/// </para>
/// <para>
/// It draws the run's real silhouettes through <see cref="BlockOutline"/>, at the position the resolver
/// snapped to, so the ghost is the block the user is dragging rather than a rectangle standing in for it.
/// A ghost that is obviously not the block is worse than none: it teaches the eye to ignore it.
/// </para>
/// </remarks>
public sealed class DragAdorner : Adorner
{
    private readonly DragController _drag;
    private readonly VisualEditorViewModel _vm;

    public DragAdorner(UIElement target, DragController drag, VisualEditorViewModel vm)
        : base(target)
    {
        _drag = drag;
        _vm = vm;
        IsHitTestVisible = false;
    }

    /// <summary>How transparent the ghost is while being dragged.</summary>
    /// <remarks>
    /// Part 9.5 asks for a semi-transparent copy. It is drawn at 60% rather than lower because the ghost
    /// overlaps the blocks it is about to join, and a fainter one would hide the notch it is lining up.
    /// </remarks>
    private const double GhostOpacity = 0.6;

    /// <summary>How thick the insertion indicator is, in pixels.</summary>
    private const double IndicatorThickness = 4;

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        if (_drag is { IsActive: true, Resolution: { } resolution })
        {
            DrawGhost(context, resolution);
            DrawIndicator(context, resolution.Candidate);
        }
    }

    /// <summary>
    /// The dragged run, at the snapped position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drawn from the real <see cref="BlockOutline"/> silhouettes rather than as a tinted rectangle, and
    /// laid out with <see cref="StackLayout.LayoutRun"/> so a C-block in the ghost is as tall as the C-block
    /// on screen. A ghost whose pitch does not match the stack it is about to join tells the user the
    /// whole drag is approximate, which is the one impression a snapping ghost exists to remove — and the
    /// pitch is the thing that makes a long run look like the right length.
    /// </para>
    /// <para>
    /// The outline is the block's own stroke colour rather than white. White at this weight reads as a
    /// wireframe selection rather than as a block, and a ghost that looks like something other than a
    /// block teaches the eye to ignore the thing that is showing where it will land.
    /// </para>
    /// </remarks>
    private void DrawGhost(DrawingContext context, DropResolution resolution)
    {
        var run = _drag.Ghost;
        if (run.Count == 0)
        {
            return;
        }

        var rects = StackLayout.LayoutRun(run);
        var top = resolution.SnapY;

        foreach (var block in run)
        {
            var descriptor = BlockCatalog.Find(block.Kind);
            var category = descriptor?.Category ?? BlockCategory.Control;

            if (rects.TryGetValue(block.Id, out var rect) && BlockShapeGeometry.ToGeometry(
                    BlockOutline.Create(descriptor?.Shape ?? BlockShape.Placeholder, rect.Width, rect.Height))
                is { } outline)
            {
                var fill = Resolve(FillKey(category)) ?? Brushes.LightGray;
                var stroke = Resolve(StrokeKey(category)) ?? Brushes.White;

                context.PushOpacity(GhostOpacity);
                context.DrawGeometry(fill, new Pen(stroke, 1.25), outline);
                context.Pop();
            }

            top += (rects.TryGetValue(block.Id, out var placed) ? placed.Height : BlockMetrics.MinTileHeight)
                + BlockMetrics.StackGap;
        }
    }

    /// <summary>
    /// The insertion indicator: a bar for a gap or a mouth, a ring for a hole.
    /// </summary>
    /// <remarks>
    /// A bar in the accent colour rather than a gap in the stack, because opening a real gap would mean
    /// reflowing the canvas on every pointer move — and reflowing on every pointer move is what makes a
    /// canvas feel like it is fighting the pointer. The bar says the same thing without moving anything.
    /// </remarks>
    private void DrawIndicator(DrawingContext context, DropCandidate target)
    {
        var accent = Resolve(AccentBrushKey) ?? Brushes.White;

        if (target.Kind is DropTargetKind.Canvas or DropTargetKind.HatSlot)
        {
            // Nothing to point at: the drop creates a script rather than joining one, so a ghost with no
            // indicator under it is the honest picture.
            return;
        }

        var width = IndicatorWidth(target);
        var rect = new Rect(target.X, target.Y - (IndicatorThickness / 2), width, IndicatorThickness);
        context.DrawRoundedRectangle(accent, null, rect, IndicatorThickness / 2, IndicatorThickness / 2);
    }

    /// <summary>
    /// How wide the indicator is: the width of the body it opens into.
    /// </summary>
    /// <remarks>
    /// Measured from the block that owns the body when it can be found, and a fixed span when it cannot —
    /// a free script body has no owning block, and its gap still has to be visible.
    /// </remarks>
    private double IndicatorWidth(DropCandidate target)
    {
        const double DefaultWidth = 180;

        var owner = DocumentLists.Find(_vm.Editor.Project, target.ParentId);
        if (owner is null)
        {
            return DefaultWidth;
        }

        var descriptor = BlockCatalog.Find(owner.Kind);
        return descriptor is null ? DefaultWidth : BlockMetrics.EstimateWidth(descriptor);
    }

    private const string AccentBrushKey = "Liquid.AccentBrush";

    private static string FillKey(BlockCategory category) => BlockTheme.FillKey(category);

    private static string StrokeKey(BlockCategory category) => BlockTheme.StrokeKey(category);

    /// <summary>
    /// A theme brush by key, or null when the theme has none.
    /// </summary>
    /// <remarks>
    /// Null rather than a grey stand-in in the ghost, unlike <see cref="BlockTile"/>: a ghost that is a
    /// flat grey rectangle is still a rectangle, and the fallback is only reached when the theme is missing
    /// a key the tiles themselves already cope without — so there is nothing to say about it here.
    /// </remarks>
    private static Brush? Resolve(string key) => Application.Current?.TryFindResource(key) as Brush;
}
