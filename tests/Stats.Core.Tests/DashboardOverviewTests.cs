using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class DashboardOverviewTests
{
    private static readonly List<MetricDefinition> Definitions = new()
    {
        new("cpu.temp", "CPU temperature", MetricGroup.Cpu, "CPU", "°C", "F1"),
        new("cpu.power", "CPU power", MetricGroup.Cpu, "CPU", "W", "F1"),
        new("gpu.temp", "GPU temperature", MetricGroup.Gpu, "GPU", "°C", "F1"),
        new("memory.used", "Memory used", MetricGroup.Memory, "Memory", "GB", "F1"),
        new("fps.avg", "FPS", MetricGroup.Game, "Game", "fps", "F0"),
        new("disk.busy", "Disk busy", MetricGroup.Storage, "Disk", "%", "F0"),
    };

    private static (DashboardViewModel Vm, MetricStore Store) Make(params string[] selected)
    {
        var store = new MetricStore(Definitions);
        var settings = new AppSettings { DashboardMetrics = selected.ToList() };
        return (new DashboardViewModel(store, settings, () => { }), store);
    }

    [Fact]
    public void OverviewTiles_UsesFirstSelectedTileForEachEligibleGroup_InDashboardOrder()
    {
        var (vm, _) = Make("disk.busy", "cpu.power", "cpu.temp", "gpu.temp", "memory.used", "fps.avg");

        Assert.Equal(new[] { "cpu.power", "gpu.temp", "memory.used", "fps.avg" },
            vm.OverviewTiles.Select(t => t.Definition.Id));
        Assert.All(vm.OverviewTiles, tile => Assert.Same(tile, vm.Tiles.Single(t => t.Definition.Id == tile.Definition.Id)));
    }

    [Fact]
    public void OverviewTiles_RebuildWhenSelectionChanges_AndDropsRemovedGroup()
    {
        var (vm, _) = Make("cpu.temp", "gpu.temp", "memory.used");

        vm.RemoveTile("gpu.temp");

        Assert.Equal(new[] { "cpu.temp", "memory.used" }, vm.OverviewTiles.Select(t => t.Definition.Id));
        Assert.All(vm.OverviewTiles, tile => Assert.Same(tile, vm.Tiles.Single(t => t.Definition.Id == tile.Definition.Id)));
    }

    [Fact]
    public void OverviewTiles_ReuseLiveTileValues_IncludingUnavailableReadings()
    {
        var (vm, store) = Make("cpu.temp");
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 62.5f }, DateTime.UtcNow));
        vm.RefreshAll();
        var overviewTile = Assert.Single(vm.OverviewTiles);
        Assert.Same(vm.Tiles.Single(), overviewTile);
        Assert.Equal("62.5", overviewTile.ValueText);

        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = null }, DateTime.UtcNow));
        vm.RefreshAll();

        Assert.Equal("—", overviewTile.ValueText);
    }
}
