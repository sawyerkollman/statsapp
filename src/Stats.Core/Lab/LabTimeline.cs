namespace Stats.Core.Lab;

public sealed record LabTimelineEvent(DateTime AtUtc, string Kind, string Text, string? RuleKey = null);

/// <summary>Independent AND rules. Missing data breaks holds; a sustained episode raises once.</summary>
public sealed class CompoundEvaluator
{
    private sealed class State
    {
        public DateTime? Since, Last, Seen;
        public bool Raised;
        public required string Signature;
    }
    private readonly Dictionary<CompoundRule, State> _states = new();
    private DateTime? _lastTick;
    public void Reset() => _states.Clear();
    public IReadOnlyList<CompoundRule> Tick(IEnumerable<CompoundRule> rules, IReadOnlyDictionary<string, float?> values, DateTime utc, double pollSeconds = 1)
    {
        if (_lastTick > utc) _states.Clear();
        _lastTick = utc;
        var active = rules.Where(r => r.Enabled).Take(32).ToArray();
        foreach (var stale in _states.Keys.Where(r => !active.Contains(r)).ToArray()) _states.Remove(stale);
        var raised = new List<CompoundRule>();
        foreach (var rule in active)
        {
            var signature = $"{rule.FirstMetricId}|{rule.SecondMetricId}|{rule.FirstThreshold:R}|{rule.SecondThreshold:R}|{rule.FirstLower}|{rule.SecondLower}|{rule.HoldSeconds}|{rule.CooldownSeconds}|{rule.TestOnly}";
            if (!_states.TryGetValue(rule, out var state) || state.Signature != signature)
                _states[rule] = state = new State { Signature = signature };
            // Allow jitter up to three configured polls; longer gaps break continuity.
            var maxGap = TimeSpan.FromSeconds(3 * (double.IsFinite(pollSeconds) ? Math.Clamp(pollSeconds, .5, 5) : 1));
            if (state.Since > utc || (state.Seen is DateTime seen && utc - seen > maxGap)) { state.Since = null; state.Raised = false; }
            if (!Match(rule.FirstMetricId, rule.FirstThreshold, rule.FirstLower) || !Match(rule.SecondMetricId, rule.SecondThreshold, rule.SecondLower))
            { state.Since = null; state.Raised = false; continue; }
            state.Since ??= utc;
            state.Seen = utc;
            if (!state.Raised && utc - state.Since.Value >= TimeSpan.FromSeconds(Math.Clamp(rule.HoldSeconds, 1, 120)) &&
                (state.Last is null || utc - state.Last.Value >= TimeSpan.FromSeconds(Math.Clamp(rule.CooldownSeconds, 1, 3600))))
            { state.Last = utc; state.Raised = true; raised.Add(rule); }
        }
        return raised;
        bool Match(string id, float threshold, bool lower) => float.IsFinite(threshold) &&
            values.TryGetValue(id, out var value) && value is float number && float.IsFinite(number) &&
            (lower ? number <= threshold : number >= threshold);
    }
}
