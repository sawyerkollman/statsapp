using Stats.Core.Fans;

namespace Stats.UiPreview.Fixtures;

/// <summary>Fake <see cref="IFanControlBackend"/>: every SetPercent/SetAuto is recorded to a
/// <see cref="CommandLog"/> instead of reaching real hardware. <see cref="FailingChannels"/> lets the
/// write-failed/three-strikes substate simulate a channel whose writes always throw, exactly like
/// tests/Stats.Core.Tests/FanControllerTests.cs's own FakeBackend.</summary>
public sealed class FakeFanControlBackend : IFanControlBackend
{
    private readonly CommandLog _log;

    public FakeFanControlBackend(CommandLog log) => _log = log;

    public List<FanChannel> Chans { get; } = new();
    public HashSet<string> FailingChannels { get; } = new();

    public IReadOnlyList<FanChannel> Channels => Chans;

    public void SetPercent(string channelId, float percent)
    {
        if (FailingChannels.Contains(channelId))
        {
            _log.Record($"fan.SetPercent {channelId}={percent:F0}% -> FAILED (simulated write failure)");
            throw new InvalidOperationException("simulated fan write failure");
        }
        _log.Record($"fan.SetPercent {channelId}={percent:F0}%");
    }

    public void SetAuto(string channelId) => _log.Record($"fan.SetAuto {channelId}");
}
