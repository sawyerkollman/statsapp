using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

public interface ISensorReader : IDisposable
{
    string Name { get; }
    /// <summary>True when running on the WMI/PerfCounter fallback (no temps/clocks/power).</summary>
    bool IsDegraded { get; }
    IReadOnlyList<MetricDefinition> Discover();
    SensorSnapshot Read();
}
