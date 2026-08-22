using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class SensorPollerTests
{
    private sealed class FakeReader : ISensorReader
    {
        public int ReadCount;
        public bool ThrowOnRead;
        public string Name => "Fake";
        public bool IsDegraded => false;
        public IReadOnlyList<MetricDefinition> Discover() => Array.Empty<MetricDefinition>();
        public SensorSnapshot Read()
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("boom");
            return new SensorSnapshot(new Dictionary<string, float?> { ["x"] = ReadCount }, DateTime.UtcNow);
        }
        public void Dispose() { }
    }

    [Fact]
    public void PollOnce_RaisesEventWithSnapshot()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader);
        SensorSnapshot? received = null;
        poller.SnapshotAvailable += s => received = s;
        poller.PollOnce();
        Assert.NotNull(received);
        Assert.Equal(1f, received!.Values["x"]);
    }

    [Fact]
    public async Task StartStop_PollsRepeatedly_ThenStops()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(30) };
        int events = 0;
        poller.SnapshotAvailable += _ => Interlocked.Increment(ref events);
        poller.Start();
        await Task.Delay(400);
        poller.Stop();
        int atStop = events;
        Assert.True(atStop >= 2, $"expected >=2 polls, got {atStop}");
        await Task.Delay(200);
        Assert.Equal(atStop, events); // no polls after Stop
    }

    [Fact]
    public async Task ReaderThrow_DoesNotKillLoop()
    {
        var reader = new FakeReader { ThrowOnRead = true };
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(20) };
        poller.Start();
        await Task.Delay(150);
        poller.Stop();
        Assert.True(reader.ReadCount >= 2, $"loop should survive exceptions, got {reader.ReadCount} reads");
    }
}
