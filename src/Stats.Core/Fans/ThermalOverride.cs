using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>Decides when software fan control must stop following a quiet curve or a manual duty and drive every
/// channel to its maximum. Thresholds default to the CPU/GPU °C crit values in <see cref="ThresholdDefaults"/>
/// and follow the user's rules when those are loaded. The latch (see <see cref="ShouldForceFullSpeed"/>) keeps
/// the override on until the temperature falls by the caller's hysteresis, so a reading that chatters around
/// the line does not slam fans between max and the profile every tick.</summary>
public static class ThermalOverride
{
    public const float DefaultCpuCritC = 92f;
    public const float DefaultGpuCritC = 88f;

    public static (float Cpu, float Gpu) CriticalTemperatures(IEnumerable<ThresholdRule>? rules)
    {
        float cpu = DefaultCpuCritC, gpu = DefaultGpuCritC;
        if (rules is null) return (cpu, gpu);
        foreach (var rule in rules)
        {
            if (rule is null || rule.LowerIsWorse || !string.Equals(rule.Unit, "°C", StringComparison.Ordinal)) continue;
            if (!float.IsFinite(rule.Crit) || rule.Crit <= 0f) continue;
            if (rule.Group == MetricGroup.Cpu) cpu = rule.Crit;
            else if (rule.Group == MetricGroup.Gpu) gpu = rule.Crit;
        }
        return (cpu, gpu);
    }

    /// <param name="latched">True when the previous tick was already forcing full speed. The clear line is then
    /// <paramref name="clearHysteresisC"/> below each crit, so the override stays on while the chip is still hot.</param>
    public static bool ShouldForceFullSpeed(SensorSnapshot snapshot, bool latched, float cpuCritC, float gpuCritC, float clearHysteresisC)
    {
        float cpuLine = latched ? cpuCritC - clearHysteresisC : cpuCritC;
        float gpuLine = latched ? gpuCritC - clearHysteresisC : gpuCritC;
        foreach (var (id, value) in snapshot.Values)
        {
            if (value is not float t || !float.IsFinite(t)) continue;
            if (IsCpuTemperature(id) && t >= cpuLine) return true;
            if (IsGpuTemperature(id) && t >= gpuLine) return true;
        }
        return false;
    }

    public static bool IsCpuTemperature(string? id) => IsGroupTemperature(id, "cpu");
    public static bool IsGpuTemperature(string? id) => IsGroupTemperature(id, "gpu");

    /// <summary>Mapped sensor ids are <c>{group}.{hardware}.temperature.{name}</c>. <c>cpu.temp</c> /
    /// <c>gpu.temp</c> are the short ids a few callers use.</summary>
    public static bool IsGroupTemperature(string? id, string group)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (id.Equals(group + ".temp", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = id.Split('.');
        return parts.Length >= 3
            && parts[0].Equals(group, StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("temperature", StringComparison.OrdinalIgnoreCase);
    }
}
