namespace Stats.Core.Fans;

/// <summary>One controllable fan output. Id is the LHM control identifier (stable across runs).</summary>
public sealed record FanChannel(
    string Id, string Name, string Device,
    string? RpmMetricId, string? PercentMetricId,
    float MinPercent, float MaxPercent);

/// <summary>Writes fan speeds. All members are poll-thread only (LHM is not thread-safe).</summary>
public interface IFanControlBackend
{
    IReadOnlyList<FanChannel> Channels { get; }
    /// <exception cref="KeyNotFoundException">Unknown channel id.</exception>
    void SetPercent(string channelId, float percent);
    void SetAuto(string channelId);
}
