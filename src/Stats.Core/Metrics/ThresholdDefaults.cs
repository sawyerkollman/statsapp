using Stats.Core.Settings;

namespace Stats.Core.Metrics;

public static class ThresholdDefaults
{
    public static List<ThresholdRule> Rules() => new()
    {
        new() { Group = MetricGroup.Cpu, Unit = "°C", Warn = 85, Crit = 92 },
        new() { Group = MetricGroup.Gpu, Unit = "°C", Warn = 80, Crit = 88 },
        new() { Group = MetricGroup.Cpu, Unit = "%", Warn = 90, Crit = 98 },
        new() { Group = MetricGroup.Gpu, Unit = "%", Warn = 90, Crit = 98 },
    };
}
