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
}
