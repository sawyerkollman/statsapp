using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class TileDimensionsTests
{
    [Theory]
    [InlineData(TileSize.S, 160.0, 80.0)]
    [InlineData(TileSize.M, 224.0, 144.0)]
    [InlineData(TileSize.L, 460.0, 192.0)]
    public void Of_ReturnsSpecSizes(TileSize size, double width, double height)
    {
        var (w, h) = TileDimensions.Of(size);
        Assert.Equal(width, w);
        Assert.Equal(height, h);
    }
}

public class DashboardLayoutTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(7.0, 0.0)]     // below half a grid step (8) rounds down
    [InlineData(8.0, 16.0)]    // exact half rounds up ("half-up")
    [InlineData(9.0, 16.0)]
    [InlineData(15.0, 16.0)]
    [InlineData(16.0, 16.0)]
    [InlineData(24.0, 32.0)]   // another exact half
    [InlineData(100.0, 96.0)]
    public void Snap_RoundsToNearestGridStep_HalfUp(double input, double expected)
    {
        Assert.Equal(expected, DashboardLayout.Snap(input));
    }

    [Theory]
    [InlineData(50.0, 50.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(-500.0, 0.0)]
    public void Clamp_NeverNegative(double input, double expected)
    {
        Assert.Equal(expected, DashboardLayout.Clamp(input));
    }

    [Fact]
    public void GridSizeAndGap_MatchSpec()
    {
        Assert.Equal(16, DashboardLayout.GridSize);
        Assert.Equal(12, DashboardLayout.Gap);
    }
}
