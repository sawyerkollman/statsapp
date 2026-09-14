namespace Stats.Core.Metrics;

/// <summary>Static, allocation-free maths shared by the Histogram tile: bin a metric's history buffer into equal-
/// width bins over its finite range, and compute a nearest-rank percentile of the same buffer for the marker.
/// Modelled on <see cref="SampleAxis"/>/<see cref="CurveSmoothing"/>: no LINQ, no allocation, pure functions over
/// caller-supplied spans.</summary>
public static class HistogramBinning
{
    public const int DefaultBinCount = 12;

    /// <summary>Clears <paramref name="counts"/>, then counts every finite sample of <paramref name="samples"/>
    /// into <paramref name="counts"/>.Length equal-width bins over [min, max] (min/max = the finite min/max of
    /// <paramref name="samples"/>). Returns the finite sample count. Non-finite samples are skipped entirely (they widen
    /// neither the range nor any bin). No finite sample → returns 0, <paramref name="min"/> = <paramref name="max"/>
    /// = NaN, every count stays zero. A degenerate range (max − min &lt; 1e-6f) puts every finite sample in
    /// counts[counts.Length / 2] (min and max both report the single value). Otherwise
    /// bin = min(counts.Length − 1, (int)((v − min) / (max − min) * counts.Length)), so v == max always falls in
    /// the last bin. counts.Length &lt; 1 → <see cref="ArgumentException"/>.</summary>
    public static int Bin(ReadOnlySpan<float> samples, Span<int> counts, out float min, out float max)
    {
        if (counts.Length < 1) throw new ArgumentException("counts must have at least one bin.", nameof(counts));

        counts.Clear();

        min = float.PositiveInfinity;
        max = float.NegativeInfinity;
        int finite = 0;
        foreach (var v in samples)
        {
            if (!float.IsFinite(v)) continue;
            finite++;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (finite == 0)
        {
            min = float.NaN;
            max = float.NaN;
            return 0;
        }

        double range = (double)max - min;
        if (range < 1e-6f)
        {
            counts[counts.Length / 2] += finite;
            max = min;
            return finite;
        }

        foreach (var v in samples)
        {
            if (!float.IsFinite(v)) continue;
            int bin = (int)(((double)v - min) / range * counts.Length);
            if (bin >= counts.Length) bin = counts.Length - 1;
            counts[bin]++;
        }

        return finite;
    }

    /// <summary>Nearest-rank percentile of the finite samples in <paramref name="samples"/>: copies them into
    /// <paramref name="scratch"/>[0..n), sorts that slice ascending, returns
    /// scratch[clamp(ceil(p · n) − 1, 0, n − 1)] — the same rank rule as
    /// <see cref="Frames.FrameStatsAggregator"/>.Snapshot's 1% low. Returns NaN when n == 0 (no finite sample). p
    /// outside [0, 1] (including NaN), or scratch shorter than samples, throws <see cref="ArgumentException"/>. Allocation-free
    /// when the caller reuses <paramref name="scratch"/> across calls.</summary>
    public static float Percentile(ReadOnlySpan<float> samples, double p, Span<float> scratch)
    {
        if (double.IsNaN(p) || p is < 0 or > 1) throw new ArgumentException("p must be within [0, 1].", nameof(p));
        if (scratch.Length < samples.Length) throw new ArgumentException("scratch must be at least as long as samples.", nameof(scratch));

        int n = 0;
        foreach (var v in samples)
        {
            if (float.IsFinite(v)) scratch[n++] = v;
        }

        if (n == 0) return float.NaN;

        var sorted = scratch[..n];
        sorted.Sort();

        int idx = (int)Math.Ceiling(p * n) - 1;
        idx = Math.Clamp(idx, 0, n - 1);
        return sorted[idx];
    }

    /// <summary>Position of <paramref name="value"/> within [<paramref name="min"/>, <paramref name="max"/>] as
    /// 0..1 (clamped); 0.5 when the range is degenerate (max − min &lt; 1e-6f); NaN when any input is non-finite.</summary>
    public static double Fraction(float value, float min, float max)
    {
        if (!float.IsFinite(value) || !float.IsFinite(min) || !float.IsFinite(max)) return double.NaN;

        double range = (double)max - min;
        if (range < 1e-6f) return 0.5;

        double fraction = ((double)value - min) / range;
        return Math.Clamp(fraction, 0.0, 1.0);
    }
}
