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
        new() { Group = MetricGroup.Game, Unit = "fps", Warn = 60, Crit = 30, LowerIsWorse = true },
    };

    /// <summary>Adds any default (Group, Unit) rule missing from <paramref name="rules"/>. Never changes existing rules.</summary>
    public static void EnsureDefaults(List<ThresholdRule> rules)
    {
        foreach (var d in Rules())
            if (!rules.Any(r => r.Group == d.Group && r.Unit == d.Unit)) rules.Add(d);
    }
}
