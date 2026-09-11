using System.Globalization;

namespace Stats.Core.Metrics;

public static class ValueFormatter
{
    /// <summary>Same rules as <see cref="Format"/>, kept as separate value/unit strings (v1.8 UI-polish §4:
    /// the design forbids splitting a formatted string on spaces to recover the unit). Missing → ("—", "").</summary>
    public static (string Value, string Unit) FormatParts(MetricDefinition def, float? value)
    {
        if (value is not float v || float.IsNaN(v)) return ("—", "");
        if (def.Unit == "B/s")
        {
            if (v >= 1_000_000f) return ((v / 1_000_000f).ToString("F1", CultureInfo.InvariantCulture), "MB/s");
            if (v >= 1_000f) return ((v / 1_000f).ToString("F1", CultureInfo.InvariantCulture), "KB/s");
            return (v.ToString("F0", CultureInfo.InvariantCulture), "B/s");
        }
        return (v.ToString(def.Format, CultureInfo.InvariantCulture), def.Unit);
    }

    public static string Format(MetricDefinition def, float? value)
    {
        var (val, unit) = FormatParts(def, value);
        return unit.Length == 0 ? val : $"{val} {unit}";
    }
}
