using Stats.Core.Refresh;

namespace Stats.Core.Tests;

public class RefreshCoalescerTests
{
    [Fact]
    public void TryPost_FirstCall_ReturnsTrue()
    {
        var c = new RefreshCoalescer();
        Assert.True(c.TryPost());
    }

    [Fact]
    public void TryPost_WhilePending_ReturnsFalse()
    {
        var c = new RefreshCoalescer();
        Assert.True(c.TryPost());
        Assert.False(c.TryPost());
        Assert.False(c.TryPost()); // repeated arrivals while a refresh is in flight all coalesce
    }

    [Fact]
    public void TryPost_AfterTake_ReturnsTrueAgain()
    {
        var c = new RefreshCoalescer();
        Assert.True(c.TryPost());
        c.Take();
        Assert.True(c.TryPost());
    }

    [Fact]
    public void Take_WithoutPriorPost_IsHarmless()
    {
        var c = new RefreshCoalescer();
        c.Take(); // never posted — must not throw or leave the latch in a bad state
        Assert.True(c.TryPost());
    }

    [Fact]
    public void Post_Take_Post_Take_Cycle_NeverGetsStuck()
    {
        var c = new RefreshCoalescer();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(c.TryPost());
            Assert.False(c.TryPost());
            c.Take();
        }
    }
}
