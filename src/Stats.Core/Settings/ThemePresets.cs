using System.Text.RegularExpressions;

namespace Stats.Core.Settings;

/// <summary>Pure, WPF-free theme preset data: preset names, accent swatch hex list, and hex validation/sanitation
/// used by <see cref="SettingsService"/> at load. The actual palette colour values (System.Windows.Media.Color)
/// live in Stats.App's ThemeManager — Stats.Core stays WPF-free (see CLAUDE.md).</summary>
public static class ThemePresets
{
    public const string Default = "Dark Amber";

    public static readonly IReadOnlyList<string> Names = new[]
    {
        "Dark Amber", "Dark Blue", "Dark Green", "Dark Purple", "Light",
    };

    /// <summary>Predefined accent swatches offered in Settings, independent of the active preset.</summary>
    public static readonly IReadOnlyList<string> AccentSwatches = new[]
    {
        "#E68A2E", "#4A9EE0", "#4FC06A", "#A47AE0", "#D97B1F",
        "#E0525C", "#3FBFBF", "#C9C93C", "#E06AC0", "#7A8CFF",
    };

    /// <summary>Unknown/legacy/null preset name falls back to <see cref="Default"/>.</summary>
    public static string SanitizePresetName(string? name) =>
        name is not null && Names.Contains(name) ? name : Default;

    /// <summary>Exactly #RRGGBB, case-insensitive.</summary>
    public static bool IsValidHex(string? hex) =>
        hex is not null && Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$");

    /// <summary>Invalid input sanitizes to null (= "use the preset's accent").</summary>
    public static string? SanitizeAccentHex(string? hex) => IsValidHex(hex) ? hex!.ToUpperInvariant() : null;
}
