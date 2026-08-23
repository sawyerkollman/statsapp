using System.Windows;
using System.Windows.Media;
using Stats.Core.Settings;

namespace Stats.App.Helpers;

/// <summary>Live-applies a theme preset (+ optional custom accent) by REPLACING each of the 11 palette brush
/// entries on the TOP-LEVEL Application.Current.Resources with a brand-new (frozen) SolidColorBrush — this is
/// the only mechanism runtime-verified to repaint every consumer with no restart. An earlier version of this
/// class mutated brush.Color in place / via a DynamicResource-backed Color indirection; harness testing proved
/// that approach never re-resolves a dictionary-owned SolidColorBrush once realized (StaticResource consumers
/// never update; a bare {DynamicResource AccentBrush} with only the Color changing still doesn't repaint). Entry
/// replacement works only for consumers that reference the brush via {DynamicResource X} — every such XAML
/// reference was swept from StaticResource to DynamicResource alongside this change; StaticResource consumers
/// can never update under entry replacement (including Style setters — WPF resolves those per-instance via
/// DynamicResource too, so they DO update, but a hypothetical StaticResource setter would not).
/// The backing "*Color" resources are still written on every Apply — ThemeManager.Get, HeatToBrushConverter,
/// HeatToForegroundConverter, Sparkline and FanCurveEditor read those directly and that path is unaffected by
/// this change. See docs/superpowers/specs/2026-08-23-theme-colors-design.md.</summary>
public static class ThemeManager
{
    /// <summary>Raised after Apply() finishes replacing the brush entries and Color resources. HeatToBrushConverter
    /// and the custom-drawn controls (Sparkline, ArcGauge, LevelBar, FanCurveEditor) subscribe to rebuild any
    /// cached/derived brush and re-render. App also uses this moment to nudge every live Severity-bound view-model
    /// property (see the VMs' RaiseSeverityRefresh) since SeverityToBrushConverter's Bindings won't re-run on
    /// their own — the Severity value they're bound to hasn't changed, only the brush instance it resolves to.</summary>
    public static event Action? Changed;

    /// <summary>The 11 brush keys defined in Theme.xaml — must match exactly (Warn/Crit stay semantic colours
    /// but are still theme-tinted per preset per the design doc; they are palette entries, not user-editable).
    /// <see cref="Get"/> also accepts these; it maps each to its backing "*Color" resource key.</summary>
    private static readonly string[] BrushKeys =
    {
        "WindowBg", "TileBg", "FlyoutBg", "ControlBg", "BorderDim",
        "TextPrimary", "TextSecondary", "AccentBrush", "WarnBrush", "CritBrush", "GaugeTrack",
    };

    /// <summary>"AccentBrush" → "AccentColor", "WindowBg" → "WindowBgColor", etc. — the naming convention used
    /// throughout Theme.xaml for a brush's backing Color resource.</summary>
    private static string ColorKeyFor(string brushKey) =>
        (brushKey.EndsWith("Brush", StringComparison.Ordinal) ? brushKey[..^"Brush".Length] : brushKey) + "Color";

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
                ["WarnBrush"] = "#FF8C5A0C", // darkened from #C4841D (~3.1:1 on #F2F2F4, WCAG AA fail) to ~5.2:1
                ["CritBrush"] = "#FFC94438", // ~4.6:1 on #F2F2F4 already — no change needed
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

    /// <summary>Replaces the 11 palette brush entries (and their backing Color resources) on
    /// Application.Current.Resources (top level). Safe to call before any window is created (App startup, before
    /// Show()) or any time after (live from a Settings change). Falls back to the default preset if
    /// <paramref name="presetName"/> is unknown, so a future preset removed from this dictionary (or Core's
    /// ThemePresets.Names drifting out of sync with it) never throws.</summary>
    public static void Apply(string? presetName, string? accentHex)
    {
        if (Application.Current is null) return;
        var preset = ThemePresets.SanitizePresetName(presetName);
        if (!Palettes.TryGetValue(preset, out var palette)) palette = Palettes[ThemePresets.Default];
        var accentOverride = ThemePresets.IsValidHex(accentHex) ? accentHex : null;

        var resources = Application.Current.Resources;
        foreach (var key in BrushKeys)
        {
            var hex = key == "AccentBrush" && accentOverride is not null ? accentOverride : palette[key];
            var color = (Color)ColorConverter.ConvertFromString(hex);

            // Backing Color resource — still consumed directly by Get()/HeatToBrushConverter/
            // HeatToForegroundConverter/Sparkline/FanCurveEditor. Set on the top-level dictionary (never inside
            // a MergedDictionaries entry) so it's visible from both physical merges of Theme.xaml.
            resources[ColorKeyFor(key)] = color;

            // Brush entry REPLACEMENT — runtime-verified to be the only mechanism that repaints a
            // {DynamicResource X}-bound consumer whose brush was already realized. Frozen: a replaced entry is
            // never mutated again (the next Apply() replaces it wholesale), so freezing is safe and cheap.
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
        Changed?.Invoke();
    }

    /// <summary>Current resolved colour of a palette brush (e.g. "CritBrush") — for code that derives a tint from
    /// a theme colour instead of binding to the brush directly (e.g. FanCurveEditor's floor shading). Reads the
    /// Color resource directly — never a SolidColorBrush's .Color — since the brush instances found via
    /// Application.Current.Resources may not be the physical instance a given visual tree actually renders with
    /// (see the double-merge note on Theme.xaml), while the Color resource is always the single source of truth.</summary>
    public static Color Get(string brushKey) =>
        Application.Current?.Resources[ColorKeyFor(brushKey)] is Color c ? c : Colors.Gray;
}
