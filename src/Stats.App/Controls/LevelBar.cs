using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>Horizontal level bar (Afterburner style). Fraction 0..1 of a known max. When
/// <see cref="GraphStyle.Effects"/> is on, the fill is a left-to-right gradient with a thin top highlight; when
/// <see cref="GraphStyle.Motion"/> is also on, the drawn fraction eases to a new <see cref="Fraction"/> instead
/// of jumping.</summary>
public sealed class LevelBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(LevelBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFractionChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelBar),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(LevelBar),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The actually-drawn fraction — animated toward <see cref="Fraction"/> when motion is allowed,
    /// otherwise set directly. Private: this is a rendering implementation detail, not a bindable property.</summary>
    private static readonly DependencyProperty AnimatedFractionProperty = DependencyProperty.Register(
        nameof(AnimatedFraction), typeof(double), typeof(LevelBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    private double AnimatedFraction { get => (double)GetValue(AnimatedFractionProperty); set => SetValue(AnimatedFractionProperty, value); }

    public LevelBar()
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
    // target, so WPF re-resolves it on its own the moment ThemeManager.Apply replaces the resource entry. Fill
    // is bound through SeverityToBrushConverter, which only re-runs when the composition root re-raises the
    // bound tile's Severity property after ThemeManager.Apply (see MetricTileViewModel.RaiseSeverityRefresh);
    // neither path needs code in this control — InvalidateVisual here just keeps it in line with the other three
    // custom-drawn controls.
    private void OnThemeChanged() => InvalidateVisual();
    private void OnGraphStyleChanged() => InvalidateVisual();

    /// <summary>Animates the drawn fraction toward a new value (250 ms quadratic ease-out) when motion is
    /// allowed; otherwise (or before the control has ever rendered a value — the very first assignment while
    /// AnimatedFraction still sits at its default 0) sets it directly so the initial paint isn't animated in
    /// from empty.</summary>
    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (LevelBar)d;
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
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double radius = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), radius, radius);
        double fw = w * Math.Clamp(AnimatedFraction, 0.0, 1.0);
        if (fw <= 0.5) return;

        var fillRect = new Rect(0, 0, fw, h);
        dc.DrawRoundedRectangle(GraphStyle.Effects ? GradientFillFor(Fill) : Fill, null, fillRect, radius, radius);

        if (GraphStyle.Effects && fw > 2)
        {
            // 1px top highlight, inset so it doesn't bleed past the rounded ends.
            var highlight = new Pen(HighlightBrush, 1);
            double inset = Math.Min(radius, fw / 2);
            dc.DrawLine(highlight, new Point(inset, 0.75), new Point(Math.Max(inset, fw - inset), 0.75));
        }
    }

    private static Brush GradientFillFor(Brush fill)
    {
        var c = fill is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var b = new LinearGradientBrush(c, Color.FromArgb((byte)(c.A * 0.7), c.R, c.G, c.B), 0) { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        b.Freeze();
        return b;
    }

    private static Brush HighlightBrush { get; } = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
