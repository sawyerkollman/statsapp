namespace Stats.Core.Sensors;

/// <summary>One poll tick: metric id → value (null = sensor produced nothing this tick).</summary>
public sealed record SensorSnapshot(IReadOnlyDictionary<string, float?> Values, DateTime TimestampUtc);
