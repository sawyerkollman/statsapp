using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.Settings;
using Stats.Core.Sensors;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class ComparisonViewModelTests
{
    private static readonly MetricDefinition Cpu = new("cpu", "CPU", MetricGroup.Cpu, "CPU", "%");
    private static readonly MetricDefinition Gpu = new("gpu", "GPU", MetricGroup.Gpu, "GPU", "%");

    [Fact]
    public void LiveComparison_DefaultsDashboardMetrics_AndFreezeKeepsSnapshot()
    {
        var store = new MetricStore(new[] { Cpu, Gpu });
        var settings = new AppSettings { DashboardMetrics = { "cpu", "gpu" } };
        var t0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        store.Apply(new(new Dictionary<string, float?> { ["cpu"] = 10, ["gpu"] = null }, t0));
        var vm = new ComparisonViewModel(store, settings);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(t0, vm.AxisStartUtc);
        Assert.True(float.IsNaN(vm.Rows.Single(r => r.Name == "GPU").Values.Single()));

        vm.FreezeCommand.Execute(null);
        store.Apply(new(new Dictionary<string, float?> { ["cpu"] = 20, ["gpu"] = 30 }, t0.AddSeconds(1)));
        vm.RefreshLive();
        Assert.Single(vm.Rows.Single(r => r.Name == "CPU").Values);

        vm.ResumeCommand.Execute(null);
        Assert.Equal(2, vm.Rows.Single(r => r.Name == "CPU").Values.Count);
    }

    [Fact]
    public void CapturedComparison_CapsRowsAtFour_AndUsesExactUtcBounds()
    {
        var definitions = Enumerable.Range(0, 5).Select(i => new MetricDefinition($"m{i}", $"M{i}", MetricGroup.Cpu, "CPU", "%")).ToArray();
        var store = new MetricStore(definitions);
        var start = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var vm = new ComparisonViewModel(store, new AppSettings());
        vm.SetCaptured("Session", definitions.Select((d, i) => new MetricSeries(d, new[] { start.AddSeconds(i) }, new float?[] { i })));

        Assert.Equal(4, vm.Rows.Count);
        Assert.Equal(start, vm.AxisStartUtc);
        Assert.Equal(start.AddSeconds(3), vm.AxisEndUtc);
    }

    [Fact]
    public void LiveRefresh_UpdatesExistingRowsInPlace_AndCapturedModeDisablesLiveCommands()
    {
        var store = new MetricStore(new[] { Cpu, Gpu });
        var settings = new AppSettings { DashboardMetrics = { "cpu", "gpu" } };
        var start = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        store.Apply(new(new Dictionary<string, float?> { ["cpu"] = 10, ["gpu"] = 20 }, start));
        var vm = new ComparisonViewModel(store, settings);
        var cpuRow = vm.Rows.Single(row => row.Series.Definition.Id == "cpu");

        store.Apply(new(new Dictionary<string, float?> { ["cpu"] = 15, ["gpu"] = 25 }, start.AddSeconds(1)));
        vm.RefreshLive();

        Assert.Same(cpuRow, vm.Rows.Single(row => row.Series.Definition.Id == "cpu"));
        Assert.Equal(2, cpuRow.Values.Count);
        Assert.Equal(3, cpuRow.YAxisLabels.Count);
        Assert.All(cpuRow.TimeAxisLabels, label => Assert.EndsWith("UTC", label));

        vm.SetCaptured("Session", [new MetricSeries(Cpu, [start], [10f])]);

        Assert.False(vm.IsLive);
        Assert.True(vm.IsFrozen);
        Assert.False(vm.FreezeCommand.CanExecute(null));
        Assert.False(vm.ResumeCommand.CanExecute(null));

        vm.SwitchToLive();

        Assert.True(vm.IsLive);
        Assert.False(vm.IsFrozen);
    }
}
