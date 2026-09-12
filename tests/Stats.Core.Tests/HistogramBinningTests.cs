using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class HistogramBinningTests
{
    [Fact]
    public void Bin_TwelveBins_CountsSumToFiniteCount()
    {
        float[] samples = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, float.NaN, 5.5f };
        Span<int> counts = stackalloc int[HistogramBinning.DefaultBinCount];

        int finite = HistogramBinning.Bin(samples, counts, out _, out _);

        Assert.Equal(11, finite);
        int sum = 0;
        foreach (var c in counts) sum += c;
        Assert.Equal(11, sum);
    }

    [Fact]
    public void Bin_IgnoresNaN()
    {
        float[] samples = { float.NaN, 1f, 2f, float.NaN, 3f };
        Span<int> counts = stackalloc int[HistogramBinning.DefaultBinCount];

        int finite = HistogramBinning.Bin(samples, counts, out var min, out var max);

        Assert.Equal(3, finite);
        Assert.Equal(1f, min);
        Assert.Equal(3f, max);
        int sum = 0;
        foreach (var c in counts) sum += c;
        Assert.Equal(3, sum);
    }

    [Fact]
    public void Bin_MaxValueLandsInLastBin_MinInFirst()
    {
        float[] samples = { 0f, 100f };
        Span<int> counts = stackalloc int[HistogramBinning.DefaultBinCount];

        HistogramBinning.Bin(samples, counts, out var min, out var max);

        Assert.Equal(0f, min);
        Assert.Equal(100f, max);
        Assert.Equal(1, counts[0]);
        Assert.Equal(1, counts[^1]);
    }

    [Fact]
    public void Bin_DegenerateRange_AllSamplesInMiddleBin_MinEqualsMax()
    {
        float[] samples = { 7f, 7f, 7f };
        Span<int> counts = stackalloc int[HistogramBinning.DefaultBinCount];

        int finite = HistogramBinning.Bin(samples, counts, out var min, out var max);

        Assert.Equal(3, finite);
        Assert.Equal(min, max);
        Assert.Equal(7f, min);
        Assert.Equal(3, counts[counts.Length / 2]);
        int sum = 0;
        foreach (var c in counts) sum += c;
        Assert.Equal(3, sum); // every sample went into exactly the middle bin
    }

    [Fact]
    public void Bin_AllNaN_ReturnsZero_NaNRange_ZeroCounts()
    {
        float[] samples = { float.NaN, float.NaN };
        Span<int> counts = stackalloc int[HistogramBinning.DefaultBinCount];

        int finite = HistogramBinning.Bin(samples, counts, out var min, out var max);

        Assert.Equal(0, finite);
        Assert.True(float.IsNaN(min));
        Assert.True(float.IsNaN(max));
        foreach (var c in counts) Assert.Equal(0, c);
    }

    [Fact]
    public void Bin_EmptyCounts_Throws()
    {
        float[] samples = { 1f, 2f };
        Assert.Throws<ArgumentException>(() => HistogramBinning.Bin(samples, Span<int>.Empty, out _, out _));
    }

    [Fact]
    public void Percentile_NearestRank_MatchesFrameStatsAggregatorRule()
    {
        var samples = new float[100];
        for (int i = 0; i < 100; i++) samples[i] = i + 1; // 1..100
        var scratch = new float[100];

        Assert.Equal(99f, HistogramBinning.Percentile(samples, 0.99, scratch));
        Assert.Equal(1f, HistogramBinning.Percentile(samples, 0.01, scratch));
        Assert.Equal(50f, HistogramBinning.Percentile(samples, 0.5, scratch));
    }

    [Fact]
    public void Percentile_SingleSample_ReturnsIt()
    {
        float[] samples = { 42f };
        var scratch = new float[1];
        Assert.Equal(42f, HistogramBinning.Percentile(samples, 0.99, scratch));
    }

    [Fact]
    public void Percentile_IgnoresNaN()
    {
        float[] samples = { float.NaN, 1f, 2f, 3f, float.NaN };
        var scratch = new float[5];
        Assert.Equal(3f, HistogramBinning.Percentile(samples, 0.99, scratch));
    }

    [Fact]
    public void Percentile_NoFinite_ReturnsNaN()
    {
        float[] samples = { float.NaN, float.NaN };
        var scratch = new float[2];
        Assert.True(float.IsNaN(HistogramBinning.Percentile(samples, 0.99, scratch)));
    }

    [Fact]
    public void Percentile_ScratchTooSmall_Throws()
    {
        float[] samples = { 1f, 2f, 3f };
        var scratch = new float[2];
        Assert.Throws<ArgumentException>(() => HistogramBinning.Percentile(samples, 0.5, scratch));
    }

    [Fact]
    public void Percentile_POutsideZeroToOne_Throws()
    {
        float[] samples = { 1f, 2f };
        var scratch = new float[2];
        Assert.Throws<ArgumentException>(() => HistogramBinning.Percentile(samples, -0.1, scratch));
        Assert.Throws<ArgumentException>(() => HistogramBinning.Percentile(samples, 1.1, scratch));
    }

    [Fact]
    public void Fraction_MinIsZero_MaxIsOne_Clamped_DegenerateIsHalf_NaNIsNaN()
    {
        Assert.Equal(0.0, HistogramBinning.Fraction(0f, 0f, 10f));
        Assert.Equal(1.0, HistogramBinning.Fraction(10f, 0f, 10f));
        Assert.Equal(0.5, HistogramBinning.Fraction(5f, 0f, 10f));
        Assert.Equal(0.0, HistogramBinning.Fraction(-5f, 0f, 10f));  // clamped below range
        Assert.Equal(1.0, HistogramBinning.Fraction(15f, 0f, 10f));  // clamped above range
        Assert.Equal(0.5, HistogramBinning.Fraction(7f, 7f, 7f));    // degenerate range
        Assert.True(double.IsNaN(HistogramBinning.Fraction(float.NaN, 0f, 10f)));
    }
}
