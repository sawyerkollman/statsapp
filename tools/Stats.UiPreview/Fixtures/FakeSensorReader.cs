using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.UiPreview.Fixtures;

/// <summary>Fake <see cref="ISensorReader"/>: never touches LibreHardwareMonitor, PawnIO, or perf counters.
/// <see cref="Discover"/> returns the fixture definitions; captures apply fixture snapshots to the
/// <see cref="Stats.Core.Metrics.MetricStore"/> directly rather than through <see cref="Read"/> — this exists so
/// a composition can prove (and --interactive can use) a real ISensorReader instance that is provably fake.</summary>
public sealed class FakeSensorReader : ISensorReader
{
    private readonly IReadOnlyList<MetricDefinition> _definitions;
    private SensorSnapshot _latest;

    public FakeSensorReader(IReadOnlyList<MetricDefinition> definitions, SensorSnapshot initial, bool degraded = false)
    {
        _definitions = definitions;
        _latest = initial;
        IsDegraded = degraded;
    }

    public string Name => "Preview fixture (simulated)";
    public bool IsDegraded { get; }
    public IReadOnlyList<MetricDefinition> Discover() => _definitions;
    public SensorSnapshot Read() => _latest;

    /// <summary>Used only by --interactive's seeded tick timer.</summary>
    public void SetLatest(SensorSnapshot snapshot) => _latest = snapshot;

    public void Dispose() { /* nothing to release */ }
}
