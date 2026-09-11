using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class SampleAxisTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(9)]
    public void X_FullBuffer_MatchesOldFormula(int index)
    {
        const int count = 10, capacity = 10;
        const double left = 20, width = 100;
        double expected = left + width * index / (count - 1);
        Assert.Equal(expected, SampleAxis.X(index, count, capacity, left, width), 10);
    }

    [Fact]
    public void X_HalfFullBuffer_RightAligns_LastSampleAtRightEdge()
    {
        const int count = 5, capacity = 20;
        const double left = 0, width = 200;
        Assert.Equal(left + width, SampleAxis.X(count - 1, count, capacity, left, width), 10);
    }

    [Fact]
    public void X_HalfFullBuffer_SpacingUsesCapacityMinusOne()
    {
        const int count = 5, capacity = 20;
        const double left = 0, width = 200;
        double expectedSpacing = width / (capacity - 1);
        double last = SampleAxis.X(count - 1, count, capacity, left, width);
        double secondToLast = SampleAxis.X(count - 2, count, capacity, left, width);
        Assert.Equal(expectedSpacing, last - secondToLast, 10);
    }

    [Fact]
    public void X_CapacityLessThanCount_DegradesToFullBufferFormula()
    {
        // Buffer was just resized smaller than its current sample count.
        const int count = 10, capacity = 4;
        const double left = 0, width = 90;
        for (int i = 0; i < count; i++)
        {
            double expected = left + width * i / (count - 1);
            Assert.Equal(expected, SampleAxis.X(i, count, capacity, left, width), 10);
        }
    }

    [Fact]
    public void X_CountOne_PutsSampleAtRightEdge()
    {
        const double left = 10, width = 50;
        Assert.Equal(left + width, SampleAxis.X(0, 1, 30, left, width), 10);
    }

    [Fact]
    public void X_CountZero_CapacityOne_DoesNotDivideByZero()
    {
        // Degenerate but must not throw or return NaN/Infinity.
        double x = SampleAxis.X(0, 0, 1, 0, 100);
        Assert.False(double.IsNaN(x));
        Assert.False(double.IsInfinity(x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(9)]
    public void IndexAt_FullBuffer_RoundTripsAgainstX(int index)
    {
        const int count = 10, capacity = 10;
        const double left = 20, width = 100;
        double x = SampleAxis.X(index, count, capacity, left, width);
        Assert.Equal(index, SampleAxis.IndexAt(x, count, capacity, left, width));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void IndexAt_HalfFullBuffer_RoundTripsAgainstX(int index)
    {
        const int count = 5, capacity = 20;
        const double left = 0, width = 200;
        double x = SampleAxis.X(index, count, capacity, left, width);
        Assert.Equal(index, SampleAxis.IndexAt(x, count, capacity, left, width));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void IndexAt_CapacityLessThanCount_RoundTripsAgainstX(int index)
    {
        const int count = 10, capacity = 4;
        const double left = 0, width = 90;
        double x = SampleAxis.X(index, count, capacity, left, width);
        Assert.Equal(index, SampleAxis.IndexAt(x, count, capacity, left, width));
    }

    [Fact]
    public void IndexAt_ClampsToValidRange()
    {
        const int count = 5, capacity = 20;
        const double left = 0, width = 200;
        Assert.Equal(0, SampleAxis.IndexAt(left - 1000, count, capacity, left, width));
        Assert.Equal(count - 1, SampleAxis.IndexAt(left + width + 1000, count, capacity, left, width));
    }
}
