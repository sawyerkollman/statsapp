using System.Diagnostics;

namespace Stats.Core.Fans;

/// <summary>
/// Sanitizes the software min/max a control reports. Some Super-I/O and USB controllers report NaN,
/// an inverted pair, or a zero maximum; feeding those straight into <see cref="Math.Clamp(float,float,float)"/>
/// throws (min &gt; max) or would let the controller drive a fan to a hard 0 %.
/// </summary>
public static class FanRange
{
    /// <summary>Returns a usable 0–100 percentage range: a nonsense pair falls back to 0–100,
    /// an otherwise valid pair is clamped into 0–100.</summary>
    public static (float Min, float Max) Sanitize(float min, float max)
    {
        if (float.IsNaN(min) || float.IsNaN(max) || !(max > min) || max <= 0f)
        {
            Trace.WriteLine($"[Stats.FanRange] bad control range {min}–{max}, using 0–100");
            min = 0f;
            max = 100f;
        }
        min = Math.Clamp(min, 0f, 100f);
        max = Math.Clamp(max, 0f, 100f);
        if (!(max > min)) { min = 0f; max = 100f; }
        return (min, max);
    }
}
