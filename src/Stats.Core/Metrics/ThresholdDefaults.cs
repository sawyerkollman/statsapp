using Stats.Core.Frames;
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

    /// <summary>Per-metric defaults for metrics the (Group, Unit) rules would otherwise colour wrongly.
    /// A 1 % low sits far below the average by definition, so the 60/30 fps rule would leave that tile
    /// permanently Warn/Crit — which teaches the user to ignore threshold colour everywhere else.</summary>
    public static Dictionary<string, ThresholdRule> Overrides() => new()
    {
        [FrameMetrics.LowId] = new ThresholdRule { Warn = 30, Crit = 15, LowerIsWorse = true },
    };

    /// <summary>Adds any default (Group, Unit) rule missing from <paramref name="rules"/>, and (when given) any
    /// default per-metric override whose key is absent. Never changes existing rules or overrides.</summary>
    public static void EnsureDefaults(List<ThresholdRule> rules, Dictionary<string, ThresholdRule>? overrides = null)
    {
        foreach (var d in Rules())
            if (!rules.Any(r => r.Group == d.Group && r.Unit == d.Unit)) rules.Add(d);
        if (overrides is null) return;
        foreach (var (id, rule) in Overrides())
            if (!overrides.ContainsKey(id)) overrides[id] = rule;
    }
}
