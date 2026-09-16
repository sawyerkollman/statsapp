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

    private const string NeonSecondaryBrush = "NeonSecondaryBrush";
    private const string NeonOverviewVisibility = "NeonOverviewVisibility";

    /// <summary>"AccentBrush" → "AccentColor", "WindowBg" → "WindowBgColor", etc. — the naming convention used
    /// throughout Theme.xaml for a brush's backing Color resource.</summary>
    private static string ColorKeyFor(string brushKey) =>
        (brushKey.EndsWith("Brush", StringComparison.Ordinal) ? brushKey[..^"Brush".Length] : brushKey) + "Color";

    private const string DarkWarn = "#FFE6A23C";
    // ui-polish T2 contrast fix: was #FFE05A4F (~4.18:1 on TileBg #252528, WCAG AA fail for small text) —
    // lightened to ~4.94:1 on TileBg (and higher still on the darker WindowBg) while staying a clearly "red" crit hue.
    private const string DarkCrit = "#FFE66E64";

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
                // ui-polish T2 contrast fix: was #FFD97B1F (~2.60:1 on ControlBg #EBEBEF, WCAG focus/boundary
                // fail — 3:1 target) — darkened to ~3.60:1 on ControlBg (higher still on the lighter
                // WindowBg/FlyoutBg/TileBg) while staying the same amber/orange hue family.
                ["AccentBrush"] = "#FFB8650F",
                ["WarnBrush"] = "#FF8C5A0C", // darkened from #C4841D (~3.1:1 on #F2F2F4, WCAG AA fail) to ~5.2:1
                // ui-polish T2 contrast fix: was #FFC94438 (~4.30:1 on WindowBg #F2F2F4, WCAG AA fail for
                // small text) — darkened to ~5.31:1 on WindowBg while staying a clearly "red" crit hue.
                ["CritBrush"] = "#FFB23A2F",
                ["GaugeTrack"] = "#FFD8D8DE",
            },
            ["Synthwave"] = NeonPalette(
                "#FF16111F", "#FF241631", "#FF2B1B3D", "#FF342246", "#FF563970",
                "#FFF5EEFF", "#FFC5B8D8", "#FFEA4AAA", "#FF52E5FF"),
            ["Outrun"] = NeonPalette(
                "#FF21121B", "#FF321721", "#FF421C29", "#FF512333", "#FF78364F",
                "#FFFFF0F4", "#FFFFC1CB", "#FFFF71B8", "#FFFFB347"),
            ["Midnight"] = NeonPalette(
                "#FF0D1726", "#FF122238", "#FF172B46", "#FF1D3453", "#FF2D527B",
                "#FFEAF6FF", "#FFB8D2E8", "#FF77C9FF", "#FFA6E3FF"),
            ["CRT Terminal"] = NeonPalette(
                "#FF101A14", "#FF18271E", "#FF203127", "#FF293C30", "#FF43664C",
                "#FFF0FFF3", "#FFB6D5BE", "#FF80E8A0", "#FFB9F59E"),
            ["Arctic Glass"] = NeonPalette(
                "#FF101C25", "#FF182C39", "#FF203847", "#FF284454", "#FF41677D",
                "#FFF0FAFF", "#FFB8D5E5", "#FF83DFFF", "#FFB7EEEB"),
            ["Reactor"] = NeonPalette(
                "#FF211A12", "#FF30271A", "#FF3C3020", "#FF483B28", "#FF6C5939",
                "#FFFFF8EC", "#FFDDCCAC", "#FFFFCC72", "#FFB6ED87"),
            ["Deep Space"] = NeonPalette(
                "#FF121322", "#FF1D2034", "#FF272A43", "#FF303552", "#FF4A527D",
                "#FFF3F3FF", "#FFC4C8E8", "#FFB7A5FF", "#FF81DCED"),
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

    private static IReadOnlyDictionary<string, string> NeonPalette(
        string window, string tile, string flyout, string control, string border,
        string primary, string secondary, string accent, string neonSecondary) => new Dictionary<string, string>
    {
        ["WindowBg"] = window,
        ["TileBg"] = tile,
        ["FlyoutBg"] = flyout,
        ["ControlBg"] = control,
        ["BorderDim"] = border,
        ["TextPrimary"] = primary,
        ["TextSecondary"] = secondary,
        ["AccentBrush"] = accent,
        ["WarnBrush"] = DarkWarn,
        ["CritBrush"] = DarkCrit,
        ["GaugeTrack"] = border,
        [NeonSecondaryBrush] = neonSecondary,
    };

    /// <summary>Replaces the 11 palette brush entries (and their backing Color resources) on
    /// Application.Current.Resources (top level). Safe to call before any window is created (App startup, before
    /// Show()) or any time after (live from a Settings change). Falls back to the default preset if
    /// <paramref name="presetName"/> is unknown, so a future preset removed from this dictionary (or Core's
    /// ThemePresets.Names drifting out of sync with it) never throws.</summary>
    public static void Apply(string? presetName, string? accentHex, string? secondaryHexOverride = null, bool gradient = true)
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
        var neonSecondary = (Color)ColorConverter.ConvertFromString(ThemePresets.IsValidHex(secondaryHexOverride)
            ? secondaryHexOverride! : palette.TryGetValue(NeonSecondaryBrush, out var secondaryHex) ? secondaryHex : palette["BorderDim"]);
        resources[ColorKeyFor(NeonSecondaryBrush)] = neonSecondary;
        var neonBrush = new SolidColorBrush(neonSecondary);
        neonBrush.Freeze();
        resources[NeonSecondaryBrush] = neonBrush;
        Brush decoration = gradient
            ? new LinearGradientBrush((Color)resources["AccentColor"], neonSecondary, 90)
            : new SolidColorBrush((Color)resources["AccentColor"]);
        decoration.Freeze();
        resources["NeonDecorationBrush"] = decoration;
        resources[NeonOverviewVisibility] = preset is "Synthwave" or "Outrun" or "Midnight" or "CRT Terminal" or "Arctic Glass" or "Reactor" or "Deep Space"
            ? Visibility.Visible
            : Visibility.Collapsed;
        Changed?.Invoke();
    }

    /// <summary>Test-only accessor exposing a preset's resolved hex palette (the same table <see cref="Apply"/>
    /// reads from) for contrast verification — see tests/Stats.UiPreview.Tests/PaletteContrastTests.cs. Not
    /// used by any production code path (production always goes through <see cref="Apply"/>). Falls back to
    /// <see cref="ThemePresets.Default"/> for an unknown name, matching Apply's own fallback.</summary>
    public static IReadOnlyDictionary<string, string> PaletteFor(string presetName)
    {
        var preset = ThemePresets.SanitizePresetName(presetName);
        return Palettes.TryGetValue(preset, out var palette) ? palette : Palettes[ThemePresets.Default];
    }

    /// <summary>Current resolved colour of a palette brush (e.g. "CritBrush") — for code that derives a tint from
    /// a theme colour instead of binding to the brush directly (e.g. FanCurveEditor's floor shading). Reads the
    /// Color resource directly — never a SolidColorBrush's .Color — since the brush instances found via
    /// Application.Current.Resources may not be the physical instance a given visual tree actually renders with
    /// (see the double-merge note on Theme.xaml), while the Color resource is always the single source of truth.</summary>
    public static Color Get(string brushKey) =>
        Application.Current?.Resources[ColorKeyFor(brushKey)] is Color c ? c : Colors.Gray;
}
