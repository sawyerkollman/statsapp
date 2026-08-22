using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Stats.Core.Metrics;

namespace Stats.App.Converters;

/// <summary>Severity → brush. Normal maps to TextPrimary, or AccentBrush when ConverterParameter is "accent".</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var res = Application.Current.Resources;
        return value switch
        {
            Severity.Crit => res["CritBrush"],
            Severity.Warn => res["WarnBrush"],
            _ => parameter is string s && s == "accent" ? res["AccentBrush"] : res["TextPrimary"],
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
