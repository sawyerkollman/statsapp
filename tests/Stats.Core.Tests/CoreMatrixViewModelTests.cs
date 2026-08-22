using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class CoreMatrixViewModelTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("l1a", "CPU Core #1 Thread #1", MetricGroup.Cpu, "CPU", "%"),
        new("l1b", "CPU Core #1 Thread #2", MetricGroup.Cpu, "CPU", "%"),
        new("c1", "Core #1", MetricGroup.Cpu, "CPU", "MHz"),
        new("t1", "Core #1 (Tctl/Tdie)", MetricGroup.Cpu, "CPU", "°C", "F1"),
        new("l2", "CPU Core #2", MetricGroup.Cpu, "CPU", "%"),
        new("c2", "Core #2", MetricGroup.Cpu, "CPU", "MHz"),
        new("total", "CPU Total", MetricGroup.Cpu, "CPU", "%"),          // no index → ignored
        new("gpu", "GPU Core", MetricGroup.Gpu, "GPU", "MHz"),            // not CPU → ignored
    };

    [Fact]
    public void Ctor_FindsCoresByIndex_AndIgnoresNonCoreMetrics()
    {
        var vm = new CoreMatrixViewModel(new MetricStore(Defs), new AppSettings());
        Assert.True(vm.HasCores);
        Assert.Equal(new[] { 1, 2 }, vm.Cells.Select(c => c.Index));
        Assert.Equal(2, vm.Columns);
    }

    [Fact]
    public void Refresh_AveragesThreadLoads_AndFormatsClockAndTemp()
    {
        var store = new MetricStore(Defs);
        var vm = new CoreMatrixViewModel(store, new AppSettings { ThresholdRules = ThresholdDefaults.Rules() });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        {
            ["l1a"] = 20f, ["l1b"] = 60f, ["c1"] = 5200f, ["t1"] = 93f, ["l2"] = 10f, ["c2"] = 4800f,
        }, DateTime.UtcNow));
        vm.Refresh();
        var c1 = vm.Cells[0];
        Assert.Equal("40%", c1.LoadText);
        Assert.Equal(0.4f, c1.Load01, 3);
        Assert.Equal("5200 MHz", c1.ClockText);
        Assert.Equal("93.0 °C", c1.TempText);
        Assert.Equal(Severity.Crit, c1.Severity);
        Assert.Equal("", vm.Cells[1].TempText);
    }

    [Fact]
    public void NoCoreMetrics_HasCoresFalse()
    {
        var vm = new CoreMatrixViewModel(new MetricStore(new[] { Defs[6], Defs[7] }), new AppSettings());
        Assert.False(vm.HasCores);
        Assert.Empty(vm.Cells);
    }

    [Fact]
    public void Columns_CapAtEight()
    {
        var many = Enumerable.Range(1, 16).Select(i => new MetricDefinition($"l{i}", $"CPU Core #{i}", MetricGroup.Cpu, "CPU", "%")).ToList();
        var vm = new CoreMatrixViewModel(new MetricStore(many), new AppSettings());
        Assert.Equal(16, vm.Cells.Count);
        Assert.Equal(8, vm.Columns);
    }

    [Fact]
    public void Temp_PrefersExactCoreNameOverDistanceToTjMax()
    {
        var defs = new List<MetricDefinition>
        {
            new("d1", "CPU Core #1 Distance to TjMax", MetricGroup.Cpu, "CPU", "°C", "F1"),
            new("t1", "CPU Core #1", MetricGroup.Cpu, "CPU", "°C", "F1"),
            new("l1", "CPU Core #1", MetricGroup.Cpu, "CPU", "%"),
        };
        var store = new MetricStore(defs);
        var vm = new CoreMatrixViewModel(store, new AppSettings());
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["d1"] = 40f, ["t1"] = 60f, ["l1"] = 10f }, DateTime.UtcNow));
        vm.Refresh();
        Assert.Equal("60.0 °C", vm.Cells[0].TempText);
    }
}
