using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class ThermalOverrideTests
{
    private static SensorSnapshot Snap(params (string Id, float? Value)[] values) =>
        new(values.ToDictionary(v => v.Id, v => v.Value), DateTime.UnixEpoch);

    [Fact]
    public void CriticalTemperatures_DefaultsMatchThresholdRules()
    {
        var (cpu, gpu) = ThermalOverride.CriticalTemperatures(null);

        Assert.Equal(92f, cpu);
        Assert.Equal(88f, gpu);
    }

    [Fact]
    public void CriticalTemperatures_UsesCpuAndGpuCelsiusRules_IgnoresOtherUnits()
    {
        var rules = new[]
        {
            new ThresholdRule { Group = MetricGroup.Cpu, Unit = "°C", Crit = 80 },
            new ThresholdRule { Group = MetricGroup.Gpu, Unit = "°C", Crit = 70 },
            new ThresholdRule { Group = MetricGroup.Cpu, Unit = "%", Crit = 10 },
            new ThresholdRule { Group = MetricGroup.Game, Unit = "fps", Crit = 30, LowerIsWorse = true },
        };

        var (cpu, gpu) = ThermalOverride.CriticalTemperatures(rules);

        Assert.Equal(80f, cpu);
        Assert.Equal(70f, gpu);
    }

    [Theory]
    [InlineData("cpu.amd-ryzen.temperature.tctl", true, false)]
    [InlineData("cpu.temp", true, false)]
    [InlineData("gpu.nvidia.temperature.gpu-core", false, true)]
    [InlineData("gpu.temp", false, true)]
    [InlineData("cpu.amd.load.total", false, false)]
    [InlineData("motherboard.ite.temperature.system", false, false)]
    public void TemperatureId_MatchesCpuAndGpuOnly(string id, bool cpu, bool gpu)
    {
        Assert.Equal(cpu, ThermalOverride.IsCpuTemperature(id));
        Assert.Equal(gpu, ThermalOverride.IsGpuTemperature(id));
    }

    [Fact]
    public void ShouldForceFullSpeed_EngagesAtCrit_AndHoldsThroughHysteresis()
    {
        var cool = Snap(("cpu.amd.temperature.tctl", 91f));
        var hot = Snap(("cpu.amd.temperature.tctl", 92f));
        var falling = Snap(("cpu.amd.temperature.tctl", 90f));
        var clear = Snap(("cpu.amd.temperature.tctl", 89f));
        var gpuHot = Snap(("gpu.nvidia.temperature.core", 88f));

        Assert.False(ThermalOverride.ShouldForceFullSpeed(cool, latched: false, 92f, 88f, 2f));
        Assert.True(ThermalOverride.ShouldForceFullSpeed(hot, latched: false, 92f, 88f, 2f));
        Assert.True(ThermalOverride.ShouldForceFullSpeed(gpuHot, latched: false, 92f, 88f, 2f));
        Assert.True(ThermalOverride.ShouldForceFullSpeed(falling, latched: true, 92f, 88f, 2f));
        Assert.False(ThermalOverride.ShouldForceFullSpeed(clear, latched: true, 92f, 88f, 2f));
        Assert.False(ThermalOverride.ShouldForceFullSpeed(falling, latched: false, 92f, 88f, 2f));
    }
}
