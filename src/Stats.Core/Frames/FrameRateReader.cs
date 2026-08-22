using System.Diagnostics;
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Frames;

/// <summary>
/// ISensorReader for the foreground process's frame rate, fed by a PresentMon child process that runs
/// only while <see cref="SetActive"/> is true. Read() is called by SensorPoller on its thread; SetActive
/// from the UI thread; LineReceived/Exited from the source's threads — all state is guarded by _gate.
/// </summary>
public sealed class FrameRateReader : ISensorReader
{
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };

    private readonly string? _exePath;
    private readonly Func<IFrameSource> _sourceFactory;
    private readonly Func<int?> _foregroundPid;
    private readonly Func<DateTime> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();

    private IFrameSource? _source;
    private PresentMonCsvParser _parser = new();
    private readonly FrameStatsAggregator _aggregator = new();
    private CancellationTokenSource? _restartCts;
    private int _failures;           // consecutive exits without frames since last start
    private bool _sawFrames;         // frames received since last (re)start → resets backoff
    private int _generation;         // bumped on every SetActive so stale callbacks are ignored

    public FrameRateReader(string? exePath, Func<IFrameSource> sourceFactory, Func<int?> foregroundPid,
        Func<DateTime>? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _exePath = exePath;
        _sourceFactory = sourceFactory;
        _foregroundPid = foregroundPid;
        _clock = clock ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
        IsAvailable = exePath is not null;
        if (exePath is null) StatusMessage = "PresentMon.exe not found; FPS metrics unavailable.";
    }

    /// <summary>Production wiring: bundled exe, real child process, real foreground window.</summary>
    public static FrameRateReader CreateDefault()
    {
        var exe = PresentMonLocator.Find();
        return new FrameRateReader(exe, () => new PresentMonProcess(exe!), ForegroundProcess.CurrentPid);
    }

    /// <summary>True when any selected metric (dashboard ∪ overlay) is a frame metric.</summary>
    public static bool ShouldBeActive(IEnumerable<string> dashboardIds, IEnumerable<string> overlayIds) =>
        dashboardIds.Any(FrameMetrics.IsFrameMetric) || overlayIds.Any(FrameMetrics.IsFrameMetric);

    public string Name => "PresentMon";
    public bool IsDegraded => false;
    /// <summary>Poll interval; FPS = frames in the last Window ÷ Window seconds.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);
    public bool IsActive { get; private set; }
    /// <summary>False when the exe is missing, tracing was denied, the CSV was unreadable, or restarts were exhausted.</summary>
    public bool IsAvailable { get; private set; }
    public string? StatusMessage { get; private set; }

    public IReadOnlyList<MetricDefinition> Discover() =>
        _exePath is null ? Array.Empty<MetricDefinition>() : FrameMetrics.Definitions;

    public SensorSnapshot Read()
    {
        FrameStats stats = FrameStats.Empty;
        lock (_gate)
        {
            if (IsActive && IsAvailable && _foregroundPid() is int pid)
                stats = _aggregator.Snapshot(pid, _clock(), Window);
        }
        return new SensorSnapshot(new Dictionary<string, float?>
        {
            [FrameMetrics.FpsId] = stats.Fps,
            [FrameMetrics.LowId] = stats.OnePercentLowFps,
            [FrameMetrics.FrameTimeId] = stats.FrameTimeMs,
        }, _clock());
    }

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (active == IsActive) return;
            IsActive = active;
            _generation++;
            CancelPendingRestart();
            if (active)
            {
                if (_exePath is null) return;
                IsAvailable = true;
                StatusMessage = null;
                _failures = 0;
                StartSourceLocked();
            }
            else
            {
                StopSourceLocked();
                _aggregator.Clear();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            IsActive = false;
            _generation++;
            CancelPendingRestart();
            StopSourceLocked();
        }
    }

    // ---- internals (all called with _gate held unless noted) ----

    private void StartSourceLocked()
    {
        _parser = new PresentMonCsvParser();
        _sawFrames = false;
        var src = _sourceFactory();
        int gen = _generation;
        src.LineReceived += line => OnLine(src, gen, line);
        src.Exited += (code, stderr) => OnExited(src, gen, code, stderr);
        _source = src;
        try
        {
            src.Start();
        }
        catch (Exception ex)
        {
            _source = null;
            try { src.Dispose(); } catch { /* best effort */ }
            MarkUnavailable($"PresentMon could not be started: {ex.Message}");
        }
    }

    private void StopSourceLocked()
    {
        var src = _source;
        _source = null;
        if (src is null) return;
        try { src.Dispose(); } catch { /* best effort */ }
    }

    private void CancelPendingRestart()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;
    }

    private void MarkUnavailable(string message)
    {
        IsAvailable = false;
        StatusMessage = message;
        Trace.WriteLine("[Stats.FrameRateReader] " + message);
    }

    private void OnLine(IFrameSource src, int gen, string line)
    {
        FrameSample? sample;
        lock (_gate)
        {
            if (gen != _generation || !ReferenceEquals(src, _source)) return;
            try
            {
                sample = _parser.Parse(line);
            }
            catch (PresentMonFormatException ex)
            {
                MarkUnavailable("PresentMon CSV header not understood: " + ex.Message);
                StopSourceLocked();
                return;
            }
            if (sample is FrameSample s)
            {
                _sawFrames = true;
                _aggregator.Add(s, _clock());
            }
        }
    }

    private void OnExited(IFrameSource src, int gen, int exitCode, string stderrTail)
    {
        lock (_gate)
        {
            if (gen != _generation || !ReferenceEquals(src, _source)) return;
            _source = null;
            try { src.Dispose(); } catch { }

            bool denied = exitCode == 6 || stderrTail.Contains("access denied", StringComparison.OrdinalIgnoreCase);
            if (denied)
            {
                MarkUnavailable("PresentMon: access denied starting the ETW trace session (exit " + exitCode + "). " +
                                "Launch Stats from the Start menu or a non-Store terminal; processes with MSIX package identity cannot trace. " +
                                stderrTail);
                return;
            }

            if (_sawFrames) _failures = 0;
            if (_failures >= Backoff.Length)
            {
                MarkUnavailable($"PresentMon exited repeatedly (last exit {exitCode}); gave up until FPS metrics are re-selected. {stderrTail}");
                return;
            }
            var wait = Backoff[_failures++];
            Trace.WriteLine($"[Stats.FrameRateReader] PresentMon exited ({exitCode}); restarting in {wait.TotalSeconds:F0}s. {stderrTail}");
            ScheduleRestartLocked(wait, gen);
        }
    }

    private void ScheduleRestartLocked(TimeSpan wait, int gen)
    {
        CancelPendingRestart();
        var cts = new CancellationTokenSource();
        _restartCts = cts;
        var token = cts.Token;
        _ = _delay(wait, token).ContinueWith(t =>
        {
            if (t.IsCanceled || token.IsCancellationRequested) return;
            lock (_gate)
            {
                if (gen != _generation || !IsActive || !IsAvailable) return;
                if (ReferenceEquals(_restartCts, cts)) { _restartCts = null; cts.Dispose(); }
                StartSourceLocked();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
}
