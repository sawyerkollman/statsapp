using Stats.Core.Metrics;
namespace Stats.Core.Recording;
public sealed record MetricSeries(MetricDefinition Definition, DateTime[] TimesUtc, float?[] Values);
public sealed record SessionMetricSummary(string MetricId, float? Min, float? Average, float? Max);
public sealed record SessionData(DateTime StartedUtc, DateTime? EndedUtc, bool IsComplete, long SampleCount, IReadOnlyList<MetricSeries> Series, IReadOnlyList<SessionMetricSummary> Summaries)
{
    public string? GameName { get; init; }
    public bool IncludesRawFrames { get; init; }
    public DateTime? LastObservedUtc { get; init; }
    public SessionFrameSummary? FrameSummary { get; init; }
}
