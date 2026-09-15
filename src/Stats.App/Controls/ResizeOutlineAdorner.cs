using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Stats.App.Controls;

/// <summary>Tile-resize-by-drag live feedback (owner decision assumed §4), shown over the Free/Snap tile
/// container while a resize grip drag is in flight — the real tile is never touched until
/// <c>DashboardWindow.CommitFreeResize</c> applies the candidate size on release. Draws (a) the raw dragged
/// rectangle as a thin dashed <c>BorderDim</c> outline and (b) the nearest S/M/L preset's rectangle as a thicker
/// <c>AccentBrush</c> outline with the preset's letter in the corner.
///
/// Same pattern as <see cref="InsertionAdorner"/>: <c>IsHitTestVisible=false</c>, and brushes are read fresh from
/// <c>Application.Current.Resources</c> on every render rather than cached — <c>ThemeManager.Apply</c> REPLACES
/// the brush entries wholesale on a live theme switch rather than mutating them in place, so a cached reference
/// would go stale.
///
/// Both rectangles passed to <see cref="Update"/> share the same coordinate space as the drag math itself — origin
/// at the tile's own top-left, sized to the dragged/candidate width and height — and are offset here by
/// <see cref="ContainerMargin"/> (<c>TileBorder</c>'s own 6-unit <c>Margin</c>, <c>TileTemplates.xaml</c>) so the
/// drawn outlines land on the tile's actual visual chrome rather than the (unmargined) container bounds.</summary>
public sealed class ResizeOutlineAdorner : Adorner
{
    /// <summary><c>TileBorder</c>'s <c>Margin</c> (<c>TileTemplates.xaml</c>) — the offset between the container's
    /// top-left (where <c>Canvas.Left</c>/<c>Top</c>, and this adorner's own coordinate space, both start) and the
    /// tile's visible border.</summary>
    private const double ContainerMargin = 6.0;

    private Rect _rawRect;
    private Rect _candidateRect;
    private string _candidateLabel = "";

    public ResizeOutlineAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    /// <summary>Called on every <c>PreviewMouseMove</c> while a resize is in flight (see
    /// <c>DashboardWindow.UpdateFreeResize</c>). Both rectangles are (0,0)-origin — see the class remarks on the
    /// shared coordinate space — only their width/height differ between the raw drag and the candidate preset.</summary>
    public void Update(Rect rawRect, Rect candidateRect, string candidateLabel)
    {
        _rawRect = rawRect;
        _candidateRect = candidateRect;
        _candidateLabel = candidateLabel;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var resources = Application.Current?.Resources;
        var borderDim = resources?["BorderDim"] as Brush ?? Brushes.Gray;
        var accent = resources?["AccentBrush"] as Brush ?? Brushes.Orange;
        var textPrimary = resources?["TextPrimary"] as Brush ?? Brushes.White;

        var rawPen = new Pen(borderDim, 1) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
        drawingContext.DrawRectangle(null, rawPen, Offset(_rawRect));

        var candidatePen = new Pen(accent, 1.5);
        var candidateRect = Offset(_candidateRect);
        drawingContext.DrawRectangle(null, candidatePen, candidateRect);

        if (string.IsNullOrEmpty(_candidateLabel)) return;
        var text = new FormattedText(_candidateLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, textPrimary, VisualTreeHelper.GetDpi(AdornedElement).PixelsPerDip);
        drawingContext.DrawText(text, new Point(candidateRect.Left + 4, candidateRect.Top + 2));
    }

    private static Rect Offset(Rect r) =>
        new(r.X + ContainerMargin, r.Y + ContainerMargin, Math.Max(0, r.Width), Math.Max(0, r.Height));
}
