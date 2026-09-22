namespace Stats.Core.Processes;

public sealed class ProcessCpuTracker
{
    private readonly Dictionary<int, ProcessSample> _previous = new();
    private DateTime? _previousTimestamp;

    public ProcessCpuTracker(int processorCount)
    {
        if (processorCount <= 0) throw new ArgumentOutOfRangeException(nameof(processorCount));
        ProcessorCount = processorCount;
    }

    public int ProcessorCount { get; }

    public IReadOnlyList<ProcessUsage> Apply(ProcessSourceSnapshot snapshot)
    {
        var elapsed = _previousTimestamp is { } previous ? (snapshot.TimestampUtc - previous).TotalSeconds : 0;
        var usages = new List<ProcessUsage>(snapshot.Processes.Count);
        foreach (var sample in snapshot.Processes)
        {
            float? cpu = null;
            if (elapsed > 0 && _previous.TryGetValue(sample.Pid, out var before) && before.TotalProcessorTime <= sample.TotalProcessorTime)
                cpu = (float)Math.Clamp((sample.TotalProcessorTime - before.TotalProcessorTime).TotalSeconds / (elapsed * ProcessorCount) * 100, 0, 100);
            usages.Add(new(sample.Pid, sample.Name, cpu, sample.WorkingSetBytes, sample.PrivateBytes));
        }
        _previous.Clear();
        foreach (var sample in snapshot.Processes) _previous[sample.Pid] = sample;
        _previousTimestamp = snapshot.TimestampUtc;
        return usages;
    }

    public void Reset()
    {
        _previous.Clear();
        _previousTimestamp = null;
    }
}
