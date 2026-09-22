using System.Diagnostics;

namespace Stats.Core.Processes;

public sealed class ProcessSampler : IDisposable
{
    public const double MinIntervalSeconds = 2;
    private readonly IProcessSource _source;
    private readonly ProcessCpuTracker _tracker;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _active;
    private int _generation;
    private int _resetRequested;
    private bool _failing;

    public ProcessSampler(IProcessSource source, int? processorCount = null)
    {
        _source = source;
        _tracker = new(processorCount ?? Environment.ProcessorCount);
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(MinIntervalSeconds);
    public bool IsRunning => _loop is not null;
    public bool IsActive => _active;
    public event Action<ProcessListSnapshot>? SampleAvailable;
    public static TimeSpan IntervalFor(double pollIntervalSeconds) => TimeSpan.FromSeconds(Math.Max(MinIntervalSeconds, pollIntervalSeconds));

    public ProcessListSnapshot SampleOnce() => SampleOnce(null);

    private ProcessListSnapshot SampleOnce(int? generation)
    {
        ProcessListSnapshot result;
        try
        {
            var source = _source.Sample();
            result = new(ProcessGrouping.Group(_tracker.Apply(source), source.GpuPercentByPid), source.GpuAvailable, source.TimestampUtc);
            if (_failing) { Trace.WriteLine("[Stats.ProcessSampler] process reads recovered"); _failing = false; }
        }
        catch (Exception ex)
        {
            if (!_failing) { Trace.WriteLine($"[Stats.ProcessSampler] process read failed: {ex}"); _failing = true; }
            result = ProcessListSnapshot.Unavailable(FirstLine(ex.Message), DateTime.UtcNow);
        }
        if (generation is not null && (generation != Volatile.Read(ref _generation) || !_active)) return result;
        foreach (var subscriber in SampleAvailable?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { ((Action<ProcessListSnapshot>)subscriber)(result); }
            catch (Exception ex) { Trace.WriteLine($"[Stats.ProcessSampler] subscriber threw: {ex}"); }
        }
        return result;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _active = true;
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    public void Pause()
    {
        _active = false;
        Interlocked.Increment(ref _generation);
    }

    public void Resume()
    {
        if (_active) return;
        Interlocked.Exchange(ref _resetRequested, 1);
        Interlocked.Increment(ref _generation);
        _active = true;
        if (_loop is null) { Start(); return; }
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    public bool Stop()
    {
        _active = false;
        _cts?.Cancel();
        if (_wake.CurrentCount == 0) _wake.Release();
        try
        {
            if (!(_loop?.Wait(TimeSpan.FromSeconds(2)) ?? true)) return false;
        }
        catch (AggregateException) { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        return true;
    }

    public void Dispose()
    {
        if (Stop()) { _source.Dispose(); _wake.Dispose(); }
        else Trace.WriteLine("[Stats.ProcessSampler] loop did not stop; source was not disposed");
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_active)
            {
                try { await _wake.WaitAsync(ct); } catch (OperationCanceledException) { break; }
                continue;
            }
            if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
            {
                _source.Reset();
                _tracker.Reset();
            }
            if (!_active) continue;
            SampleOnce(Volatile.Read(ref _generation));
            try { await _wake.WaitAsync(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOfAny(['\r', '\n']);
        return i < 0 ? text : text[..i];
    }
}
