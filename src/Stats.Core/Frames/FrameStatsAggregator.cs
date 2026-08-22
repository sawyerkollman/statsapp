namespace Stats.Core.Frames;

/// <summary>
/// Thread-safe per-process store of recent frames. Producer thread calls Add; poller thread calls Snapshot.
/// The ring buffer holds up to 5000 frames per PID — enough to cover the longest poll window (5 s) even at
/// 1000 fps. FPS/frame time are computed over the caller's window; 1% low over the newest
/// <see cref="LowWindowFrames"/> frames, so its horizon does not grow with the buffer.
/// </summary>
public sealed class FrameStatsAggregator
{
    public const int MinFramesInWindow = 10;
    public const int MinFramesForLow = 100;
    /// <summary>The 1% low is the 99th percentile over at most this many of the newest frames.</summary>
    public const int LowWindowFrames = 1000;

    private readonly int _capacity;
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();
    private readonly Dictionary<int, Queue<(DateTime At, double Ms)>> _frames = new();
    private readonly Dictionary<int, DateTime> _lastSeen = new();

    public FrameStatsAggregator(int capacityPerPid = 5000, TimeSpan? staleAfter = null)
    {
        _capacity = capacityPerPid;
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(10);
    }

    public int TrackedProcessCount { get { lock (_gate) return _frames.Count; } }

    public void Add(FrameSample sample, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_frames.TryGetValue(sample.Pid, out var q))
            {
                q = new Queue<(DateTime, double)>();   // grows on demand; _capacity is only the cap
                _frames[sample.Pid] = q;
            }
            q.Enqueue((nowUtc, sample.FrameTimeMs));
            while (q.Count > _capacity) q.Dequeue();
            _lastSeen[sample.Pid] = nowUtc;
        }
    }

    public FrameStats Snapshot(int pid, DateTime nowUtc, TimeSpan window)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            if (!_frames.TryGetValue(pid, out var q) || q.Count == 0) return FrameStats.Empty;

            var cutoff = nowUtc - window;
            int inWindow = 0;
            double sumMs = 0;
            foreach (var (at, ms) in q)
            {
                if (at > cutoff && at <= nowUtc) { inWindow++; sumMs += ms; }
            }

            float? fps = null, frameTime = null;
            if (inWindow >= MinFramesInWindow && window.TotalSeconds > 0)
            {
                fps = (float)(inWindow / window.TotalSeconds);
                frameTime = (float)(sumMs / inWindow);
            }

            float? low = null;
            if (q.Count >= MinFramesForLow)
            {
                int lowCount = Math.Min(q.Count, LowWindowFrames);
                int skip = q.Count - lowCount;                      // the newest lowCount frames only
                var sorted = new double[lowCount];
                int i = 0;
                foreach (var (_, ms) in q)
                {
                    if (skip > 0) { skip--; continue; }
                    sorted[i++] = ms;
                }
                Array.Sort(sorted);
                int idx = (int)Math.Ceiling(0.99 * sorted.Length) - 1;
                double p99 = sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
                if (p99 > 0) low = (float)(1000.0 / p99);           // avoid Infinity on a zero frame time
            }
            return new FrameStats(fps, frameTime, low);
        }
    }

    public void Clear()
    {
        lock (_gate) { _frames.Clear(); _lastSeen.Clear(); }
    }

    private void Prune(DateTime nowUtc)
    {
        List<int>? stale = null;
        foreach (var (pid, seen) in _lastSeen)
            if (nowUtc - seen > _staleAfter) (stale ??= new()).Add(pid);
        if (stale is null) return;
        foreach (var pid in stale) { _frames.Remove(pid); _lastSeen.Remove(pid); }
    }
}
