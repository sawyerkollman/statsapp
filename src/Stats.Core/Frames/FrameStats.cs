namespace Stats.Core.Frames;

/// <summary>Aggregate frame statistics for one process over the last poll window. Null = not enough frames.</summary>
public readonly record struct FrameStats(float? Fps, float? FrameTimeMs, float? OnePercentLowFps)
{
    public static readonly FrameStats Empty = new(null, null, null);
}
