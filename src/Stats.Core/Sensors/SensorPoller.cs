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
        try
        {
            var snapshot = _reader.Read();
            SnapshotAvailable?.Invoke(snapshot);
            return snapshot;
        }
        catch
        {
            return null; // transient sensor hiccup; next tick retries
        }
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

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();
}
