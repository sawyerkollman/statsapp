namespace Stats.Core.Settings;

public sealed class AppSettings
{
    public double PollIntervalSeconds { get; set; } = 1.0;
    public List<string> DashboardMetrics { get; set; } = new();
    public List<string> OverlayMetrics { get; set; } = new();
    /// <summary>Optional user-entered limits (e.g. PBO PPT watts) keyed by metric id; tile shows "% of limit" when set.</summary>
    public Dictionary<string, float> MetricLimits { get; set; } = new();
    public double OverlayOpacity { get; set; } = 0.85;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}
