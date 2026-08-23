using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Stats.App.Helpers;

namespace Stats.App.Converters;

/// <summary>0..1 load → tile background: cold (theme's ControlBg) → accent (0.7) → crit (1.0). Palette-derived, so
/// it tracks the active theme; Cache is dropped (not just refreshed) on ThemeManager.Changed since the whole
/// gradient shifts. Brushes frozen and cached per 2% step. This converter is a single app-lifetime resource
/// (declared once in Theme.xaml), so the ThemeManager.Changed subscription in the constructor never needs to be
/// unhooked.</summary>
public sealed class HeatToBrushConverter : IValueConverter
{
    private SolidColorBrush?[] _cache = new SolidColorBrush?[51];

    public HeatToBrushConverter()
    {
        ThemeManager.Changed += () => _cache = new SolidColorBrush?[51];
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double t = value is float f ? Math.Clamp(f, 0f, 1f) : 0.0;
        int step = (int)Math.Round(t * 50);
        var cache = _cache;
        var cached = cache[step];
        if (cached is not null) return cached;
        Color cold = ThemeManager.Get("ControlBg");
        Color warm = ThemeManager.Get("AccentBrush");
        Color hot = ThemeManager.Get("CritBrush");
        double tt = step / 50.0;
        Color c = tt <= 0.7 ? Lerp(cold, warm, tt / 0.7) : Lerp(warm, hot, (tt - 0.7) / 0.3);
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return cache[step] = brush;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Paired with <see cref="HeatToBrushConverter"/>: same 0..1 load input, but returns the cell's text
/// foreground. TextPrimary (theme colour) contrasts poorly against the hot end of the heat gradient — it flips
/// to near-black under the Light preset, which lands on the same red/orange hot-end tile background — so cells
/// at or above the hot threshold get a fixed near-white foreground instead, readable against CritBrush in every
/// preset. Below the threshold the tile background is close to ControlBg/AccentBrush, where TextPrimary already
/// reads fine.</summary>
public sealed class HeatToForegroundConverter : IValueConverter
{
    private const double HotForegroundThreshold = 0.82;
    private static readonly SolidColorBrush HotForeground = MakeFrozen(Color.FromRgb(0xF5, 0xF5, 0xF5));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double t = value is float f ? Math.Clamp(f, 0f, 1f) : 0.0;
        return t >= HotForegroundThreshold ? HotForeground : Application.Current.Resources["TextPrimary"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static SolidColorBrush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
