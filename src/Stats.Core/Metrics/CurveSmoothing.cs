namespace Stats.Core.Metrics;

/// <summary>Fritsch–Carlson monotone cubic interpolation, operating on already-projected pixel coordinates. The
/// resulting curve passes through every sample and never overshoots between two samples — see the "Monotone
/// cubic interpolation" article on Wikipedia for the standard algorithm this follows. WPF-free: callers (the
/// Sparkline/HistoryChart controls) call <see cref="MonotoneTangents"/> once per finite run after their own
/// X(i)/Y(v) projection and stride decimation, then <see cref="BezierControlPoints"/> per segment.</summary>
public static class CurveSmoothing
{
    /// <summary>Computes one tangent per sample into <paramref name="tangents"/> (same length as
    /// <paramref name="xs"/>/<paramref name="ys"/>, throws <see cref="ArgumentException"/> otherwise). n = 0 is a
    /// no-op (draw nothing); n = 1 yields tangent 0 (draw a dot); n = 2 yields both tangents equal to the single
    /// secant (draw a straight segment).</summary>
    public static void MonotoneTangents(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, Span<double> tangents)
    {
        if (xs.Length != ys.Length || xs.Length != tangents.Length)
            throw new ArgumentException("xs, ys and tangents must all have the same length.");

        int n = xs.Length;
        if (n == 0) return;
        if (n == 1) { tangents[0] = 0; return; }

        // 128 doubles = 1 KiB, comfortably inside the ~1 MiB default thread stack even nested a few calls deep;
        // beyond that (a run longer than 129 samples — larger than any stride-decimated run this codebase
        // produces, see Sparkline/HistoryChart's `stride` calc) fall back to a heap array instead of growing the
        // stackalloc unbounded.
        Span<double> d = n - 1 <= 128 ? stackalloc double[n - 1] : new double[n - 1];
        for (int k = 0; k < n - 1; k++)
        {
            // A zero-dx (two samples projected to the same pixel X) degenerate-secant of 0 forces both
            // neighbouring tangents flat below rather than skipping the segment — unreachable today (SampleAxis
            // spacing is always stride·width/denom > 0, so xs is always strictly increasing) but documented and
            // tested (CurveSmoothingTests.MonotoneTangents_ZeroDx_DoesNotThrowOrNaN) so a future caller that can
            // produce ties knows what happens: no exception, no NaN, just a flat tangent at the tie.
            double dx = xs[k + 1] - xs[k];
            d[k] = dx == 0 ? 0 : (ys[k + 1] - ys[k]) / dx;
        }

        // Initial tangents: one-sided secant at each end, average of the neighbouring secants in between.
        tangents[0] = d[0];
        tangents[n - 1] = d[n - 2];
        for (int k = 1; k < n - 1; k++)
            tangents[k] = (d[k - 1] + d[k]) / 2;

        // Zero the tangent wherever the adjoining secant is flat, or (interior points) the neighbouring secants
        // change sign — both mark a local extremum the curve must not overshoot past.
        for (int k = 0; k < n - 1; k++)
        {
            if (d[k] == 0) { tangents[k] = 0; tangents[k + 1] = 0; }
        }
        for (int k = 1; k < n - 1; k++)
        {
            if (Math.Sign(d[k - 1]) != Math.Sign(d[k])) tangents[k] = 0;
        }

        // Fritsch-Carlson clamp: on every segment, alpha^2 + beta^2 <= 9, where alpha/beta are the endpoint
        // tangents scaled by the segment's secant. Prevents overshoot on segments with very unequal tangents.
        for (int k = 0; k < n - 1; k++)
        {
            if (d[k] == 0) continue;
            double alpha = tangents[k] / d[k];
            double beta = tangents[k + 1] / d[k];
            if (alpha < 0) { tangents[k] = 0; alpha = 0; }
            if (beta < 0) { tangents[k + 1] = 0; beta = 0; }
            double sumSquares = alpha * alpha + beta * beta;
            if (sumSquares > 9)
            {
                double tau = 3 / Math.Sqrt(sumSquares);
                tangents[k] = tau * alpha * d[k];
                tangents[k + 1] = tau * beta * d[k];
            }
        }
    }

    /// <summary>Cubic Bézier control points for the segment running from (x0,y0) with tangent m0 to (x1,y1) with
    /// tangent m1, with h = x1 - x0: C1 = (x0 + h/3, y0 + m0·h/3), C2 = (x1 - h/3, y1 - m1·h/3).</summary>
    public static (double C1X, double C1Y, double C2X, double C2Y) BezierControlPoints(
        double x0, double y0, double m0, double x1, double y1, double m1)
    {
        double h = x1 - x0;
        return (x0 + h / 3, y0 + m0 * h / 3, x1 - h / 3, y1 - m1 * h / 3);
    }
}
