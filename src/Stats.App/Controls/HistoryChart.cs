using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Stats.App.Helpers;
using Stats.Core.Metrics;

namespace Stats.App.Controls;

/// <summary>
/// The tile-detail history chart: like Sparkline but with y-axis (min/mid/max) and time-axis (oldest…now)
/// labels, warn/crit guide lines from the effective threshold rule, and a hover crosshair that reports both
/// value and time. Values is IReadOnlyList&lt;float&gt; (array-typed DPs can't be bound inside DataTemplates —
/// MC4102). A NaN entry is a recorded gap (see MetricHistory.Add): the min/max range and the polyline/fill all
/// ignore it, and the line/fill are broken into one figure per run of finite samples so a gap is a visible break.
/// Samples are laid out on the fixed <see cref="SampleAxis"/> (right-anchored, constant spacing) — see
/// <see cref="Capacity"/>. Smoothing/glow/pulse follow <see cref="GraphStyle"/>, same as Sparkline.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(HistoryChart), new PropertyMetadata(""));

    /// <summary>Formats the hover label for a sample index — bound to MetricDetailViewModel.HoverTextProvider so
    /// the crosshair uses the same ValueFormatter as the y-axis labels and the header (v1.8 review); falls back to
    /// a plain "{value} {Unit}" when unset.</summary>
    public static readonly DependencyProperty HoverTextProviderProperty = DependencyProperty.Register(
        nameof(HoverTextProvider), typeof(Func<int, string>), typeof(HistoryChart), new PropertyMetadata(null));

    public static readonly DependencyProperty SecondsPerSampleProperty = DependencyProperty.Register(
        nameof(SecondsPerSample), typeof(double), typeof(HistoryChart), new PropertyMetadata(1.0));

    public static readonly DependencyProperty WarnValueProperty = DependencyProperty.Register(
        nameof(WarnValue), typeof(float?), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CritValueProperty = DependencyProperty.Register(
        nameof(CritValue), typeof(float?), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimeAxisLabelsProperty = DependencyProperty.Register(
        nameof(TimeAxisLabels), typeof(IReadOnlyList<string>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YAxisLabelsProperty = DependencyProperty.Register(
        nameof(YAxisLabels), typeof(IReadOnlyList<string>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ring-buffer capacity behind <see cref="Values"/> — 0 (default) behaves as before (samples
    /// stretched across the full plot width). See <see cref="SampleAxis"/>.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(HistoryChart),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0→1 progress of the last-value pulse ring; started by <see cref="OnValuesChanged"/> and driven by
    /// a <see cref="DoubleAnimation"/>, never a timer — idle (no new sample) costs nothing.</summary>
    private static readonly DependencyProperty PulseProgressProperty = DependencyProperty.Register(
        nameof(PulseProgress), typeof(double), typeof(HistoryChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 50, RightMargin = 8, TopMargin = 8, BottomMargin = 20;
    private static readonly Typeface Typeface = new("Segoe UI");

    // Rebuilt from the theme's Text/Warn/Crit colours whenever the theme changes — see Sparkline for why these
    // can't just be {DynamicResource}-bound brushes (this control draws everything itself in OnRender).
    private Brush _textBrush = Brushes.Gray;
    private Pen _guidePen;
    private Pen _hoverLinePen;
    private Brush _hoverDotBrush;
    private Pen _warnPen;
    private Pen _critPen;
    private int _hoverIndex = -1;

    public HistoryChart()
    {
        (_guidePen, _hoverLinePen, _hoverDotBrush, _warnPen, _critPen) = BuildThemeBrushes(out _textBrush);
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

    private void OnThemeChanged()
    {
        (_guidePen, _hoverLinePen, _hoverDotBrush, _warnPen, _critPen) = BuildThemeBrushes(out _textBrush);
        InvalidateVisual();
    }

    private void OnGraphStyleChanged() => InvalidateVisual();

    private static (Pen Guide, Pen HoverLine, Brush HoverDot, Pen Warn, Pen Crit) BuildThemeBrushes(out Brush textBrush)
    {
        var text = ThemeManager.Get("TextSecondary");
        textBrush = Frozen(text);
        var accentText = ThemeManager.Get("TextPrimary");
        var guide = new Pen(Frozen(Color.FromArgb(0x30, accentText.R, accentText.G, accentText.B)), 0.75) { DashStyle = DashStyles.Dash };
        guide.Freeze();
        var hoverLine = new Pen(Frozen(Color.FromArgb(0x60, accentText.R, accentText.G, accentText.B)), 1);
        hoverLine.Freeze();
        Brush hoverDot = Frozen(accentText);
        var warnColor = ThemeManager.Get("WarnBrush");
        var warn = new Pen(Frozen(Color.FromArgb(0x90, warnColor.R, warnColor.G, warnColor.B)), 1) { DashStyle = DashStyles.Dash };
        warn.Freeze();
        var critColor = ThemeManager.Get("CritBrush");
        var crit = new Pen(Frozen(Color.FromArgb(0x90, critColor.R, critColor.G, critColor.B)), 1) { DashStyle = DashStyles.Dash };
        crit.Freeze();
        return (guide, hoverLine, hoverDot, warn, crit);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public IReadOnlyList<float>? Values { get => (IReadOnlyList<float>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public Func<int, string>? HoverTextProvider { get => (Func<int, string>?)GetValue(HoverTextProviderProperty); set => SetValue(HoverTextProviderProperty, value); }
    public double SecondsPerSample { get => (double)GetValue(SecondsPerSampleProperty); set => SetValue(SecondsPerSampleProperty, value); }
    public float? WarnValue { get => (float?)GetValue(WarnValueProperty); set => SetValue(WarnValueProperty, value); }
    public float? CritValue { get => (float?)GetValue(CritValueProperty); set => SetValue(CritValueProperty, value); }
    public IReadOnlyList<string>? TimeAxisLabels { get => (IReadOnlyList<string>?)GetValue(TimeAxisLabelsProperty); set => SetValue(TimeAxisLabelsProperty, value); }
    public IReadOnlyList<string>? YAxisLabels { get => (IReadOnlyList<string>?)GetValue(YAxisLabelsProperty); set => SetValue(YAxisLabelsProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    private double PulseProgress { get => (double)GetValue(PulseProgressProperty); set => SetValue(PulseProgressProperty, value); }

    /// <summary>Starts the last-value pulse ring when a new sample arrives (a changed last-finite value, or a
    /// changed count) and motion is allowed. One short DoubleAnimation per poll tick that finishes and stops
    /// (FillBehavior.Stop) — nothing runs between ticks, so idle cost is zero.</summary>
    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!GraphStyle.Motion) return;
        var ctrl = (HistoryChart)d;
        var oldValues = e.OldValue as IReadOnlyList<float>;
        var newValues = e.NewValue as IReadOnlyList<float>;
        if (newValues is null) return;
        bool countChanged = (oldValues?.Count ?? -1) != newValues.Count;
        bool lastChanged = LastFinite(oldValues) != LastFinite(newValues);
        if (!countChanged && !lastChanged) return;
        ctrl.BeginAnimation(PulseProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { FillBehavior = FillBehavior.Stop });
    }

    private static float? LastFinite(IReadOnlyList<float>? values)
    {
        if (values is null) return null;
        for (int i = values.Count - 1; i >= 0; i--) { if (!float.IsNaN(values[i])) return values[i]; }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var values = Values;
        double plotW = ActualWidth - LeftMargin - RightMargin;
        if (values is null || values.Count < 2 || plotW <= 0) return;
        double px = e.GetPosition(this).X - LeftMargin;
        int idx = (int)Math.Round(px / plotW * (values.Count - 1));
        idx = Math.Clamp(idx, 0, values.Count - 1);
        if (idx == _hoverIndex) return;
        _hoverIndex = idx;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Maximal runs of consecutive finite (non-NaN) samples, oldest first — see Sparkline.FiniteRuns.</summary>
    private static IEnumerable<(int Start, int Length)> FiniteRuns(IReadOnlyList<float> values)
    {
        int start = -1;
        for (int i = 0; i < values.Count; i++)
        {
            if (float.IsNaN(values[i]))
            {
                if (start >= 0) { yield return (start, i - start); start = -1; }
            }
            else if (start < 0) start = i;
        }
        if (start >= 0) yield return (start, values.Count - start);
    }

    /// <summary>Decimated pixel points for one finite run (same stride logic and final-sample fix-up as before),
    /// shared by the fill and line figures so a smoothed fill hugs the same curve as the line.</summary>
    private static List<Point> RunPoints(int start, int last, int stride, Func<int, Point> at)
    {
        var pts = new List<Point>();
        for (int i = start; i <= last; i += stride) pts.Add(at(i));
        if ((last - start) % stride != 0) pts.Add(at(last));
        return pts;
    }

    /// <summary>Emits the curve for one run's decimated points into an open figure (the first point is assumed
    /// already placed by a leading LineTo) — monotone-cubic Béziers when smoothing is on and the run has 3+
    /// points, otherwise straight LineTo segments. A single-point run (n &lt;= 1) draws nothing more.</summary>
    private static void EmitCurve(StreamGeometryContext ctx, List<Point> pts, bool smooth, bool isStroked)
    {
        int n = pts.Count;
        if (n <= 1) return;
        if (!smooth || n == 2)
        {
            for (int i = 1; i < n; i++) ctx.LineTo(pts[i], isStroked, false);
            return;
        }

        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = pts[i].X; ys[i] = pts[i].Y; }
        var tangents = new double[n];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        for (int i = 1; i < n; i++)
        {
            var (c1x, c1y, c2x, c2y) = CurveSmoothing.BezierControlPoints(xs[i - 1], ys[i - 1], tangents[i - 1], xs[i], ys[i], tangents[i]);
            ctx.BezierTo(new Point(c1x, c1y), new Point(c2x, c2y), pts[i], isStroked, true);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test surface for hover

        double plotLeft = LeftMargin, plotTop = TopMargin;
        double plotW = Math.Max(0, w - LeftMargin - RightMargin);
        double plotH = Math.Max(0, h - TopMargin - BottomMargin);

        var yLabels = YAxisLabels ?? Array.Empty<string>();
        if (yLabels.Count > 0) DrawYLabel(dc, yLabels[0], plotLeft - 4, plotTop, VerticalAlignment.Top);
        if (yLabels.Count > 1) DrawYLabel(dc, yLabels[1], plotLeft - 4, plotTop + plotH / 2, VerticalAlignment.Center);
        if (yLabels.Count > 2) DrawYLabel(dc, yLabels[2], plotLeft - 4, plotTop + plotH, VerticalAlignment.Bottom);

        var xLabels = TimeAxisLabels ?? Array.Empty<string>();
        for (int i = 0; i < xLabels.Count; i++)
        {
            double frac = xLabels.Count == 1 ? 0 : (double)i / (xLabels.Count - 1);
            DrawXLabel(dc, xLabels[i], plotLeft + plotW * frac, plotTop + plotH + 2);
        }

        var values = Values;
        if (values is null || values.Count < 2 || plotW <= 0 || plotH <= 0) return;

        // Range is computed over finite samples only — a gap (NaN) must never distort the y-scale.
        float min = float.NaN, max = float.NaN;
        for (int i = 0; i < values.Count; i++)
        {
            float v = values[i];
            if (float.IsNaN(v)) continue;
            if (float.IsNaN(min) || v < min) min = v;
            if (float.IsNaN(max) || v > max) max = v;
        }
        if (float.IsNaN(min)) return; // every sample is a gap — nothing to draw
        float range = max - min;
        if (range < 1e-6f) range = 1f;

        double X(int i) => SampleAxis.X(i, values.Count, Capacity, plotLeft, plotW);
        double Y(float v) => plotTop + plotH - (v - min) / range * plotH;
        Point At(int i) => new(X(i), Y(values[i]));

        bool smooth = GraphStyle.SmoothLines;
        bool effects = GraphStyle.Effects;

        DrawGuide(dc, WarnValue, min, max, Y, plotLeft, plotW, _warnPen);
        DrawGuide(dc, CritValue, min, max, Y, plotLeft, plotW, _critPen);

        int stride = Math.Max(1, values.Count / Math.Max(1, (int)(plotW * 2)));
        var runs = FiniteRuns(values).ToList();
        var runPoints = runs.Select(r => RunPoints(r.Start, r.Start + r.Length - 1, stride, At)).ToList();

        // fill — one closed figure per finite run so a NaN run leaves a visible gap instead of bridging it. Uses
        // the same (possibly smoothed) curve as the line so the gradient hugs it.
        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            for (int r = 0; r < runs.Count; r++)
            {
                var pts = runPoints[r];
                if (pts.Count == 0) continue;
                ctx.BeginFigure(new Point(pts[0].X, plotTop + plotH), true, true);
                ctx.LineTo(pts[0], false, false);
                EmitCurve(ctx, pts, smooth, false);
                ctx.LineTo(new Point(pts[^1].X, plotTop + plotH), false, false);
            }
        }
        fill.Freeze();
        dc.DrawGeometry(FillBrushFor(Stroke, effects), null, fill);

        // line — same per-run breakdown as the fill, above.
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            for (int r = 0; r < runs.Count; r++)
            {
                var pts = runPoints[r];
                if (pts.Count == 0) continue;
                ctx.BeginFigure(pts[0], false, false);
                EmitCurve(ctx, pts, smooth, true);
            }
        }
        line.Freeze();

        // glow pass — same geometry, wide/low-alpha pen, drawn before the main line.
        if (effects) dc.DrawGeometry(null, GlowPenFor(Stroke), line);

        dc.DrawGeometry(null, new Pen(Stroke, 1.75) { LineJoin = PenLineJoin.Round }, line);

        // last-value dot — the newest *real* sample, which may not be the newest slot if it's currently a gap.
        int lastFinite = -1;
        for (int i = values.Count - 1; i >= 0; i--) { if (!float.IsNaN(values[i])) { lastFinite = i; break; } }
        if (lastFinite >= 0)
        {
            var dotCenter = new Point(X(lastFinite), Y(values[lastFinite]));
            dc.DrawEllipse(Stroke, null, dotCenter, 3, 3);

            // pulse ring — only while an animation is actually in flight (0 < p < 1); idle cost is zero.
            double p = PulseProgress;
            if (effects && p > 0 && p < 1)
            {
                double radius = 2.5 + 6.5 * p;
                byte alpha = (byte)Math.Round(0.6 * (1 - p) * 255);
                dc.DrawEllipse(null, PulsePenFor(Stroke, alpha), dotCenter, radius, radius);
            }
        }

        // hover crosshair + value/time label — a gap sample still shows the crosshair (so the user can see where
        // the gap is) but no dot, and the label reports "—" for its value (see HoverLabel).
        if (_hoverIndex >= 0 && _hoverIndex < values.Count)
        {
            double hx = X(_hoverIndex);
            dc.DrawLine(_hoverLinePen, new Point(hx, plotTop), new Point(hx, plotTop + plotH));
            if (!float.IsNaN(values[_hoverIndex]))
                dc.DrawEllipse(_hoverDotBrush, null, new Point(hx, Y(values[_hoverIndex])), 3.5, 3.5);
            DrawHoverLabel(dc, HoverLabel(values, _hoverIndex), hx, plotTop, plotLeft, plotW);
        }
    }

    private static void DrawGuide(DrawingContext dc, float? value, float min, float max, Func<float, double> y,
        double plotLeft, double plotW, Pen pen)
    {
        if (value is not float gv || gv < min || gv > max) return;
        double gy = y(gv);
        dc.DrawLine(pen, new Point(plotLeft, gy), new Point(plotLeft + plotW, gy));
    }

    private string HoverLabel(IReadOnlyList<float> values, int idx)
    {
        if (HoverTextProvider is { } provider) { var text = provider(idx); if (text.Length > 0) return text; }
        float v = values[idx];
        string valueText = float.IsNaN(v) ? "—" : string.Create(CultureInfo.InvariantCulture, $"{v:F1} {Unit}").TrimEnd();
        double secondsAgo = (values.Count - 1 - idx) * SecondsPerSample;
        string when = secondsAgo < 0.5 ? "now" : "-" + HistoryCapacity.FormatWindow(secondsAgo);
        return $"{valueText} at {when}";
    }

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private void DrawYLabel(DrawingContext dc, string text, double right, double y, VerticalAlignment valign)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, 10, _textBrush, PixelsPerDip);
        double ty = valign switch { VerticalAlignment.Top => y, VerticalAlignment.Bottom => y - ft.Height, _ => y - ft.Height / 2 };
        dc.DrawText(ft, new Point(right - ft.Width, ty));
    }

    private void DrawXLabel(DrawingContext dc, string text, double centerX, double y)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, 10, _textBrush, PixelsPerDip);
        double tx = Math.Clamp(centerX - ft.Width / 2, 0, Math.Max(0, ActualWidth - ft.Width));
        dc.DrawText(ft, new Point(tx, y));
    }

    private void DrawHoverLabel(DrawingContext dc, string text, double hx, double top, double plotLeft, double plotW)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, 11, _hoverDotBrush, PixelsPerDip);
        double tx = Math.Clamp(hx - ft.Width / 2, plotLeft, plotLeft + plotW - ft.Width);
        dc.DrawText(ft, new Point(tx, Math.Max(0, top - ft.Height - 2)));
    }

    private static Brush FillBrushFor(Brush stroke, bool effects)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        byte top = effects ? (byte)0x58 : (byte)0x40;
        var b = new LinearGradientBrush(
            Color.FromArgb(top, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        b.Freeze();
        return b;
    }

    private static Pen GlowPenFor(Brush stroke)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B));
        brush.Freeze();
        var pen = new Pen(brush, 4.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static Pen PulsePenFor(Brush stroke, byte alpha)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        var pen = new Pen(brush, 1.5);
        pen.Freeze();
        return pen;
    }
}
