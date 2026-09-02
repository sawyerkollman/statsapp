using Stats.Core.Sensors;

namespace Stats.Core.Metrics;

/// <summary>Holds one MetricHistory per known metric. UI reads from here; poller writes via Apply.</summary>
public sealed class MetricStore
{
    private readonly Dictionary<string, MetricHistory> _histories = new();

    public IReadOnlyList<MetricDefinition> Definitions { get; }

    public MetricStore(IEnumerable<MetricDefinition> definitions, int capacity = 120)
    {
        Definitions = definitions.ToList();
        foreach (var d in Definitions)
            _histories[d.Id] = new MetricHistory(capacity);
    }

    public MetricHistory this[string id] => _histories[id];

    public bool TryGet(string id, out MetricHistory history) => _histories.TryGetValue(id, out history!);

    public void Apply(SensorSnapshot snapshot)
    {
        foreach (var (id, history) in _histories)
            history.Add(snapshot.Values.TryGetValue(id, out var v) ? v : null, snapshot.TimestampUtc);
    }

    public void ResizeAll(int capacity)
    {
        foreach (var history in _histories.Values) history.Resize(capacity);
    }

    public void ResetSession()
    {
        foreach (var history in _histories.Values) history.ResetSession();
    }
}
