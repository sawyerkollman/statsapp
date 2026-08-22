using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Stats.Core.Settings;

namespace Stats.App.Converters;

public sealed class OverlayOrientationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is OverlayOrientation.Vertical ? Orientation.Vertical : Orientation.Horizontal;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
