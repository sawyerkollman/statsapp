using System.Text.Json;
using System.Threading.Channels;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Recording;

/// <summary>Explicit UI-thread commands; only owned samples cross to the file writer.</summary>
public sealed class SessionRecorder : IDisposable
{
    private abstract record Entry;
    private sealed record Sample(DateTime TimestampUtc, float?[] Values) : Entry;
    private sealed record Frame(RecordedFrame Value) : Entry;
    private readonly string _directory;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, TextWriter> _openWriter;
    private readonly object _gate = new();
    private Channel<Entry>? _channel;
    private Task? _writer;
    private string[] _ids = [];
    private DateTime _lastTimestampUtc;
    private DateTime _startedUtc;
    private bool _hasSample;
    private volatile bool _includeFrames;
    private volatile bool _stopping;
    private volatile bool _isRecording;
    private volatile string? _error;

    public SessionRecorder(string directory, Func<DateTime>? clock = null, Func<string, TextWriter>? openWriter = null)
    {
        _directory = directory; _clock = clock ?? (() => DateTime.UtcNow);
        _openWriter = openWriter ?? (path => new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)));
    }
    public bool IsRecording => _isRecording;
    public string? FilePath { get; private set; }
    public string? Error => _error;

    public void Start(IReadOnlyList<MetricDefinition> definitions, DateTime startedUtc, bool includeFrames = false, string? gameName = null)
    {
        lock (_gate) StartLocked(definitions, startedUtc, includeFrames, gameName);
    }

    private void StartLocked(IReadOnlyList<MetricDefinition> definitions, DateTime startedUtc, bool includeFrames, string? gameName)
    {
        if (_isRecording || _writer is { IsCompleted: false })
            throw new InvalidOperationException("Previous recording is still stopping.");
        var metrics = definitions.ToArray();
        SessionFile.ValidateDefinitions(metrics);
        startedUtc = startedUtc.ToUniversalTime();
        _error = null;
        FilePath = null;
        TextWriter? output = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{startedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.stats-session.jsonl");
            output = _openWriter(path);
            gameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
            if (gameName?.Length > 1024) throw new InvalidDataException("Game name is too long.");
            var version = includeFrames || gameName is not null ? 2 : 1;
            output.WriteLine(version == 1
                ? JsonSerializer.Serialize(new { type = "header", version, startedUtc, metrics })
                : JsonSerializer.Serialize(new { type = "header", version, startedUtc, metrics, gameName, includeFrames }));
            output.Flush();
            var channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(includeFrames ? 8192 : 128)
            {
                SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            });
            _ids = metrics.Select(metric => metric.Id).ToArray();
            _lastTimestampUtc = startedUtc;
            _startedUtc = startedUtc;
            _hasSample = false;
            _channel = channel;
            _includeFrames = includeFrames;
            _stopping = false;
            FilePath = path;
            _isRecording = true;
            var writer = output;
            _writer = Task.Run(() => WriteAsync(channel, writer, startedUtc));
        }
        catch (Exception ex)
        {
            output?.Dispose();
            _error = ex.Message;
            _isRecording = false;
            _includeFrames = false;
            throw;
        }
    }

    public void Record(SensorSnapshot snapshot)
    {
        lock (_gate) RecordLocked(snapshot);
    }

    private void RecordLocked(SensorSnapshot snapshot)
    {
        if (!_isRecording || _channel is null) return;
        var at = snapshot.TimestampUtc.ToUniversalTime();
        if (at < _lastTimestampUtc)
        {
            // A Dispatcher snapshot already queued before Start belongs to the preceding session.
            if (!_hasSample) return;
            Fail("Recording stopped because the sample clock moved backwards.");
            return;
        }
        var values = _ids.Select(id => snapshot.Values.TryGetValue(id, out var value) &&
            value is float finite && float.IsFinite(finite) ? value : null).ToArray();
        if (_channel.Writer.TryWrite(new Sample(at, values)))
        {
            _lastTimestampUtc = at;
            _hasSample = true;
        }
        else if (!_stopping)
            Fail("Recording stopped because the file writer could not keep up. The partial recording is available.");
    }

    /// <summary>Accepts only opted-in foreground-qualified source frames; it never synthesizes frames from polls.</summary>
    public void RecordFrame(RecordedFrame frame)
    {
        lock (_gate) RecordFrameLocked(frame, _channel);
    }

    /// <summary>Bind once at Start. A callback copied before unsubscribe cannot write into a later session.</summary>
    public Action<RecordedFrame> CreateFrameSink()
    {
        lock (_gate)
        {
            var channel = _channel;
            return frame => { lock (_gate) RecordFrameLocked(frame, channel); };
        }
    }

    private void RecordFrameLocked(RecordedFrame frame, Channel<Entry>? channel)
    {
        if (!ReferenceEquals(channel, _channel)) return;
        if (!_isRecording || !_includeFrames || _channel is null) return;
        var at = frame.TimestampUtc.ToUniversalTime();
        if (at < _startedUtc) return; // receipt was queued before this session started
        if (frame.Pid <= 0 || !double.IsFinite(frame.FrameTimeMs) || frame.FrameTimeMs <= 0)
        {
            if (!_stopping) Fail("Recording stopped because an invalid raw frame was received.");
            return;
        }
        if (!_channel.Writer.TryWrite(new Frame(frame with { TimestampUtc = at })) && !_stopping)
            Fail("Recording stopped because the file writer could not keep up. The partial recording is available.");
    }

    private void Fail(string message)
    {
        lock (_gate)
        {
        _error = message;
        _isRecording = false;
        _includeFrames = false;
        _channel?.Writer.TryComplete();
        }
    }

    private async Task WriteAsync(Channel<Entry> channel, TextWriter output, DateTime startedUtc)
    {
        try
        {
            using (output)
            {
                var last = startedUtc;
                var unflushedFrames = 0;
                await foreach (var entry in channel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    if (entry is Sample sample)
                    {
                        await output.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            type = "sample", timestampUtc = sample.TimestampUtc, values = sample.Values,
                        })).ConfigureAwait(false);
                        if (sample.TimestampUtc > last) last = sample.TimestampUtc;
                    }
                    else if (entry is Frame frame)
                    {
                        var value = frame.Value;
                        if (value.TimestampUtc > last) last = value.TimestampUtc;
                        await output.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            type = "frame", timestampUtc = value.TimestampUtc, pid = value.Pid, frameTimeMs = value.FrameTimeMs,
                        })).ConfigureAwait(false);
                    }
                    if (entry is Sample || ++unflushedFrames >= 128)
                    {
                        await output.FlushAsync().ConfigureAwait(false);
                        unflushedFrames = 0;
                    }
                }
                if (_error is null)
                {
                    var endedUtc = _clock().ToUniversalTime();
                    if (endedUtc < last) endedUtc = last;
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { type = "end", endedUtc })).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) { Fail("Recording could not be saved: " + ex.Message); }
        finally { lock (_gate) { _isRecording = false; _includeFrames = false; } }
    }

    public async Task StopAsync()
    {
        Task? writer;
        lock (_gate)
        {
        _stopping = true;
        _isRecording = false;
        _includeFrames = false;
        _channel?.Writer.TryComplete();
        writer = _writer;
        }
        if (writer is not null) await writer.ConfigureAwait(false);
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
