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
    [InlineData("\"99\"", DashboardLayoutMode.Auto)]
    [InlineData("\"-1\"", DashboardLayoutMode.Auto)]
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
