using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>270° arc gauge (Ryzen Master style). Fraction 0..1 fills clockwise from lower-left to lower-right.
/// When <see cref="GraphStyle.Effects"/> is on, the arc's end gets a soft glow cap; when <see cref="GraphStyle.Motion"/>
/// is also on, the drawn sweep eases to a new <see cref="Fraction"/> instead of jumping.</summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFractionChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The actually-drawn fraction — animated toward <see cref="Fraction"/> when motion is allowed,
    /// otherwise set directly. Private: this is a rendering implementation detail, not a bindable property.</summary>
    private static readonly DependencyProperty AnimatedFractionProperty = DependencyProperty.Register(
        nameof(AnimatedFraction), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double StartAngle = 135.0;
    private const double SweepAngle = 270.0;
    private const double GlowCapDegrees = 12.0;

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    private double AnimatedFraction { get => (double)GetValue(AnimatedFractionProperty); set => SetValue(AnimatedFractionProperty, value); }

    public ArcGauge()
    {
        // Unloaded isn't raised on app shutdown, and Loaded can fire more than once for the same instance —
        // unsubscribe first so the subscription stays idempotent.
        Loaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged;
            GraphStyle.Changed -= OnGraphStyleChanged; GraphStyle.Changed += OnGraphStyleChanged;
        };
        Unloaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            GraphStyle.Changed -= OnGraphStyleChanged;
        };
    }

    // Track is bound via {DynamicResource GaugeTrack} (TileTemplates.xaml) — a genuine DependencyProperty
    // target, so WPF re-resolves it on its own the moment ThemeManager.Apply replaces the resource entry. Stroke
    // is bound through SeverityToBrushConverter, which only re-runs when the composition root re-raises the
    // bound tile's Severity property after ThemeManager.Apply (see MetricTileViewModel.RaiseSeverityRefresh);
    // neither path needs code in this control — InvalidateVisual here just keeps it in line with the other three
    // custom-drawn controls (guide lines etc.), none of which this control currently draws.
    private void OnThemeChanged() => InvalidateVisual();
    private void OnGraphStyleChanged() => InvalidateVisual();

    /// <summary>Animates the drawn fraction toward a new value (250 ms quadratic ease-out) when motion is
    /// allowed; otherwise (or before the control has ever rendered a value — the very first assignment while
    /// AnimatedFraction still sits at its default 0) sets it directly so the initial paint isn't animated in
    /// from empty.</summary>
    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (ArcGauge)d;
        double newValue = (double)e.NewValue;
        bool firstAssignment = !ctrl.IsLoaded && ctrl.AnimatedFraction == 0.0;
        if (!GraphStyle.Motion || firstAssignment)
        {
            ctrl.BeginAnimation(AnimatedFractionProperty, null);
            ctrl.AnimatedFraction = newValue;
            return;
        }
        var anim = new DoubleAnimation(ctrl.AnimatedFraction, newValue, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        ctrl.BeginAnimation(AnimatedFractionProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        if (r <= 0) return;

        var trackPen = new Pen(Track, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, trackPen, Arc(center, r, StartAngle, SweepAngle));

        double f = Math.Clamp(AnimatedFraction, 0.0, 1.0);
        if (f > 0.001)
        {
            double valueSweep = SweepAngle * f;

            if (GraphStyle.Effects)
            {
                // glow cap — a short arc segment at the end of the value sweep, wider and low-alpha, under the
                // main value arc (same double-draw trick as the line glow).
                double capSweep = Math.Min(valueSweep, GlowCapDegrees);
                var glowPen = new Pen(GlowBrushFor(Stroke), Thickness + 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, Arc(center, r, StartAngle + valueSweep - capSweep, capSweep));
            }

            var valuePen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, valuePen, Arc(center, r, StartAngle, valueSweep));
        }
    }

    private static Brush GlowBrushFor(Brush stroke)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
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
