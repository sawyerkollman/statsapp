using Stats.Core.Fans;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class FanCurveTests
{
    private static FanCurve Make(params (float T, float P)[] pts)
    {
        Assert.True(FanCurve.TryCreate(pts.Select(p => new FanPoint(p.T, p.P)), out var c));
        return c!;
    }

    [Fact]
    public void Default_IsValid_AndMatchesSpec()
    {
        Assert.Equal(new[] { new FanPoint(30, 30), new FanPoint(50, 45), new FanPoint(70, 75), new FanPoint(85, 100) }, FanCurve.DefaultPoints);
        Assert.Equal(FanCurve.DefaultPoints, FanCurve.Default.Points);
    }

    [Theory]
    [InlineData(30f, 30f)]   // exact first point
    [InlineData(40f, 37.5f)] // halfway 30→50 : 30→45
    [InlineData(70f, 75f)]   // exact middle point
    [InlineData(80f, 91.666664f)] // 70→85 : 75→100, 2/3 of the way
    [InlineData(85f, 100f)]
    public void Evaluate_InterpolatesLinearly(float temp, float expected) =>
        Assert.Equal(expected, FanCurve.Default.Evaluate(temp), 3);

    [Fact]
    public void Evaluate_FlatBeyondEnds()
    {
        Assert.Equal(30f, FanCurve.Default.Evaluate(-10f));
        Assert.Equal(100f, FanCurve.Default.Evaluate(150f));
    }

    [Fact]
    public void TryCreate_SortsByTemperature()
    {
        var c = Make((70, 75), (30, 30));
        Assert.Equal(30f, c.Points[0].TempC);
        Assert.Equal(52.5f, c.Evaluate(50f), 3);
    }

    [Fact]
    public void TryCreate_RejectsTooFewTooManyNull()
    {
        Assert.False(FanCurve.TryCreate(null, out _));
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(50, 50) }, out _));
        Assert.False(FanCurve.TryCreate(Enumerable.Range(0, 9).Select(i => new FanPoint(10 + i * 10, 50)), out _));
        Assert.True(FanCurve.TryCreate(Enumerable.Range(0, 8).Select(i => new FanPoint(10 + i * 10, 50)), out _));
    }

    [Theory]
    [InlineData(-1f, 50f)] [InlineData(121f, 50f)] [InlineData(50f, -1f)] [InlineData(50f, 101f)] [InlineData(float.NaN, 50f)]
    public void TryCreate_RejectsOutOfRange(float t, float p) =>
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(20, 20), new FanPoint(t, p) }, out _));

    [Fact]
    public void TryCreate_RejectsDuplicateTemperaturesWithinHalfDegree()
    {
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(50, 20), new FanPoint(50.4f, 60) }, out _));
        Assert.True(FanCurve.TryCreate(new[] { new FanPoint(50, 20), new FanPoint(50.6f, 60) }, out _));
    }
}
