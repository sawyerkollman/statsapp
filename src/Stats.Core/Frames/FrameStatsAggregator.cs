namespace Stats.Core.Frames;

/// <summary>
/// Thread-safe per-process store of recent frames. Producer thread calls Add; poller thread calls Snapshot.
/// FPS/frame time are computed over the caller's window; 1% low over the whole ring buffer.
/// </summary>
public sealed class FrameStatsAggregator
{
    public const int MinFramesInWindow = 10;
    public const int MinFramesForLow = 100;

    private readonly int _capacity;
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();
    private readonly Dictionary<int, Queue<(DateTime At, double Ms)>> _frames = new();
    private readonly Dictionary<int, DateTime> _lastSeen = new();

    public FrameStatsAggregator(int capacityPerPid = 1000, TimeSpan? staleAfter = null)
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
                q = new Queue<(DateTime, double)>(_capacity);
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
                var sorted = new double[q.Count];
                int i = 0;
                foreach (var (_, ms) in q) sorted[i++] = ms;
                Array.Sort(sorted);
                int idx = (int)Math.Ceiling(0.99 * sorted.Length) - 1;
                double p99 = sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
                if (p99 > 0) low = (float)(1000.0 / p99);
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
