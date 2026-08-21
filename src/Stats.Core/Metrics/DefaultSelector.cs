namespace Stats.Core.Metrics;

/// <summary>Builds the out-of-box metric selections from whatever sensors were discovered.</summary>
public static class DefaultSelector
{
    public static List<string> DashboardDefaults(IReadOnlyList<MetricDefinition> defs)
    {
        var picks = new List<string?>
        {
            FirstId(defs, MetricGroup.Cpu, "°C", "tctl", "package"),
            FirstId(defs, MetricGroup.Cpu, "%", "total"),
            FirstId(defs, MetricGroup.Cpu, "W", "package"),
            defs.FirstOrDefault(d => d.Group == MetricGroup.Cpu &&
                d.DisplayName.Contains("ppt", StringComparison.OrdinalIgnoreCase))?.Id,
            FirstId(defs, MetricGroup.Gpu, "°C", "core"),
            FirstId(defs, MetricGroup.Gpu, "MHz", "core"),
            FirstId(defs, MetricGroup.Gpu, "W", "package", "power"),
            FirstId(defs, MetricGroup.Memory, "%", "memory")
                ?? defs.FirstOrDefault(d => d.Group == MetricGroup.Memory)?.Id,
        };

        foreach (var hw in defs.Where(d => d.Group == MetricGroup.Storage).Select(d => d.HardwareName).Distinct())
        {
            var perDisk = defs.Where(d => d.Group == MetricGroup.Storage && d.HardwareName == hw && d.Unit == "%").ToList();
            picks.Add(perDisk.FirstOrDefault(d => d.DisplayName.Contains("activity", StringComparison.OrdinalIgnoreCase))?.Id
                      ?? perDisk.FirstOrDefault()?.Id);
        }

        foreach (var hw in defs.Where(d => d.Group == MetricGroup.Network).Select(d => d.HardwareName).Distinct())
        {
            picks.Add(defs.FirstOrDefault(d => d.Group == MetricGroup.Network && d.HardwareName == hw && d.Unit == "B/s" &&
                (d.DisplayName.Contains("download", StringComparison.OrdinalIgnoreCase) ||
                 d.DisplayName.Contains("received", StringComparison.OrdinalIgnoreCase)))?.Id);
        }

        return picks.Where(p => p is not null).Cast<string>().Distinct().ToList();
    }

    public static List<string> OverlayDefaults(IReadOnlyList<MetricDefinition> defs) =>
        new[]
        {
            FirstId(defs, MetricGroup.Cpu, "°C", "tctl", "package"),
            FirstId(defs, MetricGroup.Gpu, "°C", "core"),
            FirstId(defs, MetricGroup.Gpu, "MHz", "core"),
        }.Where(p => p is not null).Cast<string>().Distinct().ToList();

    private static string? FirstId(IReadOnlyList<MetricDefinition> defs, MetricGroup group, string unit, params string[] preferredNameParts)
    {
        var candidates = defs.Where(d => d.Group == group && d.Unit == unit).ToList();
        foreach (var part in preferredNameParts)
        {
            var hit = candidates.FirstOrDefault(d => d.DisplayName.Contains(part, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Id;
        }
        return candidates.FirstOrDefault()?.Id;
    }
}
