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

    [Fact]
    public void Refresh_FillsMinAtMaxAtText_WithLocalTimeOfExtremes()
    {
        var (vm, store, _) = Make();
        var minAt = new DateTime(2024, 3, 1, 8, 15, 0, DateTimeKind.Utc);
        var maxAt = new DateTime(2024, 3, 1, 9, 42, 0, DateTimeKind.Utc);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 80f }, minAt));
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 94f }, maxAt));
        vm.Refresh();
        var cpu = vm.Rows[1];
        Assert.Equal($"at {minAt.ToLocalTime():HH:mm}", cpu.MinAtText);
        Assert.Equal($"at {maxAt.ToLocalTime():HH:mm}", cpu.MaxAtText);
    }

    [Fact]
    public void Refresh_NoSampleYet_MinAtMaxAtTextAreEmpty()
    {
        var (vm, _, _) = Make();
        vm.Refresh();
        var cpu = vm.Rows[1];
        Assert.Equal("", cpu.MinAtText);
        Assert.Equal("", cpu.MaxAtText);
    }

    [Fact]
    public void ToTsv_ProducesHeaderAndTabSeparatedRows_WithInvariantCultureNumbers()
    {
        var (vm, store, _) = Make();
        var at = new DateTime(2024, 3, 1, 8, 15, 30, DateTimeKind.Utc);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 80.25f, ["gpu.clock"] = 2000f }, at));
        vm.Refresh();
        var tsv = vm.ToTsv();
        var lines = tsv.Split('\n');
        Assert.Equal("Metric\tNow\tMin\tMin time\tAvg\tMax\tMax time", lines[0]);
        // Rows follow dashboard order: gpu.clock, then cpu.temp (with its friendly name).
        var expectedTime = at.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"CPU Temp\t80.25\t80.25\t{expectedTime}\t80.25\t80.25\t{expectedTime}", lines[2]);
    }

    [Fact]
    public void ToTsv_NoSampleYet_LeavesNumberAndTimeColumnsEmpty()
    {
        var (vm, _, _) = Make();
        var tsv = vm.ToTsv();
        var lines = tsv.Split('\n');
        Assert.Equal("GPU Core\t\t\t\t\t\t", lines[1]);
    }

    [Fact]
    public void CopyError_SetNonEmpty_SetsHasCopyErrorTrue()
    {
        var (vm, _, _) = Make();
        Assert.False(vm.HasCopyError);
        vm.CopyError = "Copy failed: access denied";
        Assert.True(vm.HasCopyError);
        vm.CopyError = "";
        Assert.False(vm.HasCopyError);
    }
}
