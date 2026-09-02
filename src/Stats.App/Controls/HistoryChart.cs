using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stats.App.Helpers;
using Stats.Core.Metrics;

namespace Stats.App.Controls;

/// <summary>
/// The tile-detail history chart: like Sparkline but with y-axis (min/mid/max) and time-axis (oldest…now)
/// labels, warn/crit guide lines from the effective threshold rule, and a hover crosshair that reports both
/// value and time. Values is IReadOnlyList&lt;float&gt; (array-typed DPs can't be bound inside DataTemplates —
/// MC4102). A NaN entry is a recorded gap (see MetricHistory.Add): the min/max range and the polyline/fill all
/// ignore it, and the line/fill are broken into one figure per run of finite samples so a gap is a visible break.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(HistoryChart), new PropertyMetadata(""));

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
        Loaded += (_, _) => { ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged; OnThemeChanged(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        (_guidePen, _hoverLinePen, _hoverDotBrush, _warnPen, _critPen) = BuildThemeBrushes(out _textBrush);
        InvalidateVisual();
    }

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
    public double SecondsPerSample { get => (double)GetValue(SecondsPerSampleProperty); set => SetValue(SecondsPerSampleProperty, value); }
    public float? WarnValue { get => (float?)GetValue(WarnValueProperty); set => SetValue(WarnValueProperty, value); }
    public float? CritValue { get => (float?)GetValue(CritValueProperty); set => SetValue(CritValueProperty, value); }
    public IReadOnlyList<string>? TimeAxisLabels { get => (IReadOnlyList<string>?)GetValue(TimeAxisLabelsProperty); set => SetValue(TimeAxisLabelsProperty, value); }
    public IReadOnlyList<string>? YAxisLabels { get => (IReadOnlyList<string>?)GetValue(YAxisLabelsProperty); set => SetValue(YAxisLabelsProperty, value); }

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

        double X(int i) => plotLeft + plotW * i / (values.Count - 1);
        double Y(float v) => plotTop + plotH - (v - min) / range * plotH;

        DrawGuide(dc, WarnValue, min, max, Y, plotLeft, plotW, _warnPen);
        DrawGuide(dc, CritValue, min, max, Y, plotLeft, plotW, _critPen);

        int stride = Math.Max(1, values.Count / Math.Max(1, (int)(plotW * 2)));
        var runs = FiniteRuns(values).ToList();

        // fill — one closed figure per finite run so a NaN run leaves a visible gap instead of bridging it.
        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            foreach (var (start, len) in runs)
            {
                int last = start + len - 1;
                ctx.BeginFigure(new Point(X(start), plotTop + plotH), true, true);
                for (int i = start; i <= last; i += stride) ctx.LineTo(new Point(X(i), Y(values[i])), false, false);
                if ((last - start) % stride != 0) ctx.LineTo(new Point(X(last), Y(values[last])), false, false);
                ctx.LineTo(new Point(X(last), plotTop + plotH), false, false);
            }
        }
        fill.Freeze();
        dc.DrawGeometry(FillBrushFor(Stroke), null, fill);

        // line — same per-run breakdown as the fill, above.
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            foreach (var (start, len) in runs)
            {
                int last = start + len - 1;
                ctx.BeginFigure(new Point(X(start), Y(values[start])), false, false);
                for (int i = start + stride; i <= last; i += stride) ctx.LineTo(new Point(X(i), Y(values[i])), true, false);
                if ((last - start) % stride != 0) ctx.LineTo(new Point(X(last), Y(values[last])), true, false);
            }
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.75) { LineJoin = PenLineJoin.Round }, line);

        // last-value dot — the newest *real* sample, which may not be the newest slot if it's currently a gap.
        int lastFinite = -1;
        for (int i = values.Count - 1; i >= 0; i--) { if (!float.IsNaN(values[i])) { lastFinite = i; break; } }
        if (lastFinite >= 0) dc.DrawEllipse(Stroke, null, new Point(X(lastFinite), Y(values[lastFinite])), 3, 3);

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

    private static Brush FillBrushFor(Brush stroke)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var b = new LinearGradientBrush(
            Color.FromArgb(0x40, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        b.Freeze();
        return b;
    }
}
