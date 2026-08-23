using System.Windows;
using System.Windows.Media;
using Stats.App.Helpers;

namespace Stats.App.Controls;

/// <summary>Horizontal level bar (Afterburner style). Fraction 0..1 of a known max.</summary>
public sealed class LevelBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(LevelBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelBar),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(LevelBar),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public LevelBar()
    {
        // Unloaded isn't raised on app shutdown, and Loaded can fire more than once for the same instance —
        // unsubscribe first so the subscription stays idempotent.
        Loaded += (_, _) => { ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged; };
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    // Track is bound via {DynamicResource GaugeTrack} (TileTemplates.xaml) — a genuine DependencyProperty
    // target, so WPF re-resolves it on its own the moment ThemeManager.Apply replaces the resource entry. Fill
    // is bound through SeverityToBrushConverter, which only re-runs when the composition root re-raises the
    // bound tile's Severity property after ThemeManager.Apply (see MetricTileViewModel.RaiseSeverityRefresh);
    // neither path needs code in this control — InvalidateVisual here just keeps it in line with the other three
    // custom-drawn controls.
    private void OnThemeChanged() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double radius = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), radius, radius);
        double fw = w * Math.Clamp(Fraction, 0.0, 1.0);
        if (fw > 0.5) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fw, h), radius, radius);
    }
}
