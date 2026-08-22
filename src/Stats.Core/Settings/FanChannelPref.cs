using Stats.Core.Fans;

namespace Stats.Core.Settings;

public enum FanMode { Auto, Manual, Curve }

/// <summary>One curve vertex: at TempC the fan runs at Percent.</summary>
public sealed record FanPoint(float TempC, float Percent);

/// <summary>Desired state for one controllable fan channel, keyed by the LHM control identifier.</summary>
public sealed class FanChannelPref
{
    public FanMode Mode { get; set; } = FanMode.Auto;
    public float ManualPercent { get; set; } = 50f;
    /// <summary>Metric id (unit °C) that drives the curve; null = no source chosen yet.</summary>
    public string? SourceMetricId { get; set; }
    public List<FanPoint> Points { get; set; } = FanCurve.DefaultPoints.ToList();
    /// <summary>Friendly display-name override; null/blank = hardware name.</summary>
    public string? Name { get; set; }
}
