using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Stats.App.Converters;

/// <summary>0..1 load → tile background: dark → accent (0.7) → red (1.0). Brushes frozen and cached per 2% step.</summary>
public sealed class HeatToBrushConverter : IValueConverter
{
    private static readonly Color Cold = Color.FromRgb(0x2E, 0x2E, 0x33);
    private static readonly Color Warm = Color.FromRgb(0xE6, 0x8A, 0x2E);
    private static readonly Color Hot = Color.FromRgb(0xE0, 0x5A, 0x4F);
    private static readonly SolidColorBrush[] Cache = new SolidColorBrush[51];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double t = value is float f ? Math.Clamp(f, 0f, 1f) : 0.0;
        int step = (int)Math.Round(t * 50);
        var cached = Cache[step];
        if (cached is not null) return cached;
        double tt = step / 50.0;
        Color c = tt <= 0.7 ? Lerp(Cold, Warm, tt / 0.7) : Lerp(Warm, Hot, (tt - 0.7) / 0.3);
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return Cache[step] = brush;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
