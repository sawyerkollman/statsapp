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
        Assert.Null(h.SessionMinAtUtc);
        Assert.Null(h.SessionMaxAtUtc);
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
    public void Add_Null_AdvancesBufferWithNaNSlot_StatsUnaffected()
    {
        var h = new MetricHistory(4);
        h.Add(5f);
        h.Add(null);
        Assert.Null(h.Current);
        Assert.Equal(5f, h.SessionMin);
        Assert.Equal(5f, h.SessionMax);
        var arr = h.ToArray();
        Assert.Equal(2, arr.Length); // the gap still occupies a slot — x stays uniform in time
        Assert.Equal(5f, arr[0]);
        Assert.True(float.IsNaN(arr[1]));
    }

    [Fact]
    public void Add_NaN_TreatedAsGap_AdvancesBufferWithNaNSlot()
    {
        var h = new MetricHistory(4);
        h.Add(float.NaN);
        Assert.Null(h.Current);
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.True(float.IsNaN(h.SessionMax));
        var arr = h.ToArray();
        Assert.Single(arr);
        Assert.True(float.IsNaN(arr[0]));
    }

    [Fact]
    public void Add_MultipleGaps_EachOccupiesOwnSlot_SessionStatsIgnoreThem()
    {
        var h = new MetricHistory(5);
        h.Add(1f);
        h.Add(null);
        h.Add(float.NaN);
        h.Add(2f);
        var arr = h.ToArray();
        Assert.Equal(new[] { 1f, float.NaN, float.NaN, 2f }, arr);
        Assert.Equal(1f, h.SessionMin);
        Assert.Equal(2f, h.SessionMax);
        Assert.Equal(1.5f, h.SessionAvg);
    }

    [Fact]
    public void Resize_PreservesNaNSlots()
    {
        var h = new MetricHistory(4);
        h.Add(1f); h.Add(null); h.Add(3f);
        h.Resize(5);
        var arr = h.ToArray();
        Assert.Equal(new[] { 1f, float.NaN, 3f }, arr);
        Assert.Equal(1f, h.SessionMin); // session stats unaffected by the gap or the resize
        Assert.Equal(3f, h.SessionMax);
    }

    [Fact]
    public void Add_WithTimestamp_SetsSessionMinMaxAtUtc_OnlyForRealValues()
    {
        var h = new MetricHistory(4);
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(5);
        var t3 = t2.AddSeconds(5);
        h.Add(10f, t1);
        h.Add(20f, t2);
        h.Add(null, t3); // gap must not move either timestamp
        Assert.Equal(t1, h.SessionMinAtUtc);
        Assert.Equal(t2, h.SessionMaxAtUtc);
    }

    [Fact]
    public void Add_NewMinOrMax_UpdatesItsOwnTimestampOnly()
    {
        var h = new MetricHistory(4);
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(5);
        h.Add(20f, t1); // first sample sets both min and max at t1
        h.Add(5f, t2);  // new min at t2; max (still 20) keeps its original timestamp
        Assert.Equal(t2, h.SessionMinAtUtc);
        Assert.Equal(t1, h.SessionMaxAtUtc);
    }

    [Fact]
    public void ResetSession_ClearsSessionMinMaxAtUtc()
    {
        var h = new MetricHistory(4);
        h.Add(10f, DateTime.UtcNow);
        h.ResetSession();
        Assert.Null(h.SessionMinAtUtc);
        Assert.Null(h.SessionMaxAtUtc);
        h.Add(30f, DateTime.UtcNow);
        Assert.NotNull(h.SessionMinAtUtc);
        Assert.NotNull(h.SessionMaxAtUtc);
    }

    [Fact]
    public void Add_WithoutExplicitTimestamp_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var h = new MetricHistory(4);
        h.Add(10f);
        var after = DateTime.UtcNow;
        Assert.NotNull(h.SessionMinAtUtc);
        Assert.InRange(h.SessionMinAtUtc!.Value, before, after);
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
