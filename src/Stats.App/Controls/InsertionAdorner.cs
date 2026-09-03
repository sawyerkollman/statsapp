using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Stats.App.Controls;

/// <summary>Drag-reorder insertion indicator: a 2px AccentBrush vertical line at the adorned tile's left edge —
/// shown by <c>DashboardWindow.Tile_DragOver</c> while dragging over a same-group tile, removed on
/// DragLeave/Drop/drag end (see the single-current-adorner tracking there so this never leaks). Reads
/// AccentBrush directly from Application.Current.Resources on every render rather than caching it, the same
/// rationale as Sparkline/SeverityToBrushConverter: ThemeManager.Apply REPLACES the brush entry wholesale on a
/// live theme switch rather than mutating it in place, so a cached reference would go stale.</summary>
public sealed class InsertionAdorner : Adorner
{
    public InsertionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = Application.Current?.Resources["AccentBrush"] as Brush ?? Brushes.Orange;
        var pen = new Pen(brush, 2);
        double height = AdornedElement is UIElement el ? el.RenderSize.Height : 0;
        if (height <= 0) return;
        drawingContext.DrawLine(pen, new Point(1, 0), new Point(1, height));
    }
}
