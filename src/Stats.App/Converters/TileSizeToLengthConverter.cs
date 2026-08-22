using System.Globalization;
using System.Windows.Data;
using Stats.Core.Settings;

namespace Stats.App.Converters;

/// <summary>TileSize → width ("W") or height ("H") in px. S 150×70, M 215×120, L 440×160.</summary>
public sealed class TileSizeToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool width = parameter as string == "W";
        return value switch
        {
            TileSize.S => width ? 150.0 : 70.0,
            TileSize.L => width ? 440.0 : 160.0,
            _ => width ? 215.0 : 120.0,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
