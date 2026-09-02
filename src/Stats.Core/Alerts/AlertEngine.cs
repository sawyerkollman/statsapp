using Stats.Core.Metrics;

namespace Stats.Core.Alerts;

/// <summary>Pure, clock-injected state machine evaluated once per UI-thread refresh with the set of *monitored*
/// metrics (union of dashboard and overlay selections). Entering Crit starts an episode with peak tracking; once
/// held for at least <see cref="HoldSeconds"/> it raises exactly one <see cref="AlertEvent"/> and disarms until
/// the metric leaves Crit. Flapping in and out of Crit inside the hold window raises nothing. Leaving Crit (Warn,
/// Normal, or a null/NaN value) ends the episode — <see cref="EpisodeEnded"/> fires with the final duration — and
/// re-arms it. A metric that simply stops being monitored (absent from a tick's samples) drops its state with no
/// event. No threads: call <see cref="Tick"/> synchronously from the UI-thread refresh.</summary>
public sealed class AlertEngine
{
    private readonly Func<DateTime> _localClock;

    /// <param name="localClock">Supplies the local time stamped on a raised <see cref="AlertEvent"/>; defaults to
    /// <see cref="DateTime.Now"/>. Injectable so tests can assert the raised time stays fixed.</param>
    public AlertEngine(Func<DateTime>? localClock = null) => _localClock = localClock ?? (() => DateTime.Now);

    /// <summary>How long a metric must hold Crit before an alert raises. Read live on every tick, so a change
    /// takes effect immediately (including for episodes already in progress).</summary>
    public double HoldSeconds { get; set; } = 10;

    /// <summary>Fires when an episode ends (metric left Crit): metric id, the local time the alert actually raised
    /// (null if the hold was never reached — nothing to finalize in the log), and the episode's total duration.</summary>
    public event Action<string, DateTime?, TimeSpan>? EpisodeEnded;

    private sealed class Episode
    {
        public DateTime CritSinceUtc;
        public float Peak;
        public bool Raised;
        public DateTime? RaisedAtLocal;
        public string DisplayName = "";
        public string Unit = "";
        public float Threshold;
        public bool LowerIsWorse;
    }

    private readonly Dictionary<string, Episode> _episodes = new();

    /// <summary>Evaluates one tick's samples against in-progress episodes. Returns the alerts raised this tick
    /// (usually empty — an episode raises at most once).</summary>
    public IReadOnlyList<AlertEvent> Tick(IEnumerable<AlertSample> samples, DateTime nowUtc)
    {
        var raised = new List<AlertEvent>();
        var seen = new HashSet<string>();

        foreach (var sample in samples)
        {
            var id = sample.Definition.Id;
            seen.Add(id);
            _episodes.TryGetValue(id, out var episode);

            bool inCrit = sample.Severity == Severity.Crit && sample.Value is float v && !float.IsNaN(v) && sample.Rule is not null;
            if (inCrit)
            {
                var value = sample.Value!.Value;
                var rule = sample.Rule!;
                if (episode is null)
                {
                    episode = new Episode
                    {
                        CritSinceUtc = nowUtc,
                        Peak = value,
                        DisplayName = sample.Definition.DisplayName,
                        Unit = sample.Definition.Unit,
                        Threshold = rule.Crit,
                        LowerIsWorse = rule.LowerIsWorse,
                    };
                    _episodes[id] = episode;
                }
                else
                {
                    bool worse = rule.LowerIsWorse ? value < episode.Peak : value > episode.Peak;
                    if (worse) episode.Peak = value;
                    episode.DisplayName = sample.Definition.DisplayName;
                    episode.Unit = sample.Definition.Unit;
                    episode.Threshold = rule.Crit;
                    episode.LowerIsWorse = rule.LowerIsWorse;
                }

                if (!episode.Raised && (nowUtc - episode.CritSinceUtc).TotalSeconds >= HoldSeconds)
                {
                    var evt = new AlertEvent(_localClock(), id, episode.DisplayName, episode.Unit, episode.Peak, episode.Threshold, episode.LowerIsWorse);
                    raised.Add(evt);
                    episode.Raised = true;
                    episode.RaisedAtLocal = evt.RaisedAtLocal;
                }
            }
            else if (episode is not null)
            {
                // Leaving Crit ends the episode and re-arms it (a later re-entry starts a brand new Episode).
                EndEpisode(id, episode, nowUtc);
            }
        }

        // Metrics that stopped being monitored entirely (absent from this tick) just drop their state — not the
        // same as "left Crit", so no EpisodeEnded fires.
        foreach (var id in _episodes.Keys.Where(id => !seen.Contains(id)).ToList())
            _episodes.Remove(id);

        return raised;
    }

    private void EndEpisode(string id, Episode episode, DateTime nowUtc)
    {
        _episodes.Remove(id);
        EpisodeEnded?.Invoke(id, episode.RaisedAtLocal, nowUtc - episode.CritSinceUtc);
    }
}
