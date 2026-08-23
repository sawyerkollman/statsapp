using System.Diagnostics;

namespace Stats.Core.Sensors;

/// <summary>Polls an ISensorReader on a background task. Event fires on the background thread — UI must marshal.</summary>
public sealed class SensorPoller : IDisposable
{
    private readonly ISensorReader _reader;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SensorPoller(ISensorReader reader) => _reader = reader;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public event Action<SensorSnapshot>? SnapshotAvailable;

    public SensorSnapshot? PollOnce()
    {
        SensorSnapshot snapshot;
        try
        {
            snapshot = _reader.Read();
        }
        catch (Exception ex)
        {
            // transient sensor hiccup; next tick retries
            Trace.WriteLine($"[Stats.SensorPoller] {_reader.Name} Read failed: {ex}");
            return null;
        }

        // Each subscriber gets its own try/catch: a throwing fan-control tick must not stop the UI refresh
        // (or vice versa), and neither may abort the poll loop.
        foreach (var d in SnapshotAvailable?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<SensorSnapshot>)d)(snapshot); }
            catch (Exception ex) { Trace.WriteLine($"[Stats.SensorPoller] snapshot subscriber threw: {ex}"); }
        }
        return snapshot;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                PollOnce();
                try { await Task.Delay(Interval, ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    /// <summary>Cancels the loop and waits up to 2 s for it to finish.</summary>
    /// <returns>true if the loop task actually completed (nothing is touching the reader any more);
    /// false if it was still running when the wait expired — the caller must not dispose the reader then.</returns>
    public bool Stop()
    {
        _cts?.Cancel();
        bool stopped = true;
        try { stopped = _loop?.Wait(TimeSpan.FromSeconds(2)) ?? true; } catch (AggregateException) { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        return stopped;
    }

    public void Dispose() => Stop();
}
