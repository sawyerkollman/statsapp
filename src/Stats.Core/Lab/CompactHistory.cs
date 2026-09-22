using System.Text.Json;
using System.Threading.Channels;

namespace Stats.Core.Lab;

public sealed record HistoryAggregate(DateTime MinuteUtc, string MetricId, int Count, float Min, double Average, float Max);

/// <summary>Minute summaries only; never accesses the user's manually saved recording directory.</summary>
public sealed class CompactHistory : IDisposable
{
    private sealed class Bucket
    {
        public int Count;
        public double Sum;
        public float Min = float.MaxValue, Max = float.MinValue;
    }
    private readonly string _directory;
    private readonly Dictionary<string, Bucket> _buckets = new();
    private readonly Channel<HistoryAggregate[]> _queue = Channel.CreateBounded<HistoryAggregate[]>(16);
    private readonly Task _writer;
    private DateTime? _minute;
    private volatile string? _error;
    private int _retentionDays = 7;
    private long _maxBytes = 64 * 1024 * 1024;
    public string? Error => _error;
    public CompactHistory(string labDirectory)
    {
        _directory = Path.Combine(Path.GetFullPath(labDirectory), "compact-history");
        _writer = Task.Run(WriteAsync);
    }
    public void Record(IReadOnlyDictionary<string, float?> values, DateTime utc, int retentionDays, int maximumMb)
    {
        if (_error is not null) return;
        _retentionDays = Math.Clamp(retentionDays, 1, 90);
        Interlocked.Exchange(ref _maxBytes, Math.Clamp(maximumMb, 16, 512) * 1024L * 1024);
        utc = utc.ToUniversalTime();
        var minute = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        if (_minute is { } last && minute < last) { _error = "History paused: sample clock moved backwards."; return; }
        if (_minute is not null && minute != _minute) Flush();
        _minute = minute;
        foreach (var (id, value) in values.Take(128))
        {
            if (value is not float number || !float.IsFinite(number)) continue;
            if (!_buckets.ContainsKey(id) && _buckets.Count >= 128) continue;
            if (!_buckets.TryGetValue(id, out var bucket)) _buckets[id] = bucket = new();
            bucket.Count++; bucket.Sum += number; bucket.Min = Math.Min(bucket.Min, number); bucket.Max = Math.Max(bucket.Max, number);
        }
    }
    private void Flush()
    {
        if (_minute is not { } minute || _buckets.Count == 0) return;
        var rows = _buckets.Select(p => new HistoryAggregate(minute, p.Key, p.Value.Count, p.Value.Min, p.Value.Sum / p.Value.Count, p.Value.Max)).ToArray();
        _buckets.Clear();
        if (!_queue.Writer.TryWrite(rows)) _error = "History paused: disk writer could not keep up.";
    }
    private async Task WriteAsync()
    {
        try
        {
            await foreach (var rows in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                Directory.CreateDirectory(_directory);
                if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("History directory cannot be a link.");
                var day = rows[0].MinuteUtc.Date;
                var files = Directory.EnumerateFiles(_directory, "*.lab-history.jsonl").Take(513).Select(p => new FileInfo(p)).ToArray();
                if (files.Length > 512) throw new IOException("History directory contains too many files.");
                foreach (var file in files)
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("History files cannot be links.");
                    if (DateTime.TryParseExact(file.Name[..^".lab-history.jsonl".Length], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fileDay) && fileDay < day.AddDays(1 - _retentionDays)) file.Delete();
                }
                var text = string.Join(Environment.NewLine, rows.Select(row => JsonSerializer.Serialize(row))) + Environment.NewLine;
                var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
                var used = files.Where(f => { f.Refresh(); return f.Exists; }).Sum(f => f.Length);
                foreach (var file in files.Where(f => f.Exists && f.Name != $"{day:yyyyMMdd}.lab-history.jsonl").OrderBy(f => f.Name))
                {
                    if (used + bytes <= Interlocked.Read(ref _maxBytes)) break;
                    // Only date-named files created by this writer are eligible for retention.
                    if (!DateTime.TryParseExact(file.Name[..^".lab-history.jsonl".Length], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _)) continue;
                    used -= file.Length; file.Delete();
                }
                if (used + bytes > Interlocked.Read(ref _maxBytes)) throw new IOException("History cap reached; increase the cap or wait for the next day.");
                await File.AppendAllTextAsync(Path.Combine(_directory, $"{day:yyyyMMdd}.lab-history.jsonl"), text).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { _error = "History paused: " + ex.Message; }
    }
    public IReadOnlyList<HistoryAggregate> ReadRecent()
    {
        var rows = new Queue<HistoryAggregate>();
        if (!Directory.Exists(_directory)) return [];
        if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("History directory cannot be a link.");
        var files = Directory.EnumerateFiles(_directory, "*.lab-history.jsonl").Take(513).ToArray();
        if (files.Length > 512) throw new InvalidDataException("History directory contains too many files.");
        foreach (var path in files.OrderBy(p => p).TakeLast(7))
        {
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 512L * 1024 * 1024)
                throw new InvalidDataException("History file is linked or exceeds 512 MB.");
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (reader.Peek() >= 0)
            {
                var line = new System.Text.StringBuilder();
                int character;
                while ((character = reader.Read()) >= 0 && character != '\n')
                {
                    if (line.Length >= 16384) throw new InvalidDataException("History row exceeds 16 KB.");
                    line.Append((char)character);
                }
                try
                {
                    var row = JsonSerializer.Deserialize<HistoryAggregate>(line.ToString());
                    if (row is null || row.MinuteUtc.Kind != DateTimeKind.Utc || row.Count <= 0 ||
                        string.IsNullOrWhiteSpace(row.MetricId) || row.MetricId.Length > 1024 ||
                        !float.IsFinite(row.Min) || !float.IsFinite(row.Max) || !double.IsFinite(row.Average) ||
                        row.Min > row.Max || row.Average < row.Min || row.Average > row.Max)
                        throw new InvalidDataException("Invalid compact-history row.");
                    if (rows.Count == 1000) rows.Dequeue();
                    rows.Enqueue(row);
                }
                catch (JsonException) when (character < 0) { /* Only an unterminated last append may be incomplete. */ }
                catch (JsonException ex) { throw new InvalidDataException("Corrupt interior compact-history row.", ex); }
            }
        }
        return rows.ToArray();
    }
    public void Dispose() { Flush(); _queue.Writer.TryComplete(); _writer.GetAwaiter().GetResult(); }
}
