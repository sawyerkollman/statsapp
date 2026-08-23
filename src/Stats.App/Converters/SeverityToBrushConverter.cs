using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Stats.Core.Metrics;

namespace Stats.App.Converters;

/// <summary>Severity → brush. Normal maps to TextPrimary, or AccentBrush when ConverterParameter is "accent", or
/// OverlayTextPrimary when ConverterParameter is "Overlay" (the overlay panel is a fixed dark translucent
/// surface in every preset, so its Normal-severity text needs a fixed-light foreground rather than TextPrimary,
/// which flips to near-black under the Light preset). Crit/Warn are unaffected by the parameter — they're already
/// readable on the overlay's dark panel in every preset. Every branch returns the live shared brush instance from
/// the resource system, so a theme switch (which only changes the brushes' backing Color resources, never
/// replaces the brush instances) repaints already-bound Foregrounds automatically — no re-evaluation of this
/// converter or of the Severity binding is needed.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var res = Application.Current.Resources;
        string? param = parameter as string;
        return value switch
        {
            Severity.Crit => res["CritBrush"],
            Severity.Warn => res["WarnBrush"],
            _ => param switch
            {
                "accent" => res["AccentBrush"],
                "Overlay" => res["OverlayTextPrimary"],
                _ => res["TextPrimary"],
            },
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
