using System.Windows;
using System.Windows.Media;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>270° arc gauge (Ryzen Master style). Fraction 0..1 fills clockwise from lower-left to lower-right.</summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public ArcGauge()
    {
        Loaded += (_, _) => ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    // Stroke/Track are already routed through shared theme brushes (bound via StaticResource), so WPF repaints
    // them on its own; this just keeps the control in line with the other three custom-drawn controls.
    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        if (r <= 0) return;

        var trackPen = new Pen(Track, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, trackPen, Arc(center, r, StartAngle, SweepAngle));

        double f = Math.Clamp(Fraction, 0.0, 1.0);
        if (f > 0.001)
        {
            var valuePen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, valuePen, Arc(center, r, StartAngle, SweepAngle * f));
        }
    }

    private static StreamGeometry Arc(Point center, double r, double startDeg, double sweepDeg)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(OnCircle(center, r, startDeg), false, false);
            ctx.ArcTo(OnCircle(center, r, startDeg + sweepDeg), new Size(r, r), 0,
                      isLargeArc: sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Point OnCircle(Point c, double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
