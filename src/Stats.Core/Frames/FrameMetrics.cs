using Stats.Core.Metrics;

namespace Stats.Core.Frames;

/// <summary>Metric identities for the PresentMon-backed frame-rate reader.</summary>
public static class FrameMetrics
{
    public const string IdPrefix = "fps.";
    public const string FpsId = "fps.avg";
    public const string LowId = "fps.low1";
    public const string FrameTimeId = "fps.frametime";
    public const string HardwareName = "Foreground app";

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        new MetricDefinition(FpsId, "FPS", MetricGroup.Game, HardwareName, "fps", "F0"),
        new MetricDefinition(LowId, "1% Low FPS", MetricGroup.Game, HardwareName, "fps", "F0"),
        new MetricDefinition(FrameTimeId, "Frame Time", MetricGroup.Game, HardwareName, "ms", "F1"),
    };

    public static bool IsFrameMetric(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal);
}
