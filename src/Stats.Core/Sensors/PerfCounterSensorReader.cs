using System.Diagnostics;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Windows performance-counter fallback used when the LHM driver can't initialize.</summary>
public sealed class PerfCounterSensorReader : ISensorReader
{
    private readonly List<(MetricDefinition Def, PerformanceCounter Counter)> _counters = new();
    private readonly List<MetricDefinition> _definitions = new();
    private PerformanceCounter? _memAvailable;
    private MetricDefinition? _memUsedDef;
    private readonly double _totalMemoryGb =
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1_000_000_000.0;

    public string Name => "Performance Counters (degraded)";
    public bool IsDegraded => true;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        TryAdd(() =>
        {
            Add(new MetricDefinition("cpu.perf.load.total", "CPU Total", MetricGroup.Cpu, "CPU", "%"),
                new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true));
            for (int i = 0; i < Environment.ProcessorCount; i++)
                Add(new MetricDefinition($"cpu.perf.load.core{i}", $"CPU Core #{i}", MetricGroup.Cpu, "CPU", "%"),
                    new PerformanceCounter("Processor", "% Processor Time", i.ToString(), readOnly: true));
        });

        TryAdd(() =>
        {
            _memAvailable = new PerformanceCounter("Memory", "Available Bytes", readOnly: true);
            _memUsedDef = new MetricDefinition("mem.perf.used", "Memory Used", MetricGroup.Memory, "Memory", "GB", "F1");
            _definitions.Add(_memUsedDef);
        });

        TryAdd(() =>
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            foreach (var instance in category.GetInstanceNames().Where(n => n != "_Total").OrderBy(n => n))
                Add(new MetricDefinition($"disk.perf.{SensorMapper.Slug(instance)}.activity", $"{instance} · Activity",
                        MetricGroup.Storage, instance, "%"),
                    new PerformanceCounter("PhysicalDisk", "% Disk Time", instance, readOnly: true));
        });

        TryAdd(() =>
        {
            var category = new PerformanceCounterCategory("Network Interface");
            foreach (var instance in category.GetInstanceNames().OrderBy(n => n))
            {
                Add(new MetricDefinition($"net.perf.{SensorMapper.Slug(instance)}.down", $"{instance} · Download",
                        MetricGroup.Network, instance, "B/s"),
                    new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, readOnly: true));
                Add(new MetricDefinition($"net.perf.{SensorMapper.Slug(instance)}.up", $"{instance} · Upload",
                        MetricGroup.Network, instance, "B/s"),
                    new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, readOnly: true));
            }
        });

        return _definitions;
    }

    public SensorSnapshot Read()
    {
        var values = new Dictionary<string, float?>(_counters.Count + 1);
        foreach (var (def, counter) in _counters)
        {
            try { values[def.Id] = counter.NextValue(); }
            catch { values[def.Id] = null; }
        }
        if (_memAvailable is not null && _memUsedDef is not null)
        {
            try { values[_memUsedDef.Id] = (float)(_totalMemoryGb - _memAvailable.NextValue() / 1_000_000_000.0); }
            catch { values[_memUsedDef.Id] = null; }
        }
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    private void Add(MetricDefinition def, PerformanceCounter counter)
    {
        _counters.Add((def, counter));
        _definitions.Add(def);
    }

    private static void TryAdd(Action action)
    {
        try { action(); }
        catch { /* category unavailable on this machine — skip it */ }
    }

    public void Dispose()
    {
        foreach (var (_, counter) in _counters) counter.Dispose();
        _memAvailable?.Dispose();
    }
}
