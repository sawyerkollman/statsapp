using System.Diagnostics;

namespace Stats.Core.Processes;

public sealed class GpuEngineCounters
{
    public const string CategoryName = "GPU Engine";
    public const string CounterName = "Utilization Percentage";
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(250);
    public const int SlowSamplesToDisable = 3;

    private readonly PerformanceCounterCategory _category = new(CategoryName);
    private readonly SlowSampleGate _slowGate = new(SlowThreshold, SlowSamplesToDisable);
    private Dictionary<string, CounterSample> _previous = new(StringComparer.Ordinal);

    public bool IsAvailable { get; private set; } = true;
    public string? UnavailableReason { get; private set; }

    public IReadOnlyDictionary<int, float>? TryRead()
    {
        if (!IsAvailable) return null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var counters = _category.ReadCategory();
            stopwatch.Stop();
            if (_slowGate.Record(stopwatch.Elapsed)) return Disable("GPU Engine counters too slow (>250 ms)");
            if (!counters.Contains(CounterName)) return Disable("GPU Engine counters missing");
            var instances = counters[CounterName];

            var current = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            var values = new Dictionary<int, float>();
            foreach (InstanceData instance in instances.Values)
            {
                current[instance.InstanceName] = instance.Sample;
                if (_previous.TryGetValue(instance.InstanceName, out var before) && TryParsePid(instance.InstanceName, out var pid))
                {
                    var value = (float)Math.Clamp(CounterSample.Calculate(before, instance.Sample), 0, 100);
                    values[pid] = Math.Min(100, values.GetValueOrDefault(pid) + value);
                }
            }
            _previous = current;
            return values.Count == 0 && _previous.Count > 0 ? null : values;
        }
        catch (Exception ex)
        {
            return Disable(FirstLine(ex.Message));
        }
    }

    public void Reset() => _previous.Clear();

    public static bool TryParsePid(string instanceName, out int pid)
    {
        const string prefix = "pid_";
        pid = 0;
        if (!instanceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var end = instanceName.IndexOf('_', prefix.Length);
        return end > prefix.Length && int.TryParse(instanceName.AsSpan(prefix.Length, end - prefix.Length), out pid) && pid > 0;
    }

    private IReadOnlyDictionary<int, float>? Disable(string reason)
    {
        IsAvailable = false;
        UnavailableReason = reason;
        return null;
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOfAny(['\r', '\n']);
        return i < 0 ? text : text[..i];
    }
}
