using Stats.Core.Settings;

namespace Stats.Core.Tests;

public sealed class ThemeStudioTests
{
    [Theory]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    public void ThemeDesign_RejectsBadAccent(string accent) => Assert.Throws<InvalidDataException>(() => new ThemeDesign("x", ThemePresets.Default, accent).Validate());

    [Fact]
    public void ThemeDesign_RejectsUnknownVersionAndNormalizesNullAccents()
    {
        Assert.Throws<InvalidDataException>(() => new ThemeDesign("x", ThemePresets.Default, Version: 2).Validate());
        var design = new ThemeDesign(" x ", ThemePresets.Default).Validate();
        Assert.Equal("x", design.Name); Assert.Null(design.Accent); Assert.Null(design.Secondary);
    }
}
