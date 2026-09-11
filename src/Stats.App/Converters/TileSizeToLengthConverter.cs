using System.Globalization;
using System.Windows.Data;
using Stats.Core.Settings;

namespace Stats.App.Converters;

/// <summary>TileSize → width ("W") or height ("H") in px. S 160×80, M 224×144, L 460×192 (v1.8 UI-polish §2:
/// L = two M columns plus their 12-unit gap — 224*2+12 = 460).</summary>
public sealed class TileSizeToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool width = parameter as string == "W";
        return value switch
        {
            TileSize.S => width ? 160.0 : 80.0,
            TileSize.L => width ? 460.0 : 192.0,
            _ => width ? 224.0 : 144.0,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
