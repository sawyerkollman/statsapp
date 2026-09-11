using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

/// <summary>v1.8 UI-polish §4: ValueText/UnitText must always agree with CurrentText (the tile templates bind
/// the split pair instead of splitting CurrentText on a space).</summary>
public class MetricTileViewModelTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition NetRx = new("net.rx", "Download", MetricGroup.Network, "NIC", "B/s", "F0");

    private static MetricStore NewStore(params MetricDefinition[] defs) => new(defs);

    [Fact]
    public void Refresh_ValueTextAndUnitText_AgreeWithCurrentText()
    {
        var store = NewStore(CpuTemp);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 42.5f }, DateTime.UtcNow));
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], new AppSettings());
        tile.Refresh();
        Assert.Equal("42.5 °C", tile.CurrentText);
        Assert.Equal("42.5", tile.ValueText);
        Assert.Equal("°C", tile.UnitText);
        Assert.Equal($"{tile.ValueText} {tile.UnitText}", tile.CurrentText);
    }

    [Fact]
    public void Refresh_Throughput_ValueTextAndUnitText_AutoScale()
    {
        var store = NewStore(NetRx);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["net.rx"] = 12_400_000f }, DateTime.UtcNow));
        var tile = new MetricTileViewModel(NetRx, store["net.rx"], new AppSettings());
        tile.Refresh();
        Assert.Equal("12.4 MB/s", tile.CurrentText);
        Assert.Equal("12.4", tile.ValueText);
        Assert.Equal("MB/s", tile.UnitText);
    }

    [Fact]
    public void Refresh_MissingValue_ValueTextIsDash_UnitTextEmpty()
    {
        var store = NewStore(CpuTemp);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], new AppSettings());
        tile.Refresh();
        Assert.Equal("—", tile.CurrentText);
        Assert.Equal("—", tile.ValueText);
        Assert.Equal("", tile.UnitText);
    }
}
