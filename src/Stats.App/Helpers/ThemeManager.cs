using System.Windows;
using System.Windows.Media;
using Stats.Core.Settings;

namespace Stats.App.Helpers;

/// <summary>Live-applies a theme preset (+ optional custom accent) by mutating the existing SolidColorBrush
/// instances registered in Theme.xaml — never replacing dictionary entries, so every StaticResource consumer
/// (styles, DataTemplates, and any custom control bound to one of these brushes) picks up the change instantly,
/// no restart. See docs/superpowers/specs/2026-08-23-theme-colors-design.md.</summary>
public static class ThemeManager
{
    /// <summary>Raised after Apply() finishes mutating brushes. HeatToBrushConverter and the custom-drawn controls
    /// (Sparkline, ArcGauge, LevelBar, FanCurveEditor) subscribe to rebuild any cached/derived brush and
    /// re-render.</summary>
    public static event Action? Changed;

    /// <summary>The 11 brush keys defined in Theme.xaml — must match exactly (Warn/Crit stay semantic colours
    /// but are still theme-tinted per preset per the design doc; they are palette entries, not user-editable).</summary>
    private static readonly string[] BrushKeys =
    {
        "WindowBg", "TileBg", "FlyoutBg", "ControlBg", "BorderDim",
        "TextPrimary", "TextSecondary", "AccentBrush", "WarnBrush", "CritBrush", "GaugeTrack",
    };

    private const string DarkWarn = "#FFE6A23C";
    private const string DarkCrit = "#FFE05A4F";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Palettes =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Dark Amber"] = DarkPalette("#FFE68A2E"),
            ["Dark Blue"] = DarkPalette("#FF4A9EE0"),
            ["Dark Green"] = DarkPalette("#FF4FC06A"),
            ["Dark Purple"] = DarkPalette("#FFA47AE0"),
            ["Light"] = new Dictionary<string, string>
            {
                ["WindowBg"] = "#FFF2F2F4",
                ["TileBg"] = "#FFFFFFFF",
                ["FlyoutBg"] = "#FFFAFAFC",
                ["ControlBg"] = "#FFEBEBEF",
                ["BorderDim"] = "#FFD0D0D6",
                ["TextPrimary"] = "#FF1E1E22",
                ["TextSecondary"] = "#FF6A6A72",
                ["AccentBrush"] = "#FFD97B1F",
                ["WarnBrush"] = "#FFC4841D",
                ["CritBrush"] = "#FFC94438",
                ["GaugeTrack"] = "#FFD8D8DE",
            },
        };

    private static IReadOnlyDictionary<string, string> DarkPalette(string accent) => new Dictionary<string, string>
    {
        ["WindowBg"] = "#FF1B1B1C",
        ["TileBg"] = "#FF252528",
        ["FlyoutBg"] = "#FF2B2B2F",
        ["ControlBg"] = "#FF303035",
        ["BorderDim"] = "#FF3A3A40",
        ["TextPrimary"] = "#FFF0F0F0",
        ["TextSecondary"] = "#FF9A9A9E",
        ["AccentBrush"] = accent,
        ["WarnBrush"] = DarkWarn,
        ["CritBrush"] = DarkCrit,
        ["GaugeTrack"] = "#FF3A3A40",
    };

    /// <summary>Mutates the 11 palette brushes in Application.Current.Resources in place. Safe to call before any
    /// window is created (App startup, before Show()) or any time after (live from a Settings change).</summary>
    public static void Apply(string? presetName, string? accentHex)
    {
        if (Application.Current is null) return;
        var preset = ThemePresets.SanitizePresetName(presetName);
        var palette = Palettes[preset];
        var accentOverride = ThemePresets.IsValidHex(accentHex) ? accentHex : null;

        var resources = Application.Current.Resources;
        foreach (var key in BrushKeys)
        {
            if (resources[key] is not SolidColorBrush brush) continue;
            var hex = key == "AccentBrush" && accentOverride is not null ? accentOverride : palette[key];
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (brush.Color != color) brush.Color = color; // mutate, never replace — StaticResource consumers hold this instance
        }
        Changed?.Invoke();
    }

    /// <summary>Current resolved colour of a palette brush (e.g. "CritBrush") — for code that derives a tint from
    /// a theme colour instead of binding to the brush directly (e.g. FanCurveEditor's floor shading).</summary>
    public static Color Get(string brushKey) =>
        Application.Current?.Resources[brushKey] is SolidColorBrush b ? b.Color : Colors.Gray;
}
