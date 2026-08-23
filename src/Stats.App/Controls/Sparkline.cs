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

    private static readonly Pen GuidePen = CreateGuidePen();
    private int _hoverIndex = -1;

    public Sparkline()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetShowDuration(this, 30000);
        Loaded += (_, _) => ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    // Stroke/Fill are already routed through shared theme brushes via SeverityToBrushConverter, so WPF's own
    // Freezable-change notification repaints them automatically; this keeps the guide/hover overlays (drawn
    // fresh every OnRender) in step with a theme switch too, and matches the pattern used by the other three
    // custom-drawn controls.
    private void OnThemeChanged() => InvalidateVisual();

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
            dc.DrawLine(GuidePen, new Point(0, Y(max)), new Point(w, Y(max)));
            dc.DrawLine(GuidePen, new Point(0, Y(min)), new Point(w, Y(min)));
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
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1), new Point(hx, 0), new Point(hx, h));
            dc.DrawEllipse(Brushes.White, null, new Point(hx, Y(values[_hoverIndex])), 3, 3);
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

    private static Pen CreateGuidePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 0.75) { DashStyle = DashStyles.Dash };
        p.Freeze();
        return p;
    }
}
