using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class ThemePresetsTests
{
    [Fact]
    public void Names_ContainsAllFivePresetsInOrder()
    {
        Assert.Equal(new[] { "Dark Amber", "Dark Blue", "Dark Green", "Dark Purple", "Light" }, ThemePresets.Names);
    }

    [Theory]
    [InlineData("Dark Amber")]
    [InlineData("Dark Blue")]
    [InlineData("Dark Green")]
    [InlineData("Dark Purple")]
    [InlineData("Light")]
    public void SanitizePresetName_KnownName_PassesThrough(string name)
    {
        Assert.Equal(name, ThemePresets.SanitizePresetName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Neon Pink")]
    [InlineData("dark amber")] // case-sensitive: not a silent match
    public void SanitizePresetName_UnknownOrNull_FallsBackToDefault(string? name)
    {
        Assert.Equal(ThemePresets.Default, ThemePresets.SanitizePresetName(name));
    }

    [Theory]
    [InlineData("#E68A2E", true)]
    [InlineData("#000000", true)]
    [InlineData("#ffffff", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("E68A2E", false)]      // missing #
    [InlineData("#E68A2", false)]      // too short
    [InlineData("#E68A2EE", false)]    // too long
    [InlineData("#GGGGGG", false)]     // not hex digits
    public void IsValidHex_ValidatesExactRrggbbFormat(string? hex, bool expected)
    {
        Assert.Equal(expected, ThemePresets.IsValidHex(hex));
    }

    [Fact]
    public void SanitizeAccentHex_Valid_UppercasesAndPassesThrough()
    {
        Assert.Equal("#4A9EE0", ThemePresets.SanitizeAccentHex("#4a9ee0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-color")]
    public void SanitizeAccentHex_Invalid_ReturnsNull(string? hex)
    {
        Assert.Null(ThemePresets.SanitizeAccentHex(hex));
    }

    [Fact]
    public void AccentSwatches_AreAllValidHex()
    {
        Assert.NotEmpty(ThemePresets.AccentSwatches);
        Assert.All(ThemePresets.AccentSwatches, hex => Assert.True(ThemePresets.IsValidHex(hex)));
    }
}
