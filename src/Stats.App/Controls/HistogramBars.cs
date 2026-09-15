using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>
/// Twelve-bin (generally: <c>Bins.Count</c>-bin) histogram with a thin percentile marker line and a control-drawn
/// label. Same skeleton as <see cref="Sparkline"/>/<see cref="LevelBar"/>: DPs registered
/// <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/>, <see cref="ThemeManager.Changed"/>/
/// <see cref="GraphStyle.Changed"/> subscribed on Loaded (unsubscribed first for idempotency) and unsubscribed on
/// Unloaded, cached geometry/pens rebuilt only when the values they're derived from change, no animation, no
/// timer. No hover/tooltip (non-goal), so no transparent hit-test rectangle is needed.
/// </summary>
public sealed class HistogramBars : FrameworkElement
{
    /// <summary>Bar heights, lowest-value bin first. Array-typed DPs cannot be bound inside DataTemplates
    /// (MC4102), hence <see cref="IReadOnlyList{T}"/> — <c>MetricTileViewModel.HistogramBins</c> (an
    /// <c>int[]</c>) satisfies it directly.</summary>
    public static readonly DependencyProperty BinsProperty = DependencyProperty.Register(
        nameof(Bins), typeof(IReadOnlyList<int>), typeof(HistogramBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBinsChanged));

    /// <summary>Marker x as a fraction of width. NaN or outside [0, 1] draws no marker/label.</summary>
    public static readonly DependencyProperty MarkerFractionProperty = DependencyProperty.Register(
        nameof(MarkerFraction), typeof(double), typeof(HistogramBars),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Text drawn next to the marker (e.g. "p99 17.6 ms"); "" draws no label (but the marker line still
    /// draws, from y = 0 instead of leaving room for a label row).</summary>
    public static readonly DependencyProperty MarkerLabelProperty = DependencyProperty.Register(
        nameof(MarkerLabel), typeof(string), typeof(HistogramBars),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bar and marker colour — bound through SeverityToBrushConverter (parameter "accent") in
    /// TileTemplates.xaml, same as every other custom-drawn tile control.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistogramBars),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>1px baseline colour under the bars — same default as <see cref="LevelBar.Track"/>.</summary>
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(HistogramBars),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int>? Bins { get => (IReadOnlyList<int>?)GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    public double MarkerFraction { get => (double)GetValue(MarkerFractionProperty); set => SetValue(MarkerFractionProperty, value); }
    public string MarkerLabel { get => (string)GetValue(MarkerLabelProperty); set => SetValue(MarkerLabelProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    // Fixed, theme/stroke-independent highlight colour (LevelBar's 1px top highlight) — built once, never rebuilt.
    private static readonly Pen HighlightPen = BuildHighlightPen();

    // ---- render cache (Sparkline/LevelBar's S1/S5 pattern): everything below is rebuilt only when the inputs it
    // is derived from actually changed, never unconditionally from OnRender. A poll tick only reassigns Bins/
    // MarkerFraction/MarkerLabel when MetricTileViewModel.Refresh runs (once per poll, not per frame), so in
    // steady state OnRender does zero rebuilding — it just replays the cached StreamGeometry/Pens/Brush. ----
    private StreamGeometry? _barsGeometry;
    private StreamGeometry? _highlightGeometry;
    private IReadOnlyList<int>? _cacheBins;
    private double _cacheGeometryWidth = -1, _cacheGeometryHeight = -1;

    private Brush? _fillBrush;
    private Pen? _glowPen;
    private Pen? _markerPen;
    private Brush? _cacheStrokeForColor;
    private bool _cacheEffects;

    private Pen? _trackPen;
    private Brush? _cacheTrack;

    private Brush _labelBrush;

    public HistogramBars()
    {
        _labelBrush = BuildLabelBrush();
        // Unloaded isn't raised on app shutdown, and Loaded can fire more than once for the same instance —
        // unsubscribe first so the subscription stays idempotent.
        Loaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged; OnThemeChanged();
            GraphStyle.Changed -= OnGraphStyleChanged; GraphStyle.Changed += OnGraphStyleChanged;
        };
        Unloaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            GraphStyle.Changed -= OnGraphStyleChanged;
        };
    }

    // The marker label is drawn from the theme's TextSecondary colour directly (not through a Binding/converter),
    // so — like Sparkline's guide/hover overlays — it needs an explicit rebuild to stay in step with a live theme
    // switch. Stroke/Track are bound through a converter/DynamicResource and WPF re-invalidates those on its own.
    private void OnThemeChanged()
    {
        _labelBrush = BuildLabelBrush();
        InvalidateVisual();
    }

    private void OnGraphStyleChanged() => InvalidateVisual();

    private static Brush BuildLabelBrush()
    {
        var b = new SolidColorBrush(ThemeManager.Get("TextSecondary"));
        b.Freeze();
        return b;
    }

    private static Pen BuildHighlightPen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }

    private static void OnBinsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HistogramBars)d)._cacheBins = null;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var bins = Bins;
        var stroke = Stroke;
        var track = Track;
        bool effects = GraphStyle.Effects;

        if (!ReferenceEquals(_cacheBins, bins) || _cacheGeometryWidth != w || _cacheGeometryHeight != h)
        {
            RebuildBars(bins, w, h);
            _cacheBins = bins;
            _cacheGeometryWidth = w; _cacheGeometryHeight = h;
        }
        if (!ReferenceEquals(_cacheStrokeForColor, stroke) || _cacheEffects != effects)
        {
            _fillBrush = CurveRenderer.BarBrushFor(stroke, effects);
            _glowPen = effects ? CurveRenderer.GlowPenFor(stroke) : null;
            var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
            var markerBrush = new SolidColorBrush(Color.FromArgb(0xC0, c.R, c.G, c.B));
            markerBrush.Freeze();
            var markerPen = new Pen(markerBrush, 1);
            markerPen.Freeze();
            _markerPen = markerPen;
            _cacheStrokeForColor = stroke; _cacheEffects = effects;
        }
        if (!ReferenceEquals(_cacheTrack, track))
        {
            var trackPen = new Pen(track, 1);
            trackPen.Freeze();
            _trackPen = trackPen;
            _cacheTrack = track;
        }

        // 1. baseline
        dc.DrawLine(_trackPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

        // 2. bars
        if (_barsGeometry is not null) dc.DrawGeometry(_fillBrush, null, _barsGeometry);
        if (effects && _highlightGeometry is not null) dc.DrawGeometry(null, HighlightPen, _highlightGeometry);

        // 3. marker + 4. label
        double fraction = MarkerFraction;
        if (double.IsNaN(fraction) || fraction < 0 || fraction > 1) return;

        double x = Math.Clamp(fraction * w, 0.5, w - 0.5); // a 1 px line at fraction 0 or 1 would otherwise be half-clipped
        string label = MarkerLabel;
        bool hasLabel = !string.IsNullOrEmpty(label);
        double top = hasLabel ? 12 : 0;

        if (effects && _glowPen is not null) dc.DrawLine(_glowPen, new Point(x, top), new Point(x, h - 1));
        dc.DrawLine(_markerPen, new Point(x, top), new Point(x, h - 1));

        if (!hasLabel) return;

        // Built fresh every render — one render per poll tick (Bins/MarkerFraction/MarkerLabel only change when
        // MetricTileViewModel.Refresh runs), so a FormattedText allocation here costs nothing worth caching for.
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 10, _labelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double lx = fraction > 0.5 ? x - 3 - ft.Width : x + 3;
        lx = Math.Clamp(lx, 0, Math.Max(0, w - ft.Width));
        dc.DrawText(ft, new Point(lx, 0));
    }

    /// <summary>Rebuilds the cached bar-fill geometry and the per-bar top-edge highlight geometry from
    /// <paramref name="bins"/>/<paramref name="w"/>/<paramref name="h"/> — the only three inputs either depends
    /// on. <paramref name="bins"/> null/empty, or every count zero, leaves both null (baseline only).</summary>
    private void RebuildBars(IReadOnlyList<int>? bins, double w, double h)
    {
        _barsGeometry = null;
        _highlightGeometry = null;
        if (bins is null || bins.Count == 0) return;

        int n = bins.Count;
        int maxCount = 0;
        for (int i = 0; i < n; i++) if (bins[i] > maxCount) maxCount = bins[i];
        if (maxCount == 0) return;

        const double gap = 2;
        double barW = (w - gap * (n - 1)) / n;
        if (barW <= 0) return;

        double bottom = h - 1;
        double available = h - 13; // 12px label row + 1px baseline reserved at the top

        var bars = new StreamGeometry();
        var highlights = new StreamGeometry();
        using (var barsCtx = bars.Open())
        using (var highlightCtx = highlights.Open())
        {
            for (int i = 0; i < n; i++)
            {
                int c = bins[i];
                if (c <= 0) continue;
                double barH = Math.Max(0, c / (double)maxCount * available);
                if (barH <= 0) continue;

                double x = i * (barW + gap);
                double top = bottom - barH;

                barsCtx.BeginFigure(new Point(x, bottom), true, true);
                barsCtx.LineTo(new Point(x, top), false, false);
                barsCtx.LineTo(new Point(x + barW, top), false, false);
                barsCtx.LineTo(new Point(x + barW, bottom), false, false);

                if (barH >= 3)
                {
                    highlightCtx.BeginFigure(new Point(x + 1, top + 0.5), false, false);
                    highlightCtx.LineTo(new Point(x + barW - 1, top + 0.5), true, false);
                }
            }
        }
        bars.Freeze();
        highlights.Freeze();
        _barsGeometry = bars;
        _highlightGeometry = highlights;
    }
}
