namespace Stats.Core.Processes;

public sealed record ProcessSample(int Pid, string Name, TimeSpan TotalProcessorTime, long WorkingSetBytes, long PrivateBytes);

public sealed record ProcessSourceSnapshot(
    IReadOnlyList<ProcessSample> Processes,
    DateTime TimestampUtc,
    bool GpuAvailable,
    IReadOnlyDictionary<int, float>? GpuPercentByPid);

public sealed record ProcessUsage(int Pid, string Name, float? CpuPercent, long WorkingSetBytes, long PrivateBytes);

public sealed record ProcessGroup(string Name, int PidCount, float? CpuPercent, float? GpuPercent, long WorkingSetBytes, long PrivateBytes);

public sealed record ProcessListSnapshot(IReadOnlyList<ProcessGroup> Groups, bool GpuAvailable, DateTime TimestampUtc, string? UnavailableReason = null)
{
    public bool IsUnavailable => UnavailableReason is not null;
    public static ProcessListSnapshot Unavailable(string reason, DateTime timestampUtc) =>
        new(Array.Empty<ProcessGroup>(), false, timestampUtc, reason);
}
