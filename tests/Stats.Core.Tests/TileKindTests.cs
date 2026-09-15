using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class TileKindTests
{
    [Fact]
    public void TileKind_ExistingOrdinalsStable_NewMembersAppended()
    {
        Assert.Equal(0, (int)TileKind.Auto);
        Assert.Equal(1, (int)TileKind.Sparkline);
        Assert.Equal(2, (int)TileKind.Gauge);
        Assert.Equal(3, (int)TileKind.Bar);
        Assert.Equal(4, (int)TileKind.Value);
        Assert.Equal(5, (int)TileKind.Histogram);
        Assert.Equal(6, (int)TileKind.FpsSummary);
    }
}
