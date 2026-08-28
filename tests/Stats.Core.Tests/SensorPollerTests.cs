using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class SensorPollerTests
{
    private sealed class FakeReader : ISensorReader
    {
        public int ReadCount;
        public bool ThrowOnRead;
        public string ThrowMessage = "boom";
        /// <summary>Set to simulate a composite child reporting a partial failure while Read() still succeeds.</summary>
        public IReadOnlyList<SensorBackendFailure> FailedBackends = Array.Empty<SensorBackendFailure>();
        public string Name => "Fake";
        public bool IsDegraded => false;
        public IReadOnlyList<MetricDefinition> Discover() => Array.Empty<MetricDefinition>();
        public SensorSnapshot Read()
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException(ThrowMessage);
            return new SensorSnapshot(new Dictionary<string, float?> { ["x"] = ReadCount }, DateTime.UtcNow, FailedBackends);
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

    // ---- runtime sensor health ----

    [Fact]
    public void Health_StartsHealthy_NoEventOnHealthyPolls()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader);
        int healthEvents = 0;
        poller.HealthChanged += _ => healthEvents++;
        Assert.True(poller.Health.IsHealthy);
        poller.PollOnce();
        poller.PollOnce();
        Assert.Equal(0, healthEvents); // an already-healthy tick is a no-op, not a repeated event
        Assert.Equal(0, poller.Health.ConsecutiveFailures);
    }

    [Fact]
    public void Health_TopLevelThrow_CountsConsecutiveFailures_AndAttributesReaderName()
    {
        var reader = new FakeReader { ThrowOnRead = true, ThrowMessage = "boom\nsecond line" };
        using var poller = new SensorPoller(reader);
        var seen = new List<SensorHealthState>();
        poller.HealthChanged += s => seen.Add(s);

        poller.PollOnce();
        poller.PollOnce();
        poller.PollOnce();

        Assert.Equal(3, seen.Count);
        Assert.Equal(new[] { 1, 2, 3 }, seen.Select(s => s.ConsecutiveFailures));
        Assert.All(seen, s => Assert.False(s.IsHealthy));
        Assert.All(seen, s => Assert.Equal(new[] { "Fake" }, s.FailingBackends));
        Assert.All(seen, s => Assert.Equal("boom", s.LatestErrorFirstLine)); // first line only, no stack/second line
        Assert.Equal(3, poller.Health.ConsecutiveFailures);
    }

    [Fact]
    public void Health_FirstFailureLocalTime_StableThroughEpisode_ThenResetsForNextEpisode()
    {
        var reader = new FakeReader { ThrowOnRead = true };
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        using var poller = new SensorPoller(reader, () => now);

        now = now.AddMinutes(1);
        poller.PollOnce(); // episode starts, first-failure time = 10:01
        var firstFailureTime = poller.Health.FirstFailureLocalTime;
        Assert.Equal(new DateTime(2026, 1, 1, 10, 1, 0), firstFailureTime);

        now = now.AddMinutes(1);
        poller.PollOnce(); // still the same episode
        now = now.AddMinutes(1);
        poller.PollOnce();
        Assert.Equal(firstFailureTime, poller.Health.FirstFailureLocalTime); // stable through the episode
        Assert.Equal(3, poller.Health.ConsecutiveFailures);

        reader.ThrowOnRead = false;
        poller.PollOnce(); // fully healthy read resets the episode
        Assert.True(poller.Health.IsHealthy);

        reader.ThrowOnRead = true;
        now = now.AddMinutes(10);
        poller.PollOnce(); // a new episode starts with a new first-failure time
        Assert.Equal(now, poller.Health.FirstFailureLocalTime);
        Assert.NotEqual(firstFailureTime, poller.Health.FirstFailureLocalTime);
    }

    [Fact]
    public void Health_PartialCompositeFailure_CountsAsUnhealthy_ButSnapshotStillDelivered()
    {
        var reader = new FakeReader
        {
            FailedBackends = new[] { new SensorBackendFailure("PresentMon", "access denied") },
        };
        using var poller = new SensorPoller(reader);
        SensorSnapshot? delivered = null;
        poller.SnapshotAvailable += s => delivered = s;
        var health = poller.PollOnce();

        Assert.NotNull(delivered); // partial healthy values still flow to subscribers
        Assert.False(poller.Health.IsHealthy);
        Assert.Equal(1, poller.Health.ConsecutiveFailures);
        Assert.Equal(new[] { "PresentMon" }, poller.Health.FailingBackends);
        Assert.Equal("access denied", poller.Health.LatestErrorFirstLine);
    }

    [Fact]
    public void Health_ResetsOnNextFullyHealthyRead()
    {
        var reader = new FakeReader { ThrowOnRead = true };
        using var poller = new SensorPoller(reader);
        poller.PollOnce();
        poller.PollOnce();
        Assert.Equal(2, poller.Health.ConsecutiveFailures);

        reader.ThrowOnRead = false;
        var recovered = new List<SensorHealthState>();
        poller.HealthChanged += s => recovered.Add(s);
        poller.PollOnce();

        Assert.True(poller.Health.IsHealthy);
        Assert.Equal(0, poller.Health.ConsecutiveFailures);
        Assert.Single(recovered);
        Assert.True(recovered[0].IsHealthy);
    }

    [Fact]
    public void HealthChanged_OneSubscriberThrowing_OthersStillInvoked_LoopSurvives()
    {
        var reader = new FakeReader { ThrowOnRead = true };
        using var poller = new SensorPoller(reader);
        bool before = false, after = false;
        poller.HealthChanged += _ => before = true;
        poller.HealthChanged += _ => throw new InvalidOperationException("bad health subscriber");
        poller.HealthChanged += _ => after = true;

        poller.PollOnce();

        Assert.True(before);
        Assert.True(after); // the throwing subscriber did not starve the ones after it
        Assert.Equal(1, poller.Health.ConsecutiveFailures); // state updates even though a subscriber faulted
    }
}
