using Stats.Core.Settings;

namespace Stats.Core.Fans;

public enum FanChannelStatus { Idle, Active, WaitingForSource, SourceUnavailable, WriteFailed }

/// <summary>Read-only snapshot of one channel for the UI.</summary>
public sealed record FanChannelView(
    string Id, string Name, string Device, FanMode Mode,
    float? Rpm, float? Percent, float? TargetPercent, float? SourceTemp,
    FanChannelStatus Status, float MinPercent, float MaxPercent,
    string? SourceMetricId, float ManualPercent, IReadOnlyList<FanPoint> Points);
