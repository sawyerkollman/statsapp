using Stats.Core.Settings;

namespace Stats.Core.Metrics;

public static class ThresholdEvaluator
{
    /// <summary>Per-metric override → first (Group, Unit) rule → null. Shared by <see cref="Evaluate"/> and any
    /// caller (e.g. <see cref="Alerts.AlertEngine"/> sample building) that needs the governing rule itself, not
    /// just the severity it produces.</summary>
    public static ThresholdRule? RuleFor(MetricDefinition def, AppSettings settings) =>
        settings.ThresholdOverrides.TryGetValue(def.Id, out var o)
            ? o
            : settings.ThresholdRules.FirstOrDefault(r => r.Group == def.Group && r.Unit == def.Unit);

    /// <summary>Per-metric override → first (Group, Unit) rule → Normal. Null/NaN → Normal. A rule whose Warn
    /// equals its Crit (e.g. a freshly added "0 / 0" rule the user hasn't filled in yet) is inactive — without
    /// this, the "≥ Crit" comparison would mark every non-negative reading Crit before the user ever sets a real
    /// threshold.</summary>
    public static Severity Evaluate(MetricDefinition def, float? value, AppSettings settings) =>
        EvaluateRule(RuleFor(def, settings), value);

    /// <summary>The Warn/Crit/LowerIsWorse comparison itself, given the rule that already governs a metric (its
    /// override, or the first matching (Group, Unit) rule — see <see cref="RuleFor"/>). Shared by
    /// <see cref="Evaluate"/> and <see cref="ThresholdIndex.Evaluate"/> so the two never drift.</summary>
    internal static Severity EvaluateRule(ThresholdRule? rule, float? value)
    {
        if (value is not float v || float.IsNaN(v)) return Severity.Normal;
        if (rule is null || rule.Warn == rule.Crit) return Severity.Normal;
        if (rule.LowerIsWorse)
        {
            if (v <= rule.Crit) return Severity.Crit;
            if (v <= rule.Warn) return Severity.Warn;
            return Severity.Normal;
        }
        if (v >= rule.Crit) return Severity.Crit;
        if (v >= rule.Warn) return Severity.Warn;
        return Severity.Normal;
    }
}
