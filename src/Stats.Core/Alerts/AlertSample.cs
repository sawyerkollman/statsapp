using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.Alerts;

/// <summary>One metric's current reading, already-computed severity, and the rule that produced it (override or
/// group rule; null if the metric has no rule at all) — everything <see cref="AlertEngine"/> needs for one tick,
/// without recomputing severity itself.</summary>
public sealed record AlertSample(MetricDefinition Definition, float? Value, Severity Severity, ThresholdRule? Rule);
