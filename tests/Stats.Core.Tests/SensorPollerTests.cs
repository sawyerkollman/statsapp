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
    public void OneSubscriberThrowing_OthersStillInvoked()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader);
        bool before = false, after = false;
        poller.SnapshotAvailable += _ => before = true;
        poller.SnapshotAvailable += _ => throw new InvalidOperationException("bad subscriber");
        poller.SnapshotAvailable += _ => after = true;
        var snap = poller.PollOnce();
        Assert.NotNull(snap);           // the read succeeded, so the snapshot is still returned
        Assert.True(before);
        Assert.True(after);             // the throwing subscriber did not starve the ones after it
    }

    [Fact]
    public async Task Stop_ReturnsTrueWhenLoopJoined()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(20) };
        Assert.True(poller.Stop());     // never started: nothing to join
        poller.Start();
        await Task.Delay(60);
        Assert.True(poller.Stop());     // loop task actually completed within the wait
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

    [Fact]
    public async Task ChangingInterval_WhileRunning_AffectsSubsequentWaits()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(20) };
        int events = 0;
        poller.SnapshotAvailable += _ => Interlocked.Increment(ref events);
        poller.Start();
        await Task.Delay(300);
        int fastCount = events;
        Assert.True(fastCount >= 3, $"expected several fast polls before the change, got {fastCount}");

        poller.Interval = TimeSpan.FromSeconds(10);
        await Task.Delay(300);
        poller.Stop();
        int slowCount = events;

        Assert.True(slowCount - fastCount <= 1,
            $"expected the longer Interval to suppress further polls, got {slowCount - fastCount} more (fast={fastCount}, slow={slowCount})");
    }
}
