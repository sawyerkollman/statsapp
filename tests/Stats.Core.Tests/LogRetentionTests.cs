using Stats.Core.Diagnostics;

namespace Stats.Core.Tests;

public class LogRetentionTests
{
    [Fact]
    public void SelectFilesToPrune_FewerThanKeep_ReturnsEmpty()
    {
        var names = new[] { "stats-20260101.log", "stats-20260102.log" };

        var result = LogRetention.SelectFilesToPrune(names, keep: 7);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectFilesToPrune_ExactlyKeep_ReturnsEmpty()
    {
        var names = Enumerable.Range(1, 7).Select(d => $"stats-2026010{d}.log").ToList();

        var result = LogRetention.SelectFilesToPrune(names, keep: 7);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectFilesToPrune_MoreThanKeep_ReturnsOldestExcess()
    {
        // Deliberately out of order — the method must sort before deciding what's oldest.
        var names = new[]
        {
            "stats-20260105.log",
            "stats-20260101.log",
            "stats-20260103.log",
            "stats-20260102.log",
            "stats-20260104.log",
        };

        var result = LogRetention.SelectFilesToPrune(names, keep: 3);

        Assert.Equal(new[] { "stats-20260101.log", "stats-20260102.log" }, result);
    }

    [Fact]
    public void SelectFilesToPrune_DuplicateNames_TreatedAsOneFile()
    {
        var names = new[] { "stats-20260101.log", "stats-20260101.log", "stats-20260102.log" };

        var result = LogRetention.SelectFilesToPrune(names, keep: 1);

        Assert.Equal(new[] { "stats-20260101.log" }, result);
    }

    [Fact]
    public void SelectFilesToPrune_NegativeKeep_TreatedAsZero()
    {
        var names = new[] { "stats-20260101.log", "stats-20260102.log" };

        var result = LogRetention.SelectFilesToPrune(names, keep: -1);

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), result);
    }

    [Fact]
    public void SelectFilesToPrune_Empty_ReturnsEmpty()
    {
        var result = LogRetention.SelectFilesToPrune(Array.Empty<string>(), keep: 7);

        Assert.Empty(result);
    }
}
