namespace Stats.Core.Sensors;

/// <summary>One poll tick: metric id → value (null = sensor produced nothing this tick).</summary>
public sealed record SensorSnapshot(
    IReadOnlyDictionary<string, float?> Values,
    DateTime TimestampUtc,
    IReadOnlyList<SensorBackendFailure>? FailedBackends = null)
{
    /// <summary>Child backends (e.g. inside a CompositeSensorReader) that failed to read this tick, alongside
    /// the ones that still merged in successfully. Empty when every backend succeeded, or the reader isn't a
    /// composite of several backends.</summary>
    public IReadOnlyList<SensorBackendFailure> FailedBackends { get; init; } = FailedBackends ?? Array.Empty<SensorBackendFailure>();
}

/// <summary>One child backend's failed read for a single CompositeSensorReader tick.</summary>
public sealed record SensorBackendFailure(string BackendName, string ErrorFirstLine);
