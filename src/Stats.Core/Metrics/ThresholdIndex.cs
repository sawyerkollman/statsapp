using Stats.Core.Settings;

namespace Stats.Core.Metrics;

/// <summary>A <c>(Group, Unit)</c>-keyed snapshot of <see cref="AppSettings.ThresholdRules"/> plus
/// <see cref="AppSettings.ThresholdOverrides"/>, built once per refresh batch so many metrics can resolve their
/// governing rule in O(1) instead of each doing its own <c>ThresholdRules.FirstOrDefault(...)</c> scan (see v1.8
/// §10 "Cheap extras"). Pure and immutable once built — <see cref="RuleFor"/>/<see cref="Evaluate"/> produce
/// results identical to <see cref="ThresholdEvaluator.RuleFor"/>/<see cref="ThresholdEvaluator.Evaluate"/> for the
/// same settings, since both route through the same first-match-wins rule and the same comparison
/// (<see cref="ThresholdEvaluator.EvaluateRule"/>).</summary>
public sealed class ThresholdIndex
{
    private readonly Dictionary<(MetricGroup Group, string Unit), ThresholdRule> _byGroupUnit;
    private readonly Dictionary<string, ThresholdRule> _overrides;

    private ThresholdIndex(Dictionary<(MetricGroup, string), ThresholdRule> byGroupUnit, Dictionary<string, ThresholdRule> overrides)
    {
        _byGroupUnit = byGroupUnit;
        _overrides = overrides;
    }

    /// <summary>Snapshots <paramref name="settings"/>' current rules/overrides. Call again (e.g. once per
    /// <c>RefreshAll</c>/tick) after anything that could have changed them — the index itself never observes
    /// later mutations.</summary>
    public static ThresholdIndex Build(AppSettings settings)
    {
        var byGroupUnit = new Dictionary<(MetricGroup, string), ThresholdRule>();
        foreach (var rule in settings.ThresholdRules)
        {
            var key = (rule.Group, rule.Unit);
            // First (Group, Unit) match wins, exactly like ThresholdEvaluator.RuleFor's FirstOrDefault.
            if (!byGroupUnit.ContainsKey(key)) byGroupUnit[key] = rule;
        }
        // Copied rather than referenced so a caller mutating settings.ThresholdOverrides after Build() can never
        // change what an already-built index resolves.
        var overrides = new Dictionary<string, ThresholdRule>(settings.ThresholdOverrides);
        return new ThresholdIndex(byGroupUnit, overrides);
    }

    /// <summary>Per-metric override → first (Group, Unit) rule → null. Same precedence as
    /// <see cref="ThresholdEvaluator.RuleFor"/>.</summary>
    public ThresholdRule? RuleFor(MetricDefinition def) =>
        _overrides.TryGetValue(def.Id, out var o) ? o
            : _byGroupUnit.TryGetValue((def.Group, def.Unit), out var r) ? r : null;

    /// <summary>Same result as <see cref="ThresholdEvaluator.Evaluate"/> for a settings snapshot equal to the one
    /// this index was built from.</summary>
    public Severity Evaluate(MetricDefinition def, float? value) => ThresholdEvaluator.EvaluateRule(RuleFor(def), value);
}
