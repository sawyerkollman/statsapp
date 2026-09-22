namespace Stats.Core.Recording;

public sealed record SessionComparison(string MetricId, string Unit, int PairCount, double Coverage, float? FirstAverage, float? SecondAverage, float? Delta, float? RelativeDelta)
{
    public double DurationSeconds { get; init; }
    public int FirstCount { get; init; }
    public int SecondCount { get; init; }
    public double FirstCoverage { get; init; }
    public double SecondCoverage { get; init; }
    public double BinSeconds { get; init; }
}
public sealed record SessionCorrelation(string FirstMetricId, string SecondMetricId, int PairCount, double Coverage, double? Pearson);
public sealed record SessionRunVariation(string MetricId, int Runs, float? Mean, float? Minimum, float? Maximum, float? StandardDeviation, double Coverage)
{
    public string Unit { get; init; } = "";
    public int TotalRuns { get; init; }
}

public static class SessionAnalysis
{
    public static IReadOnlyList<SessionComparison> Compare(SessionData first, SessionData second)
    {
        var other = second.Series.ToDictionary(s => s.Definition.Id);
        return first.Series.Where(s => other.TryGetValue(s.Definition.Id, out var o) && o.Definition.Unit == s.Definition.Unit)
            .Select(s => CompareElapsed(s, first.StartedUtc, other[s.Definition.Id], second.StartedUtc)).ToArray();
    }

    public static IReadOnlyList<SessionComparison> CompareWindow(SessionData first, SessionData second, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        var other = second.Series.ToDictionary(s => s.Definition.Id);
        return first.Series.Where(s => other.TryGetValue(s.Definition.Id, out var o) && o.Definition.Unit == s.Definition.Unit)
            .Select(s => CompareElapsed(s, first.StartedUtc, other[s.Definition.Id], second.StartedUtc, seconds)).ToArray();
    }

    private static SessionComparison CompareElapsed(MetricSeries first, DateTime firstStart, MetricSeries second, DateTime secondStart, double? requestedSeconds = null)
    {
        var empty = new SessionComparison(first.Definition.Id, first.Definition.Unit, 0, 0, null, null, null, null);
        if (first.Definition.Unit != second.Definition.Unit) return empty;
        if (requestedSeconds is null && (first.Values.Length == 0 || second.Values.Length == 0)) return empty;
        var from = requestedSeconds.HasValue ? 0 : Math.Max((first.TimesUtc[0] - firstStart).TotalSeconds, (second.TimesUtc[0] - secondStart).TotalSeconds);
        var to = requestedSeconds ?? Math.Min((first.TimesUtc[^1] - firstStart).TotalSeconds, (second.TimesUtc[^1] - secondStart).TotalSeconds);
        if (to < from) return empty;
        var duration = to - from;
        var step = Math.Max(Math.Max(Cadence(first), Cadence(second)), Math.Max(0.001, duration / 3599));
        var count = Math.Min(3600, (int)Math.Floor(duration / step) + 1);
        var left = Bins(first, firstStart, from, to, step, count, out var leftCount);
        var right = Bins(second, secondStart, from, to, step, count, out var rightCount);
        var pairs = Enumerable.Range(0, count).Where(i => left[i].HasValue && right[i].HasValue).ToArray();
        float? a = pairs.Length == 0 ? null : (float)pairs.Average(i => left[i]!.Value);
        float? b = pairs.Length == 0 ? null : (float)pairs.Average(i => right[i]!.Value);
        float? delta = a is float av && b is float bv ? bv - av : null;
        return new(first.Definition.Id, first.Definition.Unit, pairs.Length, (double)pairs.Length / count, a, b, delta,
            delta is float d && a is float baseline && baseline != 0 ? d / baseline : null)
        { DurationSeconds = duration, FirstCount = leftCount, SecondCount = rightCount,
            FirstCoverage = (double)left.Count(v => v.HasValue) / count, SecondCoverage = (double)right.Count(v => v.HasValue) / count, BinSeconds = step };
    }
    private static double Cadence(MetricSeries series)
    {
        var gaps = series.TimesUtc.Zip(series.TimesUtc.Skip(1), (a, b) => (b - a).TotalSeconds).Where(v => v > 0).Order().ToArray();
        return gaps.Length == 0 ? 1 : gaps[(gaps.Length - 1) / 2];
    }
    private static double?[] Bins(MetricSeries series, DateTime start, double from, double to, double step, int count, out int samples)
    {
        var sums = new double[count]; var counts = new int[count]; samples = 0;
        for (var i = 0; i < series.Values.Length; i++)
        {
            var elapsed = (series.TimesUtc[i] - start).TotalSeconds;
            if (elapsed < from || elapsed > to || series.Values[i] is not float value || !float.IsFinite(value)) continue;
            var bin = Math.Min(count - 1, (int)((elapsed - from) / step));
            sums[bin] += value; counts[bin]++; samples++;
        }
        return Enumerable.Range(0, count).Select(i => counts[i] == 0 ? (double?)null : sums[i] / counts[i]).ToArray();
    }
    public static SessionComparison Compare(MetricSeries first, MetricSeries second)
    {
        return CompareElapsed(first, first.TimesUtc.FirstOrDefault(), second, second.TimesUtc.FirstOrDefault());
    }
    public static SessionCorrelation Correlate(MetricSeries first, MetricSeries second)
    {
        var n = Math.Min(first.Values.Length, second.Values.Length); var pairs = Enumerable.Range(0, n).Where(i => first.Values[i] is not null && second.Values[i] is not null).ToArray();
        if (pairs.Length < 2) return new(first.Definition.Id, second.Definition.Id, pairs.Length, n == 0 ? 0 : (double)pairs.Length / n, null);
        var x = pairs.Select(i => (double)first.Values[i]!.Value).ToArray(); var y = pairs.Select(i => (double)second.Values[i]!.Value).ToArray();
        var mx = x.Average(); var my = y.Average(); var numerator = x.Zip(y).Sum(p => (p.First - mx) * (p.Second - my));
        var denominator = Math.Sqrt(x.Sum(v => Math.Pow(v - mx, 2)) * y.Sum(v => Math.Pow(v - my, 2)));
        return new(first.Definition.Id, second.Definition.Id, pairs.Length, (double)pairs.Length / n, denominator == 0 ? null : numerator / denominator);
    }
    public static IReadOnlyList<SessionRunVariation> Variation(IEnumerable<SessionData> runs, double? requestedSeconds = null)
    {
        if (requestedSeconds is double seconds && (!double.IsFinite(seconds) || seconds is < 1 or > 3600))
            throw new ArgumentOutOfRangeException(nameof(requestedSeconds));
        // Retain scalar summaries only; lazy callers load one bounded recording at a time.
        var groups = new Dictionary<(string Id, string Unit), List<(float Mean, double Coverage)>>();
        int total = 0;
        foreach (var run in runs.Take(10))
        {
            total++;
            foreach (var series in run.Series)
            {
                var key = (series.Definition.Id, series.Definition.Unit);
                if (!groups.TryGetValue(key, out var entries)) groups[key] = entries = new();
                double sum = 0; int count = 0;
                foreach (var value in series.Values)
                    if (value is float f && float.IsFinite(f)) { sum += f; count++; }
                if (count == 0) continue;
                var measured = requestedSeconds is double duration
                    ? CompareElapsed(series, run.StartedUtc, series, run.StartedUtc, duration) : null;
                if (measured is not null && measured.FirstAverage is null) continue;
                entries.Add((measured?.FirstAverage ?? (float)(sum / count), measured?.Coverage ?? (double)count / series.Values.Length));
            }
        }
        return groups.Select(group =>
        {
            var values = group.Value.Select(x => x.Mean).ToArray();
            var mean = values.Length == 0 ? (float?)null : (float)values.Average();
            var variance = values.Length < 2 ? (float?)null : (float)Math.Sqrt(values.Average(v => Math.Pow(v - mean!.Value, 2)));
            return new SessionRunVariation(group.Key.Id, values.Length, mean, values.Length == 0 ? null : values.Min(), values.Length == 0 ? null : values.Max(), variance,
                total == 0 ? 0 : group.Value.Sum(x => x.Coverage) / total) { Unit = group.Key.Unit, TotalRuns = total };
        }).ToArray();
    }
}
