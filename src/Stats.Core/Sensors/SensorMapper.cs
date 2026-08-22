using System.Text;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

public static class SensorMapper
{
    /// <summary>Maps each raw sensor; result list is parallel to input (null = sensor not exposed). Ids are unique.</summary>
    public static IReadOnlyList<MetricDefinition?> MapAll(IReadOnlyList<RawSensor> sensors)
    {
        var used = new HashSet<string>();
        var result = new List<MetricDefinition?>(sensors.Count);
        foreach (var raw in sensors)
        {
            var def = Map(raw);
            if (def is null) { result.Add(null); continue; }
            var id = def.Id;
            int n = 2;
            while (!used.Add(id)) id = $"{def.Id}-{n++}";
            result.Add(def with { Id = id });
        }
        return result;
    }

    public static MetricDefinition? Map(RawSensor raw)
    {
        MetricGroup? group = raw.HardwareType switch
        {
            "Cpu" => MetricGroup.Cpu,
            "GpuNvidia" or "GpuAmd" or "GpuIntel" => MetricGroup.Gpu,
            "Memory" => MetricGroup.Memory,
            "Storage" => MetricGroup.Storage,
            "Network" => MetricGroup.Network,
            "Motherboard" or "SuperIO" => MetricGroup.Motherboard,
            "Cooler" => MetricGroup.Cooler,
            _ => null,
        };
        if (group is null) return null;

        (string Unit, string Format)? kind = raw.SensorType switch
        {
            "Temperature" => ("°C", "F1"),
            "Clock" => ("MHz", "F0"),
            "Load" => ("%", "F0"),
            "Power" => ("W", "F1"),
            "Voltage" => ("V", "F3"),
            "Current" => ("A", "F1"),
            "Fan" => ("RPM", "F0"),
            "Control" => ("%", "F0"),
            "Throughput" => ("B/s", "F0"),
            "Data" => ("GB", "F1"),
            "SmallData" => ("MB", "F0"),
            "Level" => ("%", "F0"),
            _ => null,
        };
        if (kind is null) return null;

        bool multiInstance = group is MetricGroup.Gpu or MetricGroup.Storage or MetricGroup.Network;
        string display = multiInstance ? $"{raw.HardwareName} · {raw.SensorName}" : raw.SensorName;
        string id = $"{Slug(group.Value.ToString())}.{Slug(raw.HardwareName)}.{Slug(raw.SensorType)}.{Slug(raw.SensorName)}";
        return new MetricDefinition(id, display, group.Value, raw.HardwareName, kind.Value.Unit, kind.Value.Format);
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool lastDash = false;
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().TrimEnd('-');
    }
}
