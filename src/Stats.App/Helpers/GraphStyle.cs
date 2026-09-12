using System.Windows;
using Stats.Core.Settings;

namespace Stats.App.Helpers;

/// <summary>Live-applies the two graph settings (<see cref="AppSettings.SmoothLines"/>/
/// <see cref="AppSettings.GraphEffects"/>) the same way <see cref="ThemeManager"/> live-applies the theme: a
/// static flag pair plus a <see cref="Changed"/> event the custom-drawn controls subscribe to in Loaded/unsubscribe
/// in Unloaded. Set from App.xaml.cs at startup (next to ThemeManager.Apply) and on
/// <c>SettingsChange.Graphs</c>.</summary>
public static class GraphStyle
{
    /// <summary>Raised after <see cref="Apply"/> updates the flags. Sparkline/HistoryChart/LevelBar/ArcGauge
    /// subscribe and call InvalidateVisual, exactly like <see cref="ThemeManager.Changed"/>.</summary>
    public static event Action? Changed;

    /// <summary>Draw a monotone-cubic curve instead of a jagged polyline.</summary>
    public static bool SmoothLines { get; private set; } = true;

    /// <summary>Glow, gradients, pulse, and eased bar/gauge fills — one switch for every static+animated effect.</summary>
    public static bool Effects { get; private set; } = true;

    /// <summary>The *animated* subset of <see cref="Effects"/> (pulse, easing) — additionally gated by the OS
    /// "animation effects" preference so users who've turned that off system-wide don't get motion here either.
    /// The static glow/gradients stay on with <see cref="Effects"/> alone.</summary>
    public static bool Motion => Effects && SystemParameters.ClientAreaAnimation;

    public static void Apply(AppSettings s)
    {
        SmoothLines = s.SmoothLines;
        Effects = s.GraphEffects;
        Changed?.Invoke();
    }
}
