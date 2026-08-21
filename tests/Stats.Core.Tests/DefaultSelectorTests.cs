using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class DefaultSelectorTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp.tctl", "Core (Tctl/Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.temp.ccd1", "CCD1 (Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.load.total", "CPU Total", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.load.core1", "CPU Core #1", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.power.package", "Package", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("cpu.power.ppt", "CPU PPT", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("gpu.temp.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("gpu.clock.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.clock.mem", "RTX · GPU Memory", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.power.total", "RTX · GPU Package", MetricGroup.Gpu, "RTX", "W", "F1"),
        new("mem.load", "Memory", MetricGroup.Memory, "Generic Memory", "%"),
        new("disk.c.activity", "SSD-C · Total Activity", MetricGroup.Storage, "SSD-C", "%"),
        new("disk.c.used", "SSD-C · Used Space", MetricGroup.Storage, "SSD-C", "%"),
        new("disk.d.activity", "HDD-D · Total Activity", MetricGroup.Storage, "HDD-D", "%"),
        new("net.eth4.down", "Eth4 · Download Speed", MetricGroup.Network, "Eth4", "B/s"),
        new("net.eth4.up", "Eth4 · Upload Speed", MetricGroup.Network, "Eth4", "B/s"),
        new("net.eth5.down", "Eth5 · Download Speed", MetricGroup.Network, "Eth5", "B/s"),
    };

    [Fact]
    public void DashboardDefaults_PicksHeadlineMetricsAndPerInstanceEntries()
    {
        var ids = DefaultSelector.DashboardDefaults(Defs);
        Assert.Contains("cpu.temp.tctl", ids);      // prefers tctl over ccd
        Assert.Contains("cpu.load.total", ids);      // prefers total over per-core
        Assert.Contains("cpu.power.package", ids);
        Assert.Contains("cpu.power.ppt", ids);
        Assert.Contains("gpu.temp.core", ids);
        Assert.Contains("gpu.clock.core", ids);
        Assert.Contains("gpu.power.total", ids);
        Assert.Contains("mem.load", ids);
        Assert.Contains("disk.c.activity", ids);     // activity, not used-space
        Assert.Contains("disk.d.activity", ids);     // one per disk
        Assert.Contains("net.eth4.down", ids);       // download, not upload
        Assert.Contains("net.eth5.down", ids);       // one per adapter
        Assert.DoesNotContain("cpu.load.core1", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void OverlayDefaults_CpuTempGpuTempGpuClock()
    {
        var ids = DefaultSelector.OverlayDefaults(Defs);
        Assert.Equal(new[] { "cpu.temp.tctl", "gpu.temp.core", "gpu.clock.core" }, ids);
    }

    [Fact]
    public void Defaults_EmptyDiscovery_ReturnsEmpty()
    {
        Assert.Empty(DefaultSelector.DashboardDefaults(new List<MetricDefinition>()));
        Assert.Empty(DefaultSelector.OverlayDefaults(new List<MetricDefinition>()));
    }
}
