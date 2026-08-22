using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Presents several readers as one. Identity (Name/IsDegraded) is the primary's; values are merged.</summary>
public sealed class CompositeSensorReader : ISensorReader
{
    private readonly ISensorReader[] _readers;
    private IReadOnlyList<MetricDefinition>? _definitions;

    public CompositeSensorReader(ISensorReader primary, params ISensorReader[] others)
    {
        _readers = new[] { primary }.Concat(others).ToArray();
    }

    public string Name => _readers[0].Name;
    public bool IsDegraded => _readers[0].IsDegraded;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        if (_definitions is not null) return _definitions;
        var all = new List<MetricDefinition>();
        foreach (var r in _readers) all.AddRange(r.Discover());
        _definitions = all;
        return _definitions;
    }

    public SensorSnapshot Read()
    {
        var values = new Dictionary<string, float?>();
        foreach (var r in _readers)
        {
            SensorSnapshot snap;
            try { snap = r.Read(); }
            catch (Exception ex)
            {
                // that reader's ids are absent this tick; others still report
                System.Diagnostics.Trace.WriteLine($"[Stats.CompositeSensorReader] {r.Name} Read failed: {ex.Message}");
                continue;
            }
            foreach (var (id, v) in snap.Values) values[id] = v;
        }
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    public void Dispose()
    {
        foreach (var r in _readers)
        {
            try { r.Dispose(); }
            catch (Exception ex)
            {
                // best effort
                System.Diagnostics.Trace.WriteLine($"[Stats.CompositeSensorReader] {r.Name} Dispose failed: {ex.Message}");
            }
        }
    }
}
