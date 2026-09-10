using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.UiPreview.Fixtures;

/// <summary>Everything one named scenario (PREVIEW_HARNESS.md's Scenarios table) contributes to a preview
/// composition: discovered definitions, a deterministic tick history to apply to the MetricStore, the
/// dashboard/overlay selection, and any per-scenario settings/fan-fixture state. Never a reading of anyone's real
/// PC — every value here is an explicit, hand-picked fixture number.</summary>
public sealed record ScenarioFixture
{
    public required string Name { get; init; }
    public required IReadOnlyList<MetricDefinition> Definitions { get; init; }
    public required List<SensorSnapshot> Ticks { get; init; }

    public List<string> DashboardMetrics { get; init; } = new();
    public List<string> OverlayMetrics { get; init; } = new();
    public Dictionary<string, TilePref> TilePrefs { get; init; } = new();
    public Dictionary<string, float> MetricLimits { get; init; } = new();
    public Dictionary<string, ThresholdRule> ThresholdOverrides { get; init; } = new();
    public List<string> CollapsedGroups { get; init; } = new();

    public bool Degraded { get; init; }
    public SensorHealthState? Health { get; init; }
    public string? GameStatus { get; init; }

    public List<FanChannel> FanChannels { get; init; } = new();
    public Dictionary<string, FanChannelPref> FanChannelPrefs { get; init; } = new();
    public bool FanControlEnabled { get; init; }
    public List<FanProfile> FanProfiles { get; init; } = new();
    public string? ActiveFanProfile { get; init; }

    public string? AccentHex { get; init; }
}
