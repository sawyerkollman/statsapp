namespace Stats.Core.Processes;

public static class ProcessGrouping
{
    public static IReadOnlyList<ProcessGroup> Group(IReadOnlyList<ProcessUsage> usages, IReadOnlyDictionary<int, float>? gpuPercentByPid)
    {
        var groups = new Dictionary<string, List<ProcessUsage>>(StringComparer.OrdinalIgnoreCase);
        foreach (var usage in usages)
        {
            if (!groups.TryGetValue(usage.Name, out var group)) groups[usage.Name] = group = new();
            group.Add(usage);
        }
        return groups.Values.Select(group => new ProcessGroup(
            group[0].Name,
            group.Count,
            Sum(group.Select(x => x.CpuPercent)),
            gpuPercentByPid is null ? null : (float)Math.Clamp(group.Sum(x => gpuPercentByPid.TryGetValue(x.Pid, out var gpu) ? gpu : 0f), 0, 100),
            group.Sum(x => x.WorkingSetBytes),
            group.Sum(x => x.PrivateBytes))).ToList();
    }

    private static float? Sum(IEnumerable<float?> values)
    {
        var known = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return known.Count == 0 ? null : (float)Math.Clamp(known.Sum(), 0, 100);
    }
}
