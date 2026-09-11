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
/// Polyline sparkline with gradient fill, last-value dot, faint min/max guides and a hover tooltip.
/// Values is IReadOnlyList&lt;float&gt; (array-typed DPs can't be bound inside DataTemplates — MC4102). A NaN
/// entry is a recorded gap (see MetricHistory.Add): the min/max range, polyline and fill all ignore it, and the
/// line/fill are broken into one figure per run of finite samples so a gap is a visible break, not a bridge.
/// Samples are laid out on the fixed <see cref="SampleAxis"/> (right-anchored, constant spacing) rather than
/// stretched across the full width by however many samples currently exist — see <see cref="Capacity"/>. When
/// <see cref="GraphStyle.SmoothLines"/> is on, each finite run is drawn as a monotone-cubic curve (Bézier
/// segments from <see cref="CurveSmoothing"/>) instead of a straight polyline; when <see cref="GraphStyle.Effects"/>
/// is on, the line gets an underlying glow pass, the fill gains a stronger top stop, and the last-value dot
/// pulses on a new sample (subject to <see cref="GraphStyle.Motion"/>, i.e. OS animations enabled).
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Sparkline), new PropertyMetadata(""));

    public static readonly DependencyProperty ShowGuidesProperty = DependencyProperty.Register(
        nameof(ShowGuides), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ring-buffer capacity behind <see cref="Values"/> — 0 (default) behaves as before (samples
    /// stretched across the full width). See <see cref="SampleAxis"/>.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0→1 progress of the last-value pulse ring; started by <see cref="OnValuesChanged"/> and driven by
    /// a <see cref="DoubleAnimation"/>, never a timer — idle (no new sample) costs nothing.</summary>
    private static readonly DependencyProperty PulseProgressProperty = DependencyProperty.Register(
        nameof(PulseProgress), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // Guide lines and the hover crosshair/dot are drawn fresh every OnRender and were a static, always-white
    // translucent overlay — invisible against the Light preset's white TileBg. They're now instance fields
    // derived from the theme's TextPrimary colour (low alpha for the guides/crosshair, full for the hover dot)
    // and rebuilt whenever the theme changes.
    private Pen _guidePen;
    private Pen _hoverLinePen;
    private Brush _hoverDotBrush;
    private int _hoverIndex = -1;

    public Sparkline()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 30000);
        (_guidePen, _hoverLinePen, _hoverDotBrush) = BuildThemeBrushes();
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

    // Stroke is set via a Binding through SeverityToBrushConverter (see TileTemplates.xaml); the composition
    // root (App) re-raises the bound Severity property after every ThemeManager.Apply, which re-runs that
    // Binding and picks up the converter's freshly-fetched brush — no code needed in this control for Stroke/
    // Fill. The guide/hover overlays below are plain (not theme-resource-backed) Pens/Brushes drawn fresh every
    // OnRender, so they need an explicit rebuild here to stay in step with a theme switch.
    private void OnThemeChanged()
    {
        (_guidePen, _hoverLinePen, _hoverDotBrush) = BuildThemeBrushes();
        InvalidateVisual();
    }

    private void OnGraphStyleChanged() => InvalidateVisual();

    private static (Pen Guide, Pen HoverLine, Brush HoverDot) BuildThemeBrushes()
    {
        var c = ThemeManager.Get("TextPrimary");
        var guide = new Pen(Frozen(Color.FromArgb(0x30, c.R, c.G, c.B)), 0.75) { DashStyle = DashStyles.Dash };
        guide.Freeze();
        var hoverLine = new Pen(Frozen(Color.FromArgb(0x60, c.R, c.G, c.B)), 1);
        hoverLine.Freeze();
        Brush hoverDot = Frozen(c);
        return (guide, hoverLine, hoverDot);
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
    public bool ShowGuides { get => (bool)GetValue(ShowGuidesProperty); set => SetValue(ShowGuidesProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    private double PulseProgress { get => (double)GetValue(PulseProgressProperty); set => SetValue(PulseProgressProperty, value); }

    /// <summary>Starts the last-value pulse ring when a new sample arrives (a changed last-finite value, or a
    /// changed count) and motion is allowed. One short DoubleAnimation per poll tick that finishes and stops
    /// (FillBehavior.Stop) — nothing runs between ticks, so idle cost is zero.</summary>
    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!GraphStyle.Motion) return;
        var ctrl = (Sparkline)d;
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
        if (values is null || values.Count < 2 || ActualWidth <= 0) return;
        int idx = (int)Math.Round(e.GetPosition(this).X / ActualWidth * (values.Count - 1));
        idx = Math.Clamp(idx, 0, values.Count - 1);
        if (idx == _hoverIndex) return;
        _hoverIndex = idx;
        // A gap sample has nothing to report — no tooltip, and OnRender skips the crosshair/dot for it too.
        ToolTip = float.IsNaN(values[idx]) ? null
            : string.Create(CultureInfo.InvariantCulture, $"{values[idx]:F1} {Unit}").TrimEnd();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Maximal runs of consecutive finite (non-NaN) samples, oldest first — a NaN sample (a recorded
    /// gap; see MetricHistory.Add) ends one run and starts the search for the next, so callers can draw each run
    /// as its own polyline/fill figure and leave a visible break at every gap.</summary>
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
    /// already placed by BeginFigure/a leading LineTo) — monotone-cubic Béziers when smoothing is on and the run
    /// has 3+ points, otherwise straight LineTo segments. A single-point run (n &lt;= 1) draws nothing more.</summary>
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

        var values = Values;
        if (values is null || values.Count < 2) return;

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

        double X(int i) => SampleAxis.X(i, values.Count, Capacity, 0, w);
        double Y(float v) => h - 2 - (v - min) / range * (h - 4);
        Point At(int i) => new(X(i), Y(values[i]));

        bool smooth = GraphStyle.SmoothLines;
        bool effects = GraphStyle.Effects;

        int stride = Math.Max(1, values.Count / Math.Max(1, (int)(w * 2)));
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
                ctx.BeginFigure(new Point(pts[0].X, h), true, true);
                ctx.LineTo(pts[0], false, false);
                EmitCurve(ctx, pts, smooth, false);
                ctx.LineTo(new Point(pts[^1].X, h), false, false);
            }
        }
        fill.Freeze();
        dc.DrawGeometry(FillBrushFor(Stroke, effects), null, fill);

        // guides
        if (ShowGuides && max - min > 1e-6f)
        {
            dc.DrawLine(_guidePen, new Point(0, Y(max)), new Point(w, Y(max)));
            dc.DrawLine(_guidePen, new Point(0, Y(min)), new Point(w, Y(min)));
        }

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

        dc.DrawGeometry(null, new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);

        // last-value dot — the newest *real* sample, which may not be the newest slot if it's currently a gap.
        int lastFinite = -1;
        for (int i = values.Count - 1; i >= 0; i--) { if (!float.IsNaN(values[i])) { lastFinite = i; break; } }
        if (lastFinite >= 0)
        {
            var dotCenter = new Point(X(lastFinite), Y(values[lastFinite]));
            dc.DrawEllipse(Stroke, null, dotCenter, 2.5, 2.5);

            // pulse ring — only while an animation is actually in flight (0 < p < 1); idle cost is zero.
            double p = PulseProgress;
            if (effects && p > 0 && p < 1)
            {
                double radius = 2.5 + 6.5 * p;
                byte alpha = (byte)Math.Round(0.6 * (1 - p) * 255);
                dc.DrawEllipse(null, PulsePenFor(Stroke, alpha), dotCenter, radius, radius);
            }
        }

        // hover — ignores a gap sample entirely (no crosshair/dot; OnMouseMove already suppressed its tooltip).
        if (_hoverIndex >= 0 && _hoverIndex < values.Count && !float.IsNaN(values[_hoverIndex]))
        {
            double hx = X(_hoverIndex);
            dc.DrawLine(_hoverLinePen, new Point(hx, 0), new Point(hx, h));
            dc.DrawEllipse(_hoverDotBrush, null, new Point(hx, Y(values[_hoverIndex])), 3, 3);
        }
    }

    private static Brush FillBrushFor(Brush stroke, bool effects)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        byte top = effects ? (byte)0x70 : (byte)0x55;
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
