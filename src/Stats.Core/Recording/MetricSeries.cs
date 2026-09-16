using Stats.Core.Metrics;
namespace Stats.Core.Recording;
public sealed record MetricSeries(MetricDefinition Definition, DateTime[] TimesUtc, float?[] Values);
public sealed record SessionMetricSummary(string MetricId, float? Min, float? Average, float? Max);
public sealed record SessionData(DateTime StartedUtc, DateTime? EndedUtc, bool IsComplete, long SampleCount, IReadOnlyList<MetricSeries> Series, IReadOnlyList<SessionMetricSummary> Summaries);
