using System.Text.Json;
using System.Threading.Channels;
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Recording;

/// <summary>Explicit UI-thread commands; only owned samples cross to the file writer.</summary>
public sealed class SessionRecorder : IDisposable
{
    private sealed record Sample(DateTime TimestampUtc, float?[] Values);
    private readonly string _directory;
    private readonly Func<DateTime> _clock;
    private Channel<Sample>? _channel;
    private Task? _writer;
    private string[] _ids = [];
    private DateTime _lastTimestampUtc;
    private bool _hasSample;
    private volatile bool _isRecording;
    private volatile string? _error;

    public SessionRecorder(string directory, Func<DateTime>? clock = null)
    { _directory = directory; _clock = clock ?? (() => DateTime.UtcNow); }
    public bool IsRecording => _isRecording;
    public string? FilePath { get; private set; }
    public string? Error => _error;

    public void Start(IReadOnlyList<MetricDefinition> definitions, DateTime startedUtc)
    {
        if (_isRecording || _writer is { IsCompleted: false })
            throw new InvalidOperationException("Previous recording is still stopping.");
        var metrics = definitions.ToArray();
        SessionFile.ValidateDefinitions(metrics);
        startedUtc = startedUtc.ToUniversalTime();
        _error = null;
        FilePath = null;
        StreamWriter? output = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{startedUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.stats-session.jsonl");
            output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            output.WriteLine(JsonSerializer.Serialize(new { type = "header", version = 1, startedUtc, metrics }));
            output.Flush();
            var channel = Channel.CreateBounded<Sample>(new BoundedChannelOptions(128)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
            });
            _ids = metrics.Select(metric => metric.Id).ToArray();
            _lastTimestampUtc = startedUtc;
            _hasSample = false;
            _channel = channel;
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
            throw;
        }
    }

    public void Record(SensorSnapshot snapshot)
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
        if (!_channel.Writer.TryWrite(new Sample(at, values)))
            Fail("Recording stopped because the file writer could not keep up. The partial recording is available.");
        else { _lastTimestampUtc = at; _hasSample = true; }
    }

    private void Fail(string message)
    {
        _error = message;
        _isRecording = false;
        _channel?.Writer.TryComplete();
    }

    private async Task WriteAsync(Channel<Sample> channel, StreamWriter output, DateTime startedUtc)
    {
        try
        {
            using (output)
            {
                var last = startedUtc;
                await foreach (var sample in channel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    await output.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        type = "sample", timestampUtc = sample.TimestampUtc, values = sample.Values,
                    })).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                    last = sample.TimestampUtc;
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
        finally { _isRecording = false; }
    }

    public async Task StopAsync()
    {
        _isRecording = false;
        _channel?.Writer.TryComplete();
        if (_writer is not null) await _writer.ConfigureAwait(false);
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
