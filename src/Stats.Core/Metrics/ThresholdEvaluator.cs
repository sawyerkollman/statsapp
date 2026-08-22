using Stats.Core.Settings;

namespace Stats.Core.Metrics;

public static class ThresholdEvaluator
{
    /// <summary>Per-metric override → first (Group, Unit) rule → Normal. Null/NaN → Normal.</summary>
    public static Severity Evaluate(MetricDefinition def, float? value, AppSettings settings)
    {
        if (value is not float v || float.IsNaN(v)) return Severity.Normal;
        var rule = settings.ThresholdOverrides.TryGetValue(def.Id, out var o)
            ? o
            : settings.ThresholdRules.FirstOrDefault(r => r.Group == def.Group && r.Unit == def.Unit);
        if (rule is null) return Severity.Normal;
        if (v >= rule.Crit) return Severity.Crit;
        if (v >= rule.Warn) return Severity.Warn;
        return Severity.Normal;
    }
}
