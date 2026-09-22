namespace Stats.Core.Recording;

/// <summary>Exact whole-session raw-frame statistics. A hitch is strictly more than 50 ms; it is not causal evidence.</summary>
public sealed record SessionFrameSummary(long Count, double MeanFrameTimeMs, double MaxFrameTimeMs,
    double P50FrameTimeMs, double P95FrameTimeMs, double P99FrameTimeMs, long HitchFrameCount);
