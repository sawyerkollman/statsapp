using System.Globalization;
using System.Text;
using System.Text.Json;
using Stats.Core.Metrics;

namespace Stats.Core.Recording;

public static class SessionFile
{
    public const int ChartSampleLimit = 3600;

    internal static void ValidateDefinitions(IReadOnlyList<MetricDefinition> definitions)
    {
        if (definitions.Count is 0 or > 1024 || definitions.Any(d => d is null ||
            string.IsNullOrWhiteSpace(d.Id) || d.DisplayName is null || d.Unit is null ||
            d.Format is null || d.Format.Length != 2 || d.Format[0] != 'F' || !char.IsAsciiDigit(d.Format[1])) ||
            definitions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new InvalidDataException("A session needs between 1 and 1024 distinct valid metrics.");
    }

    public static SessionData Load(string path)
    {
        var times = new Queue<DateTime>();
        Queue<float?>[] values = [];
        double[] sums = [];
        long[] counts = [];
        float?[] mins = [], maxs = [];
        var result = Read(path, definitions =>
        {
            values = definitions.Select(_ => new Queue<float?>()).ToArray();
            sums = new double[definitions.Length];
            counts = new long[definitions.Length];
            mins = new float?[definitions.Length];
            maxs = new float?[definitions.Length];
        }, (at, row) =>
        {
            if (times.Count == ChartSampleLimit)
            {
                times.Dequeue();
                foreach (var queue in values) queue.Dequeue();
            }
            times.Enqueue(at);
            for (var i = 0; i < row.Length; i++)
            {
                values[i].Enqueue(row[i]);
                if (row[i] is not float number) continue;
                sums[i] += number;
                counts[i]++;
                mins[i] = mins[i] is float min ? Math.Min(min, number) : number;
                maxs[i] = maxs[i] is float max ? Math.Max(max, number) : number;
            }
        });
        var timestamps = times.ToArray();
        var series = result.Definitions.Select((definition, i) =>
            new MetricSeries(definition, timestamps, values[i].ToArray())).ToArray();
        var summaries = result.Definitions.Select((definition, i) => new SessionMetricSummary(
            definition.Id, mins[i], counts[i] == 0 ? null : (float)(sums[i] / counts[i]), maxs[i])).ToArray();
        return new(result.StartedUtc, result.EndedUtc, result.EndedUtc is not null,
            result.SampleCount, series, summaries);
    }

    public static void ExportCsv(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a CSV destination different from the recording.");
        // Preserve an existing export if validation or writing fails.
        var temporary = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)))
            {
                Read(source, definitions => writer.WriteLine("\"UTC\"," + string.Join(',', definitions.Select(d =>
                    Quote($"{d.DisplayName} ({d.Unit})")))), (at, values) => writer.WriteLine(
                    at.ToString("O", CultureInfo.InvariantCulture) + "," + string.Join(',', values.Select(value =>
                        value?.ToString("R", CultureInfo.InvariantCulture) ?? ""))));
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record ReadResult(MetricDefinition[] Definitions, DateTime StartedUtc, DateTime? EndedUtc, long SampleCount);

    private static ReadResult Read(string path, Action<MetricDefinition[]> header, Action<DateTime, float?[]> sample)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var first = reader.ReadLine() ?? throw new InvalidDataException("Missing session header.");
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != "header" || root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported session format.");
        var started = root.GetProperty("startedUtc").GetDateTime().ToUniversalTime();
        var definitions = root.GetProperty("metrics").Deserialize<MetricDefinition[]>()
            ?? throw new InvalidDataException("Missing session metrics.");
        ValidateDefinitions(definitions);
        header(definitions);
        DateTime previous = started;
        DateTime? end = null;
        long count = 0;
        for (string? line; (line = reader.ReadLine()) is not null;)
        {
            if (end is not null) throw new InvalidDataException("Unexpected data after the session ended.");
            JsonDocument rowDocument;
            try { rowDocument = JsonDocument.Parse(line); }
            catch (JsonException) when (reader.Peek() < 0)
            {
                // Interrupted appends may leave one incomplete final object; preceding rows remain useful.
                break;
            }
            using (rowDocument)
            {
                var row = rowDocument.RootElement;
                var type = row.GetProperty("type").GetString();
                if (type == "end")
                {
                    end = row.GetProperty("endedUtc").GetDateTime().ToUniversalTime();
                    if (end < previous) throw new InvalidDataException("Session end precedes its samples.");
                    continue;
                }
                if (type != "sample") throw new InvalidDataException("Unknown session row.");
                var at = row.GetProperty("timestampUtc").GetDateTime().ToUniversalTime();
                if (at < previous) throw new InvalidDataException("Session timestamps are unordered.");
                var raw = row.GetProperty("values");
                if (raw.GetArrayLength() != definitions.Length) throw new InvalidDataException("Sample value count mismatch.");
                var values = new float?[definitions.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    if (raw[i].ValueKind == JsonValueKind.Null) continue;
                    var value = raw[i].GetSingle();
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite sample value.");
                    values[i] = value;
                }
                sample(at, values);
                count++;
                previous = at;
            }
        }
        return new(definitions, started, end, count);
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
