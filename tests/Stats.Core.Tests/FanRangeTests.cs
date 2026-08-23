using Stats.Core.Fans;

namespace Stats.Core.Tests;

public class FanRangeTests
{
    [Theory]
    [InlineData(0f, 100f, 0f, 100f)]      // already sane
    [InlineData(30f, 100f, 30f, 100f)]
    [InlineData(float.NaN, 100f, 0f, 100f)]
    [InlineData(0f, float.NaN, 0f, 100f)]
    [InlineData(80f, 20f, 0f, 100f)]      // inverted
    [InlineData(0f, 0f, 0f, 100f)]        // zero max would let us stop the fan
    [InlineData(-10f, 0f, 0f, 100f)]      // negative max
    [InlineData(50f, 50f, 0f, 100f)]      // no headroom
    [InlineData(-20f, 100f, 0f, 100f)]    // min clamped up to 0
    [InlineData(0f, 255f, 0f, 100f)]      // max clamped down to 100
    [InlineData(-5f, 120f, 0f, 100f)]
    [InlineData(200f, 300f, 0f, 100f)]    // wholly out of range → fallback
    public void Sanitize_ProducesAUsableRange(float min, float max, float expectedMin, float expectedMax)
    {
        var (m, x) = FanRange.Sanitize(min, max);
        Assert.Equal(expectedMin, m);
        Assert.Equal(expectedMax, x);
    }

    [Fact]
    public void Sanitize_AlwaysReturnsAClampableRange()
    {
        foreach (var (min, max) in new[]
                 {
                     (float.NaN, float.NaN), (float.NegativeInfinity, float.PositiveInfinity),
                     (100f, 100f), (0f, 0.5f), (99f, 100f),
                 })
        {
            var (m, x) = FanRange.Sanitize(min, max);
            Assert.True(x > m, $"{min}–{max} → {m}–{x}");
            Assert.InRange(m, 0f, 100f);
            Assert.InRange(x, 0f, 100f);
            Math.Clamp(50f, m, x); // must not throw
        }
    }
}
