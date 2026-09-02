using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class MetricHistoryTests
{
    [Fact]
    public void NewHistory_HasNoCurrentAndNaNStats()
    {
        var h = new MetricHistory(4);
        Assert.Null(h.Current);
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.True(float.IsNaN(h.SessionMax));
        Assert.True(float.IsNaN(h.SessionAvg));
        Assert.Empty(h.ToArray());
    }

    [Fact]
    public void Add_UpdatesCurrentAndStats()
    {
        var h = new MetricHistory(4);
        h.Add(10f);
        h.Add(20f);
        Assert.Equal(20f, h.Current);
        Assert.Equal(10f, h.SessionMin);
        Assert.Equal(20f, h.SessionMax);
        Assert.Equal(15f, h.SessionAvg);
        Assert.Equal(new[] { 10f, 20f }, h.ToArray());
    }

    [Fact]
    public void Add_BeyondCapacity_WrapsOldestOut_ButSessionStatsKeepAll()
    {
        var h = new MetricHistory(3);
        h.Add(1f); h.Add(2f); h.Add(3f); h.Add(4f);
        Assert.Equal(new[] { 2f, 3f, 4f }, h.ToArray());
        Assert.Equal(1f, h.SessionMin);   // session min survives buffer eviction
        Assert.Equal(4f, h.SessionMax);
        Assert.Equal(2.5f, h.SessionAvg);
    }

    [Fact]
    public void Add_Null_SetsCurrentNull_DoesNotPolluteStatsOrBuffer()
    {
        var h = new MetricHistory(4);
        h.Add(5f);
        h.Add(null);
        Assert.Null(h.Current);
        Assert.Equal(5f, h.SessionMin);
        Assert.Equal(5f, h.SessionMax);
        Assert.Equal(new[] { 5f }, h.ToArray());
    }

    [Fact]
    public void Add_NaN_TreatedAsGap()
    {
        var h = new MetricHistory(4);
        h.Add(float.NaN);
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.Empty(h.ToArray());
    }

    [Fact]
    public void Resize_Smaller_KeepsNewestSamples()
    {
        var h = new MetricHistory(5);
        for (int i = 1; i <= 5; i++) h.Add(i);
        h.Resize(3);
        Assert.Equal(3, h.Capacity);
        Assert.Equal(new[] { 3f, 4f, 5f }, h.ToArray());
        h.Add(6f);
        Assert.Equal(new[] { 4f, 5f, 6f }, h.ToArray());
        Assert.Equal(1f, h.SessionMin); // session stats untouched
    }

    [Fact]
    public void Resize_Larger_KeepsAllAndContinues()
    {
        var h = new MetricHistory(2);
        h.Add(1f); h.Add(2f); h.Add(3f);
        h.Resize(4);
        Assert.Equal(new[] { 2f, 3f }, h.ToArray());
        h.Add(4f); h.Add(5f);
        Assert.Equal(new[] { 2f, 3f, 4f, 5f }, h.ToArray());
    }

    [Fact]
    public void ResetSession_ClearsBufferAndStats_KeepsCurrent()
    {
        var h = new MetricHistory(4);
        h.Add(10f); h.Add(20f);
        h.ResetSession();
        Assert.Empty(h.ToArray());
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.True(float.IsNaN(h.SessionAvg));
        Assert.Equal(20f, h.Current);
        h.Add(30f);
        Assert.Equal(30f, h.SessionMin);
    }

    [Theory]
    [InlineData(2, 1.0, 120)]
    [InlineData(60, 1.0, 3600)]
    [InlineData(60, 0.5, 3600)]   // capped
    [InlineData(2, 5.0, 30)]      // floor
    [InlineData(5, 0.5, 600)]
    public void HistoryCapacity_Compute(int minutes, double poll, int expected) =>
        Assert.Equal(expected, HistoryCapacity.Compute(minutes, poll));

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m")]
    [InlineData(120, "2m")]
    [InlineData(150, "2m30s")]
    [InlineData(1800, "30m")]
    [InlineData(3600, "60m")]
    public void HistoryCapacity_FormatWindow(double totalSeconds, string expected) =>
        Assert.Equal(expected, HistoryCapacity.FormatWindow(totalSeconds));
}
