namespace Stats.Core.Metrics;

/// <summary>Stable identity of one monitorable value. Id survives restarts and settings round-trips.</summary>
public sealed record MetricDefinition(
    string Id,
    string DisplayName,
    MetricGroup Group,
    string HardwareName,
    string Unit,
    string Format = "F0");
