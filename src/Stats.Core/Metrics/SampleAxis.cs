namespace Stats.Core.Metrics;

/// <summary>The fixed time-axis rule shared by Sparkline and HistoryChart: samples are laid out at a constant
/// <c>width / (capacity - 1)</c> spacing anchored to the right edge, rather than stretched across the full width
/// by however many samples currently exist. During warm-up (before the ring buffer fills) the line grows in from
/// the right at a fixed scale instead of visibly stretching every tick; once the buffer is full this is
/// pixel-identical to the old <c>left + width * i / (count - 1)</c> formula.</summary>
public static class SampleAxis
{
    /// <summary>X position (in the same units as <paramref name="left"/>/<paramref name="width"/>) of the sample
    /// at <paramref name="index"/> of <paramref name="count"/> buffered samples in a ring buffer of
    /// <paramref name="capacity"/>. A <paramref name="capacity"/> smaller than <paramref name="count"/> (the
    /// buffer was just resized smaller) is treated as <paramref name="count"/> — i.e. degrades to the full-buffer
    /// formula. <paramref name="count"/> &lt;= 1 puts the sample at <c>left + width</c> (the right edge).</summary>
    public static double X(int index, int count, int capacity, double left, double width)
    {
        capacity = Math.Max(capacity, count);
        double denom = Math.Max(1, capacity - 1);
        return left + width - (count - 1 - index) * width / denom;
    }

    /// <summary>Inverse of <see cref="X"/>: the sample index nearest pixel position <paramref name="x"/>, clamped
    /// to <c>[0, count - 1]</c>. Used by both controls' hover handlers so the crosshair tracks the pointer under
    /// the same fixed-axis rule <see cref="X"/> draws with (review B1) — computing <c>round(px / width * (count -
    /// 1))</c> instead (the old, pre-fixed-axis mapping) detaches the crosshair from the cursor whenever the
    /// buffer isn't full.</summary>
    public static int IndexAt(double x, int count, int capacity, double left, double width)
    {
        capacity = Math.Max(capacity, count);
        double denom = Math.Max(1, capacity - 1);
        double raw = count - 1 - (left + width - x) * denom / width;
        int idx = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        return Math.Clamp(idx, 0, Math.Max(0, count - 1));
    }
}
