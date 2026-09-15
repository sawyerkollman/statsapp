using System.Globalization;
using System.Windows.Data;
using Stats.Core.Settings;

namespace Stats.App.Converters;

/// <summary>TileSize → width ("W") or height ("H") in px. Delegates to <see cref="TileDimensions"/> (moved there
/// for dashboard layout modes, so the seed pack/canvas-extent math in Stats.Core uses the exact same numbers this
/// converter draws with).</summary>
public sealed class TileSizeToLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool width = parameter as string == "W";
        var size = value is TileSize s ? s : TileSize.M;
        var (w, h) = TileDimensions.Of(size);
        return width ? w : h;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
