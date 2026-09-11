using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class CurveSmoothingTests
{
    [Fact]
    public void MonotoneTangents_MismatchedLengths_Throws()
    {
        double[] xs = { 0, 1, 2 };
        double[] ys = { 0, 1, 2 };
        var tangents = new double[2];
        Assert.Throws<ArgumentException>(() => CurveSmoothing.MonotoneTangents(xs, ys, tangents));
    }

    [Fact]
    public void MonotoneTangents_EmptyRun_NoOp()
    {
        var tangents = Array.Empty<double>();
        CurveSmoothing.MonotoneTangents(Array.Empty<double>(), Array.Empty<double>(), tangents);
        Assert.Empty(tangents);
    }

    [Fact]
    public void MonotoneTangents_SingleSample_TangentIsZero()
    {
        double[] xs = { 5 };
        double[] ys = { 42 };
        var tangents = new double[1];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.Equal(0, tangents[0]);
    }

    [Fact]
    public void MonotoneTangents_TwoSamples_BothTangentsEqualTheSecant()
    {
        double[] xs = { 0, 2 };
        double[] ys = { 0, 6 }; // secant = 3
        var tangents = new double[2];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.Equal(3, tangents[0], 10);
        Assert.Equal(3, tangents[1], 10);
    }

    [Fact]
    public void MonotoneTangents_StrictlyIncreasingData_AllTangentsPositive()
    {
        double[] xs = { 0, 1, 2, 3, 4 };
        double[] ys = { 0, 1, 3, 4, 10 };
        var tangents = new double[5];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.All(tangents, t => Assert.True(t >= 0));
        Assert.True(tangents[0] > 0);
        Assert.True(tangents[^1] > 0);
    }

    [Fact]
    public void MonotoneTangents_FlatRun_YieldsAllZeros()
    {
        double[] xs = { 0, 1, 2, 3 };
        double[] ys = { 5, 5, 5, 5 };
        var tangents = new double[4];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.All(tangents, t => Assert.Equal(0, t, 10));
    }

    [Fact]
    public void MonotoneTangents_ExtremumSample_GetsTangentZero()
    {
        // A local max at index 1: rising then falling — the neighbouring secants change sign there.
        double[] xs = { 0, 1, 2 };
        double[] ys = { 0, 5, 0 };
        var tangents = new double[3];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.Equal(0, tangents[1], 10);
    }

    [Fact]
    public void MonotoneTangents_AlphaBetaClamp_EngagesOnDisparateSecants_KeepsSegmentWithinTheEllipse()
    {
        // Secants 1 and 0.05 differ by 20x: the naive averaged tangent at the shared point (0.525) scaled by the
        // small secant (0.05) blows past alpha^2+beta^2 <= 9 on the second segment, so the clamp must engage.
        double[] xs = { 0, 1, 2 };
        double[] ys = { 0, 1, 1.05 };
        var tangents = new double[3];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);

        double d1 = (ys[2] - ys[1]) / (xs[2] - xs[1]); // 0.05
        double alpha = tangents[1] / d1;
        double beta = tangents[2] / d1;
        Assert.True(alpha * alpha + beta * beta <= 9.0 + 1e-9);
        // And the clamp actually fired: the unclamped average (0.525) would have violated the ellipse.
        Assert.NotEqual(0.525, tangents[1], 6);
        Assert.True(tangents[1] > 0);
        Assert.True(tangents[2] > 0);
    }

    [Fact]
    public void MonotoneTangents_ZeroDx_DoesNotThrowOrNaN()
    {
        // Two samples projected to the same pixel X (a zero-dx segment) — documents the chosen behaviour (review
        // N1): the degenerate secant is treated as 0, which zeroes both of its neighbouring tangents, same as any
        // other flat secant. Unreachable via SampleAxis today (spacing is always > 0) but must not throw or NaN.
        double[] xs = { 0, 1, 1, 2 };
        double[] ys = { 0, 5, 8, 10 };
        var tangents = new double[4];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        Assert.All(tangents, t => Assert.False(double.IsNaN(t)));
        Assert.Equal(0, tangents[1], 10);
        Assert.Equal(0, tangents[2], 10);
    }

    [Fact]
    public void Curve_SpikySeries_NeverOvershootsItsSegmentRange()
    {
        // Review S3: nothing previously evaluated the resulting cubic itself — only tangent properties. Walk the
        // Bézier for every segment of a spiky series at t = 0..1 in 0.02 steps and assert every point stays
        // within that segment's [min(y_k, y_k+1), max(y_k, y_k+1)] — the property the feature actually promises
        // ("a spike still reads as a spike", never overshooting between two samples).
        double[] xs = { 0, 1, 2, 3, 4 };
        double[] ys = { 0, 10, 0.2, 9, 1 };
        var tangents = new double[xs.Length];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);

        for (int k = 0; k < xs.Length - 1; k++)
        {
            var (c1x, c1y, c2x, c2y) = CurveSmoothing.BezierControlPoints(
                xs[k], ys[k], tangents[k], xs[k + 1], ys[k + 1], tangents[k + 1]);
            double lo = Math.Min(ys[k], ys[k + 1]);
            double hi = Math.Max(ys[k], ys[k + 1]);
            for (double t = 0; t <= 1.0; t += 0.02)
            {
                double y = CubicBezier(ys[k], c1y, c2y, ys[k + 1], t);
                Assert.True(y >= lo - 1e-9 && y <= hi + 1e-9,
                    $"segment {k} overshot at t={t}: y={y}, expected within [{lo}, {hi}]");
            }
        }
    }

    private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
    {
        double u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    [Fact]
    public void BezierControlPoints_StraightLine_ControlsAreCollinear()
    {
        var (c1x, c1y, c2x, c2y) = CurveSmoothing.BezierControlPoints(0, 0, 2, 1, 2, 2);
        Assert.Equal(1.0 / 3, c1x, 10);
        Assert.Equal(2.0 / 3, c1y, 10); // on y = 2x
        Assert.Equal(2.0 / 3, c2x, 10);
        Assert.Equal(4.0 / 3, c2y, 10); // on y = 2x
    }

    [Fact]
    public void BezierControlPoints_MatchesFormula()
    {
        var (c1x, c1y, c2x, c2y) = CurveSmoothing.BezierControlPoints(0, 10, 4, 6, 20, -2);
        double h = 6;
        Assert.Equal(0 + h / 3, c1x, 10);
        Assert.Equal(10 + 4 * h / 3, c1y, 10);
        Assert.Equal(6 - h / 3, c2x, 10);
        Assert.Equal(20 - (-2) * h / 3, c2y, 10);
    }
}
