using System.Text.Json;
using Stats.Core.Recording;
namespace Stats.Core.Alerts;
public sealed class AlertHistoryStore
{
    public const int MaxRecords = 200;
    public const int MaxContextSamples = 240;
    private readonly string _path;
    public AlertHistoryStore(string directory) => _path = Path.Combine(directory, "alerts.json");
    public string? Error { get; private set; }
    public IReadOnlyList<AlertRecord> Load()
    {
        try
        {
            Error = null;
            if (!File.Exists(_path)) return [];
            var records = JsonSerializer.Deserialize<List<AlertRecord>>(File.ReadAllText(_path)) ?? [];
            return Sanitize(records).Select(record => record.State == AlertState.Ongoing
                ? record with { State = AlertState.Interrupted }
                : record).ToArray();
        }
        catch (Exception exception)
        {
            Error = $"Alert history could not be loaded: {exception.Message}";
            return [];
        }
    }
    public void Save(IReadOnlyList<AlertRecord> records)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Sanitize(records)));
            File.Move(temporaryPath, _path, true);
            Error = null;
        }
        catch (Exception exception)
        {
            Error = $"Alert history could not be saved: {exception.Message}";
        }
    }

    private static IReadOnlyList<AlertRecord> Sanitize(IEnumerable<AlertRecord> records) => records
        .Where(record => record is not null && record.Id != Guid.Empty && record.Event is not null
            && !string.IsNullOrWhiteSpace(record.Event.MetricId) && float.IsFinite(record.Event.PeakValue)
            && float.IsFinite(record.Event.Threshold))
        .OrderByDescending(record => record.RaisedUtc)
        .Take(MaxRecords)
        .Select(Sanitize)
        .ToArray();

    private static AlertRecord Sanitize(AlertRecord record)
    {
        record = record with { RaisedUtc = record.RaisedUtc.ToUniversalTime() };
        if (!Enum.IsDefined(record.State)) record = record with { State = AlertState.Interrupted };
        if (record.Duration < TimeSpan.Zero) record = record with { Duration = null };
        var context = record.Context;
        if (context?.TimesUtc is null || context.Values is null || context.TimesUtc.Length != context.Values.Length)
            return record with { Context = null, PreContextCount = 0 };
        try { SessionFile.ValidateDefinitions(new[] { context.Definition }); }
        catch (InvalidDataException) { return record with { Context = null, PreContextCount = 0 }; }
        var allTimes = context.TimesUtc.Select(time => time.ToUniversalTime()).ToArray();
        for (var i = 1; i < allTimes.Length; i++)
            if (allTimes[i] < allTimes[i - 1]) return record with { Context = null, PreContextCount = 0 };
        var boundary = Math.Clamp(record.PreContextCount, 0, allTimes.Length);
        var before = Math.Min(120, boundary);
        var after = Math.Min(120, allTimes.Length - boundary);
        var times = allTimes.Skip(boundary - before).Take(before + after).ToArray();
        var values = context.Values.Skip(boundary - before).Take(before + after)
            .Select(value => value is { } number && float.IsFinite(number) ? value : null)
            .ToArray();
        return record with { Context = new MetricSeries(context.Definition, times, values), PreContextCount = before };
    }
}
