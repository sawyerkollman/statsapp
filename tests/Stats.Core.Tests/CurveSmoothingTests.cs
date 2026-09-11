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
