using System.Globalization;

namespace Stats.Core.Metrics;

public static class ValueFormatter
{
    public static string Format(MetricDefinition def, float? value)
    {
        if (value is not float v || float.IsNaN(v)) return "—";
        if (def.Unit == "B/s")
        {
            if (v >= 1_000_000f) return string.Create(CultureInfo.InvariantCulture, $"{v / 1_000_000f:F1} MB/s");
            if (v >= 1_000f) return string.Create(CultureInfo.InvariantCulture, $"{v / 1_000f:F1} KB/s");
            return string.Create(CultureInfo.InvariantCulture, $"{v:F0} B/s");
        }
        return $"{v.ToString(def.Format, CultureInfo.InvariantCulture)} {def.Unit}".TrimEnd();
    }
}
