using Stats.Core.Sensors;

namespace Stats.UiPreview.Fixtures;

/// <summary>Fixed fixture time/seed/step shared by every scenario (PREVIEW_HARNESS.md: "freeze fixture time,
/// random seed, culture, and sample step for repeatable exports").</summary>
internal static class TimeSeries
{
    public static readonly DateTime FixedTimeUtc = new(2026, 9, 6, 14, 30, 0, DateTimeKind.Utc);
    public const int Seed = 1234;
    public const double StepSeconds = 1.0;
    public const int Ticks = 60;

    /// <summary>tickIndex 0 = oldest, Ticks-1 = FixedTimeUtc exactly.</summary>
    public static DateTime TimestampFor(int tickIndex) => FixedTimeUtc.AddSeconds(-(Ticks - 1 - tickIndex) * StepSeconds);
}

/// <summary>Builds a deterministic per-metric time series: a target value with small seeded noise, exact on the
/// final (== "current") tick unless the metric ends on a gap. Shared by every scenario in Scenarios.cs.</summary>
internal sealed class SnapshotBuilder
{
    private readonly Random _rng;
    private readonly Dictionary<string, float?[]> _series = new();

    public SnapshotBuilder(Random rng) => _rng = rng;

    /// <summary>Adds a metric whose value wanders around <paramref name="target"/> by ± <paramref name="noise"/>,
    /// landing exactly on <paramref name="target"/> for the final (current) tick.</summary>
    public SnapshotBuilder Add(string id, float target, float noise, int ticks = TimeSeries.Ticks)
    {
        var arr = new float?[ticks];
        for (int i = 0; i < ticks; i++)
            arr[i] = noise <= 0 ? target : target + (float)((_rng.NextDouble() * 2 - 1) * noise);
        arr[ticks - 1] = target;
        _series[id] = arr;
        return this;
    }

    /// <summary>Adds a metric that is a gap (null) for its last <paramref name="gapTicks"/> samples — current
    /// value ends up null and the tail of its history shows a gap, per PREVIEW_HARNESS.md's "missing" scenario.</summary>
    public SnapshotBuilder AddWithTrailingGap(string id, float target, float noise, int gapTicks, int ticks = TimeSeries.Ticks)
    {
        Add(id, target, noise, ticks);
        var arr = _series[id];
        for (int i = Math.Max(0, ticks - gapTicks); i < ticks; i++) arr[i] = null;
        return this;
    }

    /// <summary>Adds a metric with a known exact value at every tick (e.g. RPM readings used only by fan fixtures).</summary>
    public SnapshotBuilder AddExact(string id, IReadOnlyList<float?> values)
    {
        _series[id] = values.ToArray();
        return this;
    }

    public List<SensorSnapshot> Build()
    {
        int ticks = _series.Values.Select(v => v.Length).DefaultIfEmpty(TimeSeries.Ticks).Max();
        var list = new List<SensorSnapshot>(ticks);
        for (int i = 0; i < ticks; i++)
        {
            var values = new Dictionary<string, float?>();
            foreach (var (id, arr) in _series) values[id] = i < arr.Length ? arr[i] : null;
            list.Add(new SensorSnapshot(values, TimeSeries.TimestampFor(i)));
        }
        return list;
    }
}
