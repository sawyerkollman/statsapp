using Stats.Core.Metrics;

namespace Stats.Core.Settings;

/// <summary>Warn/Crit thresholds for every metric matching (Group, Unit). For overrides, Group/Unit are ignored.</summary>
public sealed class ThresholdRule
{
    public MetricGroup Group { get; set; }
    public string Unit { get; set; } = "";
    public float Warn { get; set; }
    public float Crit { get; set; }
}

public enum OverlayOrientation { Horizontal, Vertical }
