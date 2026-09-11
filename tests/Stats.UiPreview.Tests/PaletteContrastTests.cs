using System.Windows.Media;
using Stats.App.Helpers;
using Stats.Core.Settings;

namespace Stats.UiPreview.Tests;

/// <summary>DESIGN.md §2 ("Palette and states"): "Set project acceptance targets of 4.5:1 contrast for small
/// essential text and 3:1 for essential control boundaries/focus indicators. Measure final resolved colors."
/// Computes WCAG 2.x contrast ratio for every preset ThemeManager.Apply can produce (via the test-only
/// ThemeManager.PaletteFor accessor) — never against a running Application, so this runs headless. Custom
/// accents are user-chosen and explicitly out of scope (ui-polish T2 task spec).</summary>
public class PaletteContrastTests
{
    private static readonly string[] TextPairBackgrounds = { "WindowBg", "TileBg", "FlyoutBg", "ControlBg" };
    private static readonly string[] SeverityBackgrounds = { "TileBg", "WindowBg" };

    public static IEnumerable<object[]> SmallTextCases()
    {
        foreach (var preset in ThemePresets.Names)
        foreach (var fg in new[] { "TextPrimary", "TextSecondary" })
        foreach (var bg in TextPairBackgrounds)
            yield return new object[] { preset, fg, bg };

        foreach (var preset in ThemePresets.Names)
        foreach (var fg in new[] { "WarnBrush", "CritBrush" })
        foreach (var bg in SeverityBackgrounds)
            yield return new object[] { preset, fg, bg };
    }

    public static IEnumerable<object[]> ControlBoundaryCases()
    {
        foreach (var preset in ThemePresets.Names)
        foreach (var bg in TextPairBackgrounds)
            yield return new object[] { preset, "AccentBrush", bg };
    }

    [Theory]
    [MemberData(nameof(SmallTextCases))]
    public void SmallEssentialText_meets_4_5_to_1(string preset, string foregroundKey, string backgroundKey)
    {
        var ratio = ContrastRatio(preset, foregroundKey, backgroundKey);
        Assert.True(ratio >= 4.5,
            $"{preset}: {foregroundKey} on {backgroundKey} measured {ratio:0.00}:1, below the 4.5:1 target.");
    }

    [Theory]
    [MemberData(nameof(ControlBoundaryCases))]
    public void AccentControlBoundary_meets_3_to_1(string preset, string foregroundKey, string backgroundKey)
    {
        var ratio = ContrastRatio(preset, foregroundKey, backgroundKey);
        Assert.True(ratio >= 3.0,
            $"{preset}: {foregroundKey} on {backgroundKey} measured {ratio:0.00}:1, below the 3:1 target.");
    }

    private static double ContrastRatio(string preset, string foregroundKey, string backgroundKey)
    {
        var palette = ThemeManager.PaletteFor(preset);
        var fg = ParseColor(palette[foregroundKey]);
        var bg = ParseColor(palette[backgroundKey]);
        var lFg = RelativeLuminance(fg);
        var lBg = RelativeLuminance(bg);
        var (lighter, darker) = lFg >= lBg ? (lFg, lBg) : (lBg, lFg);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>WCAG 2.x relative luminance (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance).</summary>
    private static double RelativeLuminance(Color c)
    {
        double R = Channel(c.R), G = Channel(c.G), B = Channel(c.B);
        return 0.2126 * R + 0.7152 * G + 0.0722 * B;

        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }
}
