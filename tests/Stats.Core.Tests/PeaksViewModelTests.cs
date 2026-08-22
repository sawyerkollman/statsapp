using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class PeaksViewModelTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("gpu.clock", "GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
        new("disk.c", "SSD-C · Activity", MetricGroup.Storage, "SSD-C", "%"),
    };

    private static (PeaksViewModel Vm, MetricStore Store, AppSettings S) Make()
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings { DashboardMetrics = { "gpu.clock", "cpu.temp" }, ThresholdRules = ThresholdDefaults.Rules() };
        s.PrefFor("cpu.temp").Name = "CPU Temp";
        return (new PeaksViewModel(store, s), store, s);
    }

    [Fact]
    public void Rows_FollowDashboardSelectionOrder_WithFriendlyNames()
    {
        var (vm, _, _) = Make();
        Assert.Equal(new[] { "gpu.clock", "cpu.temp" }, vm.Rows.Select(r => r.Id));
        Assert.Equal("CPU Temp", vm.Rows[1].Name);
    }

    [Fact]
    public void IncludeAll_ShowsEveryDiscoveredMetric()
    {
        var (vm, _, _) = Make();
        vm.IncludeAll = true;
        Assert.Equal(3, vm.Rows.Count);
        vm.IncludeAll = false;
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void Refresh_FillsNowMinAvgMax_AndSeverity()
    {
        var (vm, store, _) = Make();
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 80f, ["gpu.clock"] = 2000f }, DateTime.UtcNow));
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 94f, ["gpu.clock"] = 2400f }, DateTime.UtcNow));
        vm.Refresh();
        var cpu = vm.Rows[1];
        Assert.Equal("94.0 °C", cpu.NowText);
        Assert.Equal("80.0 °C", cpu.MinText);
        Assert.Equal("87.0 °C", cpu.AvgText);
        Assert.Equal("94.0 °C", cpu.MaxText);
        Assert.Equal(Severity.Crit, cpu.Severity);
    }

    [Fact]
    public void ResetSession_ClearsStoreStats()
    {
        var (vm, store, _) = Make();
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 80f }, DateTime.UtcNow));
        vm.ResetSessionCommand.Execute(null);
        Assert.True(float.IsNaN(store["cpu.temp"].SessionMax));
        Assert.Equal("—", vm.Rows[1].MinText);
    }
}
