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
/// <see cref="Capacity"/>. Smoothing/glow/pulse follow <see cref="GraphStyle"/>, same as Sparkline (both share
/// their curve/geometry-building code via <see cref="CurveRenderer"/>).
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

    // S1/S5 render cache — see Sparkline's copy of this comment for why: rebuilt only when Values,
    // ActualWidth/ActualHeight, Stroke, Capacity or GraphStyle change, not on every OnRender (in particular not
    // on every pulse-animation frame).
    private StreamGeometry? _fillGeometry;
    private StreamGeometry? _lineGeometry;
    private Brush? _fillBrush;
    private Pen? _glowPen;
    private Pen? _linePen;
    private readonly List<Point> _singlePointDots = new();
    private float _cachedMin, _cachedMax, _cachedRange;
    private bool _hasData;
    private int _lastFiniteIndex = -1;

    private IReadOnlyList<float>? _cacheValues;
    private double _cacheWidth = -1, _cacheHeight = -1;
    private Brush? _cacheStroke;
    private int _cacheCapacity = -1;
    private bool _cacheSmooth, _cacheEffects;

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
        var ctrl = (HistoryChart)d;
        ctrl._cacheValues = null;
        if (!GraphStyle.Motion) return;
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
        int idx = SampleAxis.IndexAt(e.GetPosition(this).X, values.Count, Capacity, LeftMargin, plotW);
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

    /// <summary>See Sparkline.RebuildGeometry — same cache-rebuild responsibility, adapted for this control's
    /// plotLeft/plotW/plotTop/plotH margins.</summary>
    private void RebuildGeometry(IReadOnlyList<float> values, double plotLeft, double plotTop, double plotW, double plotH, Brush stroke, bool smooth, bool effects)
    {
        _singlePointDots.Clear();
        _lastFiniteIndex = -1;

        float min = float.NaN, max = float.NaN;
        for (int i = 0; i < values.Count; i++)
        {
            float v = values[i];
            if (float.IsNaN(v)) continue;
            if (float.IsNaN(min) || v < min) min = v;
            if (float.IsNaN(max) || v > max) max = v;
            _lastFiniteIndex = i;
        }

        _hasData = !float.IsNaN(min);
        if (!_hasData)
        {
            _fillGeometry = null; _lineGeometry = null; _fillBrush = null; _glowPen = null; _linePen = null;
            return;
        }

        _cachedMin = min; _cachedMax = max;
        float range = max - min;
        if (range < 1e-6f) range = 1f;
        _cachedRange = range;

        double X(int i) => SampleAxis.X(i, values.Count, Capacity, plotLeft, plotW);
        double Y(float v) => plotTop + plotH - (v - min) / range * plotH;
        Point At(int i) => new(X(i), Y(values[i]));

        int stride = Math.Max(1, values.Count / Math.Max(1, (int)(plotW * 2)));
        var runs = CurveRenderer.FiniteRuns(values).ToList();
        var runPoints = runs.Select(r => CurveRenderer.RunPoints(r.Start, r.Start + r.Length - 1, stride, At)).ToList();

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
                CurveRenderer.EmitCurve(ctx, pts, smooth, false);
                ctx.LineTo(new Point(pts[^1].X, plotTop + plotH), false, false);
            }
        }
        fill.Freeze();
        _fillGeometry = fill;

        // line — same per-run breakdown as the fill, above. A single-point run draws no segment; its point is
        // recorded for a small dot in OnRender instead (review S4).
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            for (int r = 0; r < runs.Count; r++)
            {
                var pts = runPoints[r];
                if (pts.Count == 0) continue;
                ctx.BeginFigure(pts[0], false, false);
                CurveRenderer.EmitCurve(ctx, pts, smooth, true);
                if (pts.Count == 1) _singlePointDots.Add(pts[0]);
            }
        }
        line.Freeze();
        _lineGeometry = line;

        _fillBrush = CurveRenderer.FillBrushFor(stroke, 0x40, 0x58, effects);
        _glowPen = effects ? CurveRenderer.GlowPenFor(stroke) : null;
        var linePen = new Pen(stroke, 1.75) { LineJoin = PenLineJoin.Round };
        linePen.Freeze();
        _linePen = linePen;
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

        bool smooth = GraphStyle.SmoothLines;
        bool effects = GraphStyle.Effects;
        var stroke = Stroke;

        if (!ReferenceEquals(_cacheValues, values) || _cacheWidth != w || _cacheHeight != h ||
            !ReferenceEquals(_cacheStroke, stroke) || _cacheCapacity != Capacity ||
            _cacheSmooth != smooth || _cacheEffects != effects)
        {
            RebuildGeometry(values, plotLeft, plotTop, plotW, plotH, stroke, smooth, effects);
            _cacheValues = values; _cacheWidth = w; _cacheHeight = h; _cacheStroke = stroke;
            _cacheCapacity = Capacity; _cacheSmooth = smooth; _cacheEffects = effects;
        }

        double X(int i) => SampleAxis.X(i, values.Count, Capacity, plotLeft, plotW);
        double Y(float v) => plotTop + plotH - (v - _cachedMin) / _cachedRange * plotH;

        DrawGuide(dc, WarnValue, _hasData ? _cachedMin : float.NaN, _hasData ? _cachedMax : float.NaN, Y, plotLeft, plotW, _warnPen);
        DrawGuide(dc, CritValue, _hasData ? _cachedMin : float.NaN, _hasData ? _cachedMax : float.NaN, Y, plotLeft, plotW, _critPen);

        if (!_hasData) return; // every sample is a gap — nothing more to draw

        dc.DrawGeometry(_fillBrush, null, _fillGeometry);

        // glow pass — same geometry, wide/low-alpha pen, drawn before the main line.
        if (effects && _glowPen is not null) dc.DrawGeometry(null, _glowPen, _lineGeometry);

        dc.DrawGeometry(null, _linePen, _lineGeometry);

        // single-point finite runs (review S4) — a small dot instead of nothing.
        foreach (var pt in _singlePointDots) dc.DrawEllipse(stroke, null, pt, 1.25, 1.25);

        // last-value dot — the newest *real* sample, which may not be the newest slot if it's currently a gap.
        if (_lastFiniteIndex >= 0)
        {
            var dotCenter = new Point(X(_lastFiniteIndex), Y(values[_lastFiniteIndex]));
            dc.DrawEllipse(stroke, null, dotCenter, 3, 3);

            // pulse ring — only while an animation is actually in flight (0 < p < 1); idle cost is zero.
            double p = PulseProgress;
            if (effects && p > 0 && p < 1)
            {
                double radius = 2.5 + 6.5 * p;
                byte alpha = (byte)Math.Round(0.6 * (1 - p) * 255);
                dc.DrawEllipse(null, CurveRenderer.PulsePenFor(stroke, alpha), dotCenter, radius, radius);
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
        if (value is not float gv || float.IsNaN(min) || gv < min || gv > max) return;
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
}
