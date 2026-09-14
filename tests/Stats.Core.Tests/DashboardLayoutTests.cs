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

    [Theory]
    [InlineData(TileSize.S)]
    [InlineData(TileSize.M)]
    [InlineData(TileSize.L)]
    public void Nearest_ExactPreset_ReturnsIt(TileSize size)
    {
        var (w, h) = TileDimensions.Of(size);
        Assert.Equal(size, TileDimensions.Nearest(w, h));
    }

    [Fact]
    public void Nearest_Midpoints_TieGoesLarger()
    {
        // Exact midpoint between S (160,80) and M (224,144) is (192,112): dist²(S) = 32²+32² = dist²(M) — tie → M.
        Assert.Equal(TileSize.M, TileDimensions.Nearest(192.0, 112.0));
        // Exact midpoint between M (224,144) and L (460,192) is (342,168): dist²(M) = 118²+24² = dist²(L) — tie → L.
        Assert.Equal(TileSize.L, TileDimensions.Nearest(342.0, 168.0));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-5.0, -5.0)]
    [InlineData(double.NaN, double.NaN)]
    public void Nearest_TinyOrNegative_ReturnsS(double width, double height)
    {
        Assert.Equal(TileSize.S, TileDimensions.Nearest(width, height));
    }

    [Fact]
    public void Nearest_Huge_ReturnsL()
    {
        Assert.Equal(TileSize.L, TileDimensions.Nearest(2000.0, 900.0));
    }

    [Fact]
    public void Nearest_WideButShort_PicksByDistance()
    {
        // The design doc's own example — (400, 80) → M "since M (224,144) is nearer than L (460,192)" — doesn't
        // hold up: dist²(M) = 176²+64² = 35072 but dist²(L) = 60²+112² = 16144, so (400, 80) is actually nearer to
        // L. (350, 80) is the corrected point that keeps the intended lesson (a wide-but-short drag must be judged
        // by real 2D distance, not by width alone — nearest-by-width-alone would say L, since |350-460| = 110 is
        // less than |350-224| = 126) while landing on M once height is folded in: dist²(M) = 126²+64² = 19972 is
        // less than dist²(L) = 110²+112² = 24644.
        Assert.Equal(TileSize.M, TileDimensions.Nearest(350.0, 80.0));
    }

    [Theory]
    [InlineData(TileSize.S, -1, TileSize.S)]
    [InlineData(TileSize.L, 1, TileSize.L)]
    [InlineData(TileSize.M, 1, TileSize.L)]
    [InlineData(TileSize.M, -1, TileSize.S)]
    public void Step_SaturatesAtEnds(TileSize size, int delta, TileSize expected)
    {
        Assert.Equal(expected, TileDimensions.Step(size, delta));
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

public class DashboardLayoutModeConverterTests
{
    private static DashboardLayoutMode ReadJson(string json)
    {
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read();
        return new DashboardLayoutModeConverter().Read(ref reader, typeof(DashboardLayoutMode), new System.Text.Json.JsonSerializerOptions());
    }

    [Theory]
    [InlineData("\"Free\"", DashboardLayoutMode.Free)]
    [InlineData("\"grid\"", DashboardLayoutMode.Grid)] // case-insensitive
    [InlineData("\"Auto\"", DashboardLayoutMode.Auto)]
    [InlineData("\"NotARealMode\"", DashboardLayoutMode.Auto)] // unrecognized string
    [InlineData("null", DashboardLayoutMode.Auto)]
    [InlineData("42", DashboardLayoutMode.Auto)]
    [InlineData("true", DashboardLayoutMode.Auto)]
    public void Read_ScalarTokens_ParseOrFallBackToAuto(string json, DashboardLayoutMode expected)
    {
        Assert.Equal(expected, ReadJson(json));
    }

    // S10: a hand-edited "DashboardLayoutMode": {} or [] used to leave the reader positioned on the container's
    // start token instead of consuming to its matching end token, so the whole-object JsonSerializer.Deserialize
    // call downstream throws "read too much or not enough" — defeating the point of this lenient converter. These
    // two must return Auto *and* fully consume the container, which the round-trip test below verifies.
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"nested\":{\"a\":1}}")]
    [InlineData("[1,2,3]")]
    public void Read_ContainerTokens_ReturnsAuto_AndDoesNotCorruptTheParse(string json)
    {
        Assert.Equal(DashboardLayoutMode.Auto, ReadJson(json));
    }

    [Fact]
    public void Read_ObjectToken_InAWholeSettingsDocument_DoesNotThrow()
    {
        // Reproduces the failure mode directly: a hand-edited settings.json with an object where the mode string
        // should be. Deserializing the whole AppSettings object must not throw.
        var json = "{\"DashboardLayoutMode\":{},\"PollIntervalSeconds\":1}";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(settings);
        Assert.Equal(DashboardLayoutMode.Auto, settings!.DashboardLayoutMode);
    }
}
