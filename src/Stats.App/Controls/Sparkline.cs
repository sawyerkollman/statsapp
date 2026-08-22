using System.Windows;
using System.Windows.Media;

namespace Stats.App.Controls;

/// <summary>Minimal polyline sparkline. Auto-scales vertically to the window of values it is given.</summary>
public sealed class Sparkline : FrameworkElement
{
    // Note: registered as IReadOnlyList<float> rather than float[] — a float[]-typed
    // dependency property set via {Binding} inside a compiled DataTemplate trips the WPF
    // markup compiler ("MC4102: Tags of type 'PropertyArrayStart' are not supported in
    // template sections"), since BAML's array-property record isn't valid there. The
    // interface type sidesteps that while still accepting the float[] HistoryValues source.
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<float>? Values
    {
        get => (IReadOnlyList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        double w = ActualWidth, h = ActualHeight;
        if (values is null || values.Count < 2 || w <= 0 || h <= 0) return;

        float min = values.Min(), max = values.Max();
        float range = max - min;
        if (range < 1e-6f) range = 1f;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                double x = w * i / (values.Count - 1);
                double y = h - 2 - (values[i] - min) / range * (h - 4);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.5), geometry);
    }
}
