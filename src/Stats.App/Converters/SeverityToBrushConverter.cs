using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Stats.Core.Metrics;

namespace Stats.App.Converters;

/// <summary>Severity → brush. Normal maps to TextPrimary, or AccentBrush when ConverterParameter is "accent", or
/// OverlayTextPrimary when ConverterParameter is "Overlay" (the overlay panel is a fixed dark translucent
/// surface in every preset, so its Normal-severity text needs a fixed-light foreground rather than TextPrimary,
/// which flips to near-black under the Light preset). Crit/Warn also branch on "Overlay": the pinned
/// OverlayWarn/OverlayCrit brushes, not the theme's WarnBrush/CritBrush — Light's darkened WarnBrush
/// (#8C5A0C, tuned for contrast on a light TileBg) reads at ~2.9:1 on the overlay's dark panel.
///
/// Always re-fetches the resource by key on every Convert call — never caches a resolved brush — because
/// ThemeManager.Apply REPLACES each palette brush entry rather than mutating it in place (runtime-verified: a
/// dictionary-owned SolidColorBrush's Color latches at first realization and never re-resolves). That also means
/// a Foreground already bound through this converter stays on the OLD brush instance after a theme switch until
/// something re-runs the Binding — the Severity value it's bound to hasn't changed, only which brush that
/// severity now maps to. App nudges this by calling each live view-model's RaiseSeverityRefresh() (which just
/// re-raises PropertyChanged(nameof(Severity))) right after ThemeManager.Apply — see
/// DashboardViewModel/OverlayViewModel/PeaksViewModel/CoreMatrixViewModel.RaiseSeverityRefresh and
/// App.OnSettingsChanged's SettingsChange.Theme case.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var res = Application.Current.Resources;
        string? param = parameter as string;
        bool overlay = param == "Overlay";
        return value switch
        {
            Severity.Crit => res[overlay ? "OverlayCrit" : "CritBrush"],
            Severity.Warn => res[overlay ? "OverlayWarn" : "WarnBrush"],
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
