using System.Diagnostics;

namespace Stats.Core.Sensors;

/// <summary>Polls an ISensorReader on a background task. Events fire on the background thread — UI must marshal.</summary>
public sealed class SensorPoller : IDisposable
{
    private readonly ISensorReader _reader;
    private readonly Func<DateTime> _localClock;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private int _consecutiveFailures;
    private DateTime _firstFailureLocalTime;

    /// <param name="localClock">Supplies the local time stamped on a newly-started failure episode; defaults to
    /// <see cref="DateTime.Now"/>. Injectable so tests can assert the episode's start time stays fixed.</param>
    public SensorPoller(ISensorReader reader, Func<DateTime>? localClock = null)
    {
        _reader = reader;
        _localClock = localClock ?? (() => DateTime.Now);
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public event Action<SensorSnapshot>? SnapshotAvailable;

    /// <summary>Fires on the poll thread whenever the failure episode changes: a new or continuing failure, or
    /// recovery on the next fully healthy read. Never fires on an already-healthy tick. Each subscriber runs in
    /// its own try/catch, mirroring <see cref="SnapshotAvailable"/> — a throwing handler cannot stop the others
    /// or starve/kill the poll loop.</summary>
    public event Action<SensorHealthState>? HealthChanged;

    /// <summary>Current failure-episode state; updated on the poll thread immediately before HealthChanged fires.</summary>
    public SensorHealthState Health { get; private set; } = SensorHealthState.Healthy;

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
            RecordFailure(new[] { _reader.Name }, FirstLine(ex.Message));
            return null;
        }

        if (snapshot.FailedBackends.Count > 0)
        {
            var backends = snapshot.FailedBackends.Select(f => f.BackendName).ToArray();
            RecordFailure(backends, snapshot.FailedBackends[0].ErrorFirstLine);
        }
        else if (!Health.IsHealthy)
        {
            RecordRecovery();
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

    private void RecordFailure(IReadOnlyList<string> backends, string errorFirstLine)
    {
        if (_consecutiveFailures == 0) _firstFailureLocalTime = _localClock(); // stable for the rest of the episode
        _consecutiveFailures++;
        Health = new SensorHealthState(false, _consecutiveFailures, _firstFailureLocalTime, errorFirstLine, backends);
        RaiseHealthChanged();
    }

    private void RecordRecovery()
    {
        _consecutiveFailures = 0;
        Health = SensorHealthState.Healthy;
        RaiseHealthChanged();
    }

    private void RaiseHealthChanged()
    {
        var state = Health;
        foreach (var d in HealthChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<SensorHealthState>)d)(state); }
            catch (Exception ex) { Trace.WriteLine($"[Stats.SensorPoller] health subscriber threw: {ex}"); }
        }
    }

    private static string FirstLine(string text)
    {
        int i = text.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? text : text[..i];
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
