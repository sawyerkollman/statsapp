using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Stats.App.Converters;

/// <summary>Dashboard layout modes (Free/Snap canvas): Visible only when every bound value is boolean true —
/// used to combine two independent flags (e.g. "not Auto layout" and "not empty") in a single MultiBinding without
/// a dedicated view-model property for the conjunction. Any non-true value (including non-bool) counts as false.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
