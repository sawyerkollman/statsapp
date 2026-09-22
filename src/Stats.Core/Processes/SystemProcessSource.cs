using System.Diagnostics;

namespace Stats.Core.Processes;

public sealed class SystemProcessSource : IProcessSource
{
    private readonly GpuEngineCounters _gpu = new();
    public string Name => "System processes";

    public ProcessSourceSnapshot Sample()
    {
        var samples = new List<ProcessSample>();
        var timestamp = DateTime.UtcNow;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var id = process.Id;
                var name = process.ProcessName;
                if (id is 0 or 4 || name.Equals("Idle", StringComparison.OrdinalIgnoreCase) || name.Equals("System", StringComparison.OrdinalIgnoreCase)) continue;
                samples.Add(new(id, name, process.TotalProcessorTime, process.WorkingSet64, process.PrivateMemorySize64));
            }
            catch (Exception) { }
            finally { process.Dispose(); }
        }
        var gpu = _gpu.TryRead();
        return new(samples, timestamp, _gpu.IsAvailable, gpu);
    }

    public void Reset() => _gpu.Reset();
    public void Dispose() { }
}
