using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>
/// Polyline sparkline with gradient fill, last-value dot, faint min/max guides and a hover tooltip.
/// Values is IReadOnlyList&lt;float&gt; (array-typed DPs can't be bound inside DataTemplates — MC4102).
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Sparkline), new PropertyMetadata(""));

    public static readonly DependencyProperty ShowGuidesProperty = DependencyProperty.Register(
        nameof(ShowGuides), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

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
        Loaded += (_, _) => { ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged; OnThemeChanged(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var values = Values;
        if (values is null || values.Count < 2 || ActualWidth <= 0) return;
        int idx = (int)Math.Round(e.GetPosition(this).X / ActualWidth * (values.Count - 1));
        idx = Math.Clamp(idx, 0, values.Count - 1);
        if (idx == _hoverIndex) return;
        _hoverIndex = idx;
        ToolTip = string.Create(CultureInfo.InvariantCulture, $"{values[idx]:F1} {Unit}").TrimEnd();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test surface for hover

        var values = Values;
        if (values is null || values.Count < 2) return;

        float min = values[0], max = values[0];
        for (int i = 1; i < values.Count; i++) { if (values[i] < min) min = values[i]; if (values[i] > max) max = values[i]; }
        float range = max - min;
        if (range < 1e-6f) range = 1f;

        double X(int i) => w * i / (values.Count - 1);
        double Y(float v) => h - 2 - (v - min) / range * (h - 4);

        int stride = Math.Max(1, values.Count / Math.Max(1, (int)(w * 2)));

        // fill
        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            for (int i = 0; i < values.Count; i += stride) ctx.LineTo(new Point(X(i), Y(values[i])), false, false);
            if ((values.Count - 1) % stride != 0) ctx.LineTo(new Point(X(values.Count - 1), Y(values[values.Count - 1])), false, false);
            ctx.LineTo(new Point(w, h), false, false);
        }
        fill.Freeze();
        dc.DrawGeometry(FillBrushFor(Stroke), null, fill);

        // guides
        if (ShowGuides && max - min > 1e-6f)
        {
            dc.DrawLine(_guidePen, new Point(0, Y(max)), new Point(w, Y(max)));
            dc.DrawLine(_guidePen, new Point(0, Y(min)), new Point(w, Y(min)));
        }

        // line
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(values[0])), false, false);
            for (int i = stride; i < values.Count; i += stride) ctx.LineTo(new Point(X(i), Y(values[i])), true, false);
            if ((values.Count - 1) % stride != 0) ctx.LineTo(new Point(X(values.Count - 1), Y(values[values.Count - 1])), true, false);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);

        // last-value dot
        int last = values.Count - 1;
        dc.DrawEllipse(Stroke, null, new Point(X(last), Y(values[last])), 2.5, 2.5);

        // hover
        if (_hoverIndex >= 0 && _hoverIndex < values.Count)
        {
            double hx = X(_hoverIndex);
            dc.DrawLine(_hoverLinePen, new Point(hx, 0), new Point(hx, h));
            dc.DrawEllipse(_hoverDotBrush, null, new Point(hx, Y(values[_hoverIndex])), 3, 3);
        }
    }

    private static Brush FillBrushFor(Brush stroke)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var b = new LinearGradientBrush(
            Color.FromArgb(0x55, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        b.Freeze();
        return b;
    }
}
