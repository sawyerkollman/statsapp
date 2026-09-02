using Stats.Core.Metrics;
using Stats.Core.Tray;

namespace Stats.Core.Tests;

public class TrayMetricSelectorTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp.tctl", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.load.total", "CPU Total", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.power.package", "Package", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("gpu.temp.core", "GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("gpu.load.total", "GPU Load", MetricGroup.Gpu, "RTX", "%"),
        new("mem.load", "Memory", MetricGroup.Memory, "Generic Memory", "%"),
        new("net.eth.down", "Download", MetricGroup.Network, "Eth", "B/s"),
    };

    [Fact]
    public void Resolve_NullId_ReturnsNull()
    {
        Assert.Null(TrayMetricSelector.Resolve(null, Defs));
    }

    [Fact]
    public void Resolve_MissingId_ReturnsNull()
    {
        Assert.Null(TrayMetricSelector.Resolve("nope", Defs));
    }

    [Fact]
    public void Resolve_KnownId_ReturnsDefinition()
    {
        var def = TrayMetricSelector.Resolve("gpu.temp.core", Defs);
        Assert.NotNull(def);
        Assert.Equal("GPU Core", def!.DisplayName);
    }

    [Fact]
    public void Candidates_OnlyDegreesOrPercent_ExcludesOtherUnits()
    {
        var candidates = TrayMetricSelector.Candidates(Defs);
        Assert.DoesNotContain(candidates, d => d.Id == "cpu.power.package"); // W
        Assert.DoesNotContain(candidates, d => d.Id == "net.eth.down");      // B/s
        Assert.Contains(candidates, d => d.Id == "cpu.temp.tctl");
        Assert.Contains(candidates, d => d.Id == "cpu.load.total");
        Assert.Contains(candidates, d => d.Id == "gpu.temp.core");
        Assert.Contains(candidates, d => d.Id == "gpu.load.total");
        Assert.Contains(candidates, d => d.Id == "mem.load");
    }

    [Fact]
    public void Candidates_OrderedByGroupOrderThenDisplayName()
    {
        var candidates = TrayMetricSelector.Candidates(Defs);
        Assert.Equal(
            new[] { "cpu.load.total", "cpu.temp.tctl", "gpu.temp.core", "gpu.load.total", "mem.load" },
            candidates.Select(d => d.Id));
    }

    [Fact]
    public void Candidates_EmptyDiscovery_ReturnsEmpty()
    {
        Assert.Empty(TrayMetricSelector.Candidates(new List<MetricDefinition>()));
    }
}
