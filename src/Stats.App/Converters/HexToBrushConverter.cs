using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Stats.App.Converters;

/// <summary>"#RRGGBB" → SolidColorBrush, for displaying the accent swatch buttons in Settings. Invalid/empty
/// input falls back to transparent rather than throwing — the swatch list itself is always valid, but this keeps
/// the converter safe if ever bound to free-form text.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length > 0)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch (FormatException) { /* fall through */ }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
