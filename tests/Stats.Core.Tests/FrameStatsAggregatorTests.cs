using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class FrameStatsAggregatorTests
{
    private static readonly DateTime T0 = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan OneSec = TimeSpan.FromSeconds(1);

    /// <summary>Adds <paramref name="count"/> frames for pid evenly spread over the last second ending at <paramref name="end"/>.</summary>
    private static void AddBurst(FrameStatsAggregator a, int pid, int count, double frameTimeMs, DateTime end)
    {
        for (int i = count - 1; i >= 0; i--)
            a.Add(new FrameSample(pid, frameTimeMs), end - TimeSpan.FromMilliseconds(i * 1000.0 / count));
    }

    [Fact]
    public void Snapshot_UnknownPid_IsEmpty()
    {
        var a = new FrameStatsAggregator();
        Assert.Equal(FrameStats.Empty, a.Snapshot(99, T0, OneSec));
    }

    [Fact]
    public void Fps_IsFramesInWindowDividedBySeconds()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16.6, T0);
        var s = a.Snapshot(1, T0, OneSec);
        Assert.Equal(60f, s.Fps);
        Assert.Equal(16.6f, s.FrameTimeMs!.Value, 2);
    }

    [Fact]
    public void Fps_UsesWindowLength_NotFrameTimes()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 120, 8.3, T0);                     // 120 frames in the last second
        Assert.Equal(60f, a.Snapshot(1, T0, TimeSpan.FromSeconds(2)).Fps); // 120 / 2 s
    }

    [Fact]
    public void Fps_OnlyCountsFramesInsideWindow()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 50, 20, T0 - TimeSpan.FromSeconds(5)); // old
        AddBurst(a, 1, 30, 33.3, T0);                          // recent
        Assert.Equal(30f, a.Snapshot(1, T0, OneSec).Fps);
    }

    [Fact]
    public void FewerThanTenFramesInWindow_FpsAndFrameTimeNull()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 9, 100, T0);
        var s = a.Snapshot(1, T0, OneSec);
        Assert.Null(s.Fps);
        Assert.Null(s.FrameTimeMs);
        AddBurst(a, 2, 10, 100, T0);
        Assert.Equal(10f, a.Snapshot(2, T0, OneSec).Fps);
    }

    [Fact]
    public void OnePercentLow_NullUntilHundredFrames_ThenP99OfWholeBuffer()
    {
        var a = new FrameStatsAggregator();
        // 98 fast frames, then 2 slow ones → 100 frames; p99 index = ceil(0.99*100)-1 = 98 → the 40 ms sample.
        AddBurst(a, 1, 98, 10, T0 - TimeSpan.FromSeconds(1));
        Assert.Null(a.Snapshot(1, T0, OneSec).OnePercentLowFps); // 98 < 100
        a.Add(new FrameSample(1, 50), T0);
        a.Add(new FrameSample(1, 40), T0);
        // sorted: 98×10, 40, 50 → index ceil(99)-1 = 98 → 40 ms → 25 fps
        Assert.Equal(25f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void OnePercentLow_AtThousandFrames_UsesIndex989()
    {
        var a = new FrameStatsAggregator();
        for (int i = 0; i < 1000; i++)
            a.Add(new FrameSample(1, i < 990 ? 10 : 20), T0);   // 990×10ms, 10×20ms
        // ceil(0.99*1000)-1 = 989 → 10 ms → 100 fps (the slow 1% starts at index 990)
        Assert.Equal(100f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void RingBuffer_CapsAtCapacity_DroppingOldest()
    {
        var a = new FrameStatsAggregator(capacityPerPid: 100);
        for (int i = 0; i < 100; i++) a.Add(new FrameSample(1, 100), T0 - TimeSpan.FromSeconds(2)); // slow, old
        for (int i = 0; i < 100; i++) a.Add(new FrameSample(1, 10), T0);                             // fast, recent, evicts all slow
        Assert.Equal(100f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void Fps_FiveSecondWindow_HighFrameRate_NotTruncated()
    {
        var a = new FrameStatsAggregator();                 // default capacity must cover 5 s at high fps
        var window = TimeSpan.FromSeconds(5);
        for (int i = 1500 - 1; i >= 0; i--)
            a.Add(new FrameSample(1, 3.333), T0 - TimeSpan.FromMilliseconds(i * 5000.0 / 1500));
        Assert.Equal(300f, a.Snapshot(1, T0, window).Fps);  // 1500 frames / 5 s
    }

    [Fact]
    public void OnePercentLow_UsesNewestThousandFramesOnly()
    {
        var a = new FrameStatsAggregator();
        for (int i = 0; i < 100; i++) a.Add(new FrameSample(1, 100), T0);    // old, slow
        for (int i = 0; i < 1000; i++) a.Add(new FrameSample(1, 10), T0);    // newest 1000, fast
        // the 100 slow frames fall outside the 1000-frame low window → p99 = 10 ms → 100 fps
        Assert.Equal(100f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void StalePid_PrunedAfterTenSeconds()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        AddBurst(a, 2, 60, 16, T0 + TimeSpan.FromSeconds(11));
        Assert.Equal(2, a.TrackedProcessCount);
        a.Snapshot(2, T0 + TimeSpan.FromSeconds(11), OneSec);   // prune runs on Snapshot
        Assert.Equal(1, a.TrackedProcessCount);
        Assert.Equal(FrameStats.Empty, a.Snapshot(1, T0 + TimeSpan.FromSeconds(11), OneSec));
    }

    [Fact]
    public void Pids_AreIsolated()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        AddBurst(a, 2, 30, 33, T0);
        Assert.Equal(60f, a.Snapshot(1, T0, OneSec).Fps);
        Assert.Equal(30f, a.Snapshot(2, T0, OneSec).Fps);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        a.Clear();
        Assert.Equal(0, a.TrackedProcessCount);
        Assert.Equal(FrameStats.Empty, a.Snapshot(1, T0, OneSec));
    }

    [Fact]
    public async Task ConcurrentAddAndSnapshot_DoesNotThrow()
    {
        var a = new FrameStatsAggregator();
        using var cts = new CancellationTokenSource(300);
        var writer = Task.Run(() => { int i = 0; while (!cts.IsCancellationRequested) a.Add(new FrameSample(1 + (i++ % 3), 16), DateTime.UtcNow); });
        var reader = Task.Run(() => { while (!cts.IsCancellationRequested) { a.Snapshot(1, DateTime.UtcNow, OneSec); a.Snapshot(2, DateTime.UtcNow, OneSec); } });
        await Task.WhenAll(writer, reader);
    }
}
