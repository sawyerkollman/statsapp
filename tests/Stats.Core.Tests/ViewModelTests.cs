using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class ViewModelTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition CpuPpt = new("cpu.ppt", "CPU PPT", MetricGroup.Cpu, "CPU", "W", "F1");
    private static readonly MetricDefinition GpuClock = new("gpu.clock", "GPU Core", MetricGroup.Gpu, "GPU", "MHz");

    private static MetricStore NewStore() => new(new[] { CpuTemp, CpuPpt, GpuClock });

    private static void Push(MetricStore store, float temp, float ppt, float clock) =>
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        { ["cpu.temp"] = temp, ["cpu.ppt"] = ppt, ["gpu.clock"] = clock }, DateTime.UtcNow));

    [Fact]
    public void Tile_Refresh_FormatsCurrentMinMax()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        Push(store, 45f, 71f, 2500f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"]);
        tile.Refresh();
        Assert.Equal("45.0 °C", tile.CurrentText);
        Assert.Contains("40.0", tile.MinMaxText);
        Assert.Contains("45.0", tile.MinMaxText);
        Assert.Contains("avg 42.5", tile.MinMaxText);
        Assert.Equal(2, tile.HistoryValues.Length);
    }

    [Fact]
    public void Tile_WithZeroLimit_ShowsNoLimitText()
    {
        var store = NewStore();
        Push(store, 40f, 75f, 2400f);
        var tile = new MetricTileViewModel(CpuPpt, store["cpu.ppt"], limit: 0f);
        tile.Refresh();
        Assert.Equal("", tile.LimitText);
    }

    [Fact]
    public void Tile_WithLimit_ShowsPercentOfLimit()
    {
        var store = NewStore();
        Push(store, 40f, 75f, 2400f);
        var tile = new MetricTileViewModel(CpuPpt, store["cpu.ppt"], limit: 150f);
        tile.Refresh();
        Assert.Equal("50% of 150.0 W", tile.LimitText);
    }

    [Fact]
    public void Dashboard_BuildsTilesOnlyForSelectedMetrics_InDefinitionOrder()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "gpu.clock", "cpu.temp" } };
        var vm = new DashboardViewModel(store, settings, () => { });
        Assert.Equal(new[] { "cpu.temp", "gpu.clock" }, vm.Tiles.Select(t => t.Definition.Id));
    }

    [Fact]
    public void Picker_Uncheck_RemovesTileAndUpdatesSettingsAndSaves()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "cpu.temp" } };
        int saves = 0;
        var vm = new DashboardViewModel(store, settings, () => saves++);
        var item = vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp");

        item.IsChecked = false;
        Assert.Empty(vm.Tiles);
        Assert.Empty(settings.DashboardMetrics);
        Assert.Equal(1, saves);

        item.IsChecked = true;
        Assert.Single(vm.Tiles);
        Assert.Contains("cpu.temp", settings.DashboardMetrics);
    }

    [Fact]
    public void Picker_OverlayToggle_UpdatesOverlaySettingsAndRaisesEvent()
    {
        var store = NewStore();
        var settings = new AppSettings();
        var vm = new DashboardViewModel(store, settings, () => { });
        int raised = 0;
        vm.OverlayMetricsChanged += () => raised++;

        vm.PickerItems.Single(p => p.Definition.Id == "gpu.clock").IsOnOverlay = true;
        Assert.Contains("gpu.clock", settings.OverlayMetrics);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Overlay_Rebuild_TracksSettings()
    {
        var store = NewStore();
        var settings = new AppSettings { OverlayMetrics = { "cpu.temp" } };
        var vm = new OverlayViewModel(store, settings);
        Assert.Single(vm.Tiles);
        settings.OverlayMetrics.Add("gpu.clock");
        vm.Rebuild();
        Assert.Equal(2, vm.Tiles.Count);
    }
}
