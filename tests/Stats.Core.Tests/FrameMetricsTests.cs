using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class FrameMetricsTests
{
    [Fact]
    public void Definitions_ThreeGameMetrics_WithExpectedIdsUnitsFormats()
    {
        var defs = FrameMetrics.Definitions;
        Assert.Equal(new[] { "fps.avg", "fps.low1", "fps.frametime" }, defs.Select(d => d.Id));
        Assert.All(defs, d => Assert.Equal(MetricGroup.Game, d.Group));
        Assert.All(defs, d => Assert.Equal("Foreground app", d.HardwareName));
        Assert.Equal(("fps", "F0"), (defs[0].Unit, defs[0].Format));
        Assert.Equal(("fps", "F0"), (defs[1].Unit, defs[1].Format));
        Assert.Equal(("ms", "F1"), (defs[2].Unit, defs[2].Format));
    }

    [Theory]
    [InlineData("fps.avg", true)]
    [InlineData("fps.frametime", true)]
    [InlineData("cpu.temp", false)]
    [InlineData("", false)]
    public void IsFrameMetric_MatchesPrefix(string id, bool expected) =>
        Assert.Equal(expected, FrameMetrics.IsFrameMetric(id));

    [Fact]
    public void Game_IsLastEnumMember_SoSerializedRulesStayStable() =>
        Assert.Equal(Enum.GetValues<MetricGroup>().Max(), MetricGroup.Game);

    [Fact]
    public void DashboardViewModel_PlacesGameSectionLast()
    {
        var defs = new List<MetricDefinition>
        {
            new("cpu.l", "CPU Total", MetricGroup.Cpu, "CPU", "%"),
            new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps"),
            new("net.d", "Eth · Download", MetricGroup.Network, "Eth", "B/s"),
        };
        var store = new MetricStore(defs);
        var s = new AppSettings { DashboardMetrics = new() { "fps.avg", "net.d", "cpu.l" } };
        var vm = new DashboardViewModel(store, s, () => { });
        Assert.Equal(new[] { "Cpu", "Network", "Game" }, vm.Sections.Select(x => x.Name));
    }
}
