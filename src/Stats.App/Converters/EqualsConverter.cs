using System.Globalization;
using System.Windows.Data;

namespace Stats.App.Converters;

/// <summary>IsChecked = (value.ToString() == parameter); ConvertBack returns parameter (parsed to the target type) when checked.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) return Binding.DoNothing;
        var text = parameter?.ToString() ?? "";
        if (targetType == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (targetType.IsEnum) return Enum.Parse(targetType, text);
        return text;
    }
}
