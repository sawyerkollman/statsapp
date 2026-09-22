using Stats.Core.Metrics;
using Stats.Core.Frames;

namespace Stats.Core.Recording;

public sealed record SessionSpike(long SampleIndex, DateTime TimestampUtc, float Value, IReadOnlyList<string> Readings)
{ public string Details => string.Join(" · ", Readings); }
public sealed record SessionReport(TimeSpan Duration, bool IsComplete, IReadOnlyList<SessionMetricSummary> Summaries,
    IReadOnlyList<SessionSpike> Spikes, string Text);

public static class SessionReports
{
    public static SessionReport Build(SessionData data, long sampleOffset = 0, bool windowSummaries = false)
    {
        var duration = (data.EndedUtc ?? data.LastObservedUtc ?? data.Series.SelectMany(s => s.TimesUtc).DefaultIfEmpty(data.StartedUtc).Max()) - data.StartedUtc;
        var frame = data.Series.FirstOrDefault(s => s.Definition.Id == FrameMetrics.FrameTimeId);
        var spikes = frame is null ? [] : frame.Values.Select((value, index) => (value, index)).Where(x => x.value is float)
            .OrderByDescending(x => x.value).Take(20).Select(x => new SessionSpike(sampleOffset + x.index, frame.TimesUtc[x.index], x.value!.Value,
                data.Series.Where(s => s != frame).Select(s => (s, index: Array.IndexOf(s.TimesUtc, frame.TimesUtc[x.index])))
                    .Where(x => x.index >= 0 && x.index < x.s.Values.Length && x.s.Values[x.index] is float).Take(8)
                    .Select(x => { var baseline = x.s.Values.Where(v => v is float).Select(v => v!.Value).Average(); var value = x.s.Values[x.index]!.Value;
                        return $"{x.s.Definition.DisplayName}: {ValueFormatter.Format(x.s.Definition, value)} (Δ {(value - baseline):+0.##;-0.##;0} {x.s.Definition.Unit} vs window mean)"; }).ToArray())).ToArray();
        var text = $"Full recording: duration {duration:g}; {(data.IsComplete ? "complete" : "incomplete")}; {data.SampleCount} sampled snapshots. " +
            "Top sampled frame times are not proven stutters. FPS and rolling 1% low are poll-sampled summaries, not true per-frame statistics.";
        text += data.FrameSummary is { } raw
            ? $"\nRaw frame timing (session opt-in, exact whole-file summary): {raw.Count:N0} frames; p50 {raw.P50FrameTimeMs:0.##} ms, p95 {raw.P95FrameTimeMs:0.##} ms, p99 {raw.P99FrameTimeMs:0.##} ms; mean {raw.MeanFrameTimeMs:0.##} ms, max {raw.MaxFrameTimeMs:0.##} ms; {raw.HitchFrameCount:N0} frames above 50 ms. This descriptive threshold does not identify a cause."
            : data.IncludesRawFrames && windowSummaries
                ? "\nRaw frame timing was opted in. Reopen the full recording for its exact whole-file raw-frame summary; replay windows do not recalculate it."
            : data.IncludesRawFrames
                ? "\nRaw frame timing was opted in, but this session contains no foreground-qualified raw frames."
                : "\nRaw frame timing was not opted in for this session; all frame metrics above are poll-sampled.";
        var windowCount = data.Series.FirstOrDefault()?.Values.Length ?? 0;
        text += $"\nSlow-frame list covers only {windowCount} retained snapshots starting at sample {sampleOffset + 1}. " +
            (windowSummaries ? "Highlights cover this replay window only." : "Highlights summarize the full recording.");
        var definitions = data.Series.Select(s => s.Definition).ToDictionary(d => d.Id);
        var highlights = data.Summaries.Where(s => definitions.TryGetValue(s.MetricId, out var d) &&
                (FrameMetrics.IsFrameMetric(d.Id) || d.Unit is "°C" or "W"))
            .OrderBy(s => FrameMetrics.IsFrameMetric(s.MetricId) ? 0 : 1).Take(12)
            .Select(s => { var d = definitions[s.MetricId]; return $"{d.DisplayName}: sampled mean {ValueFormatter.Format(d, s.Average)}, peak {ValueFormatter.Format(d, s.Max)}"; });
        text += "\n" + string.Join("\n", highlights);
        return new(duration, data.IsComplete, data.Summaries, spikes, text);
    }
}
