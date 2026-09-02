using Stats.Core.Startup;

namespace Stats.Core.Tests;

public class StartupArgsTests
{
    [Fact]
    public void HasMinimizedFlag_ExactCase_True()
    {
        Assert.True(StartupArgs.HasMinimizedFlag(new[] { "--minimized" }));
    }

    [Theory]
    [InlineData("--Minimized")]
    [InlineData("--MINIMIZED")]
    [InlineData("--MiNiMiZeD")]
    public void HasMinimizedFlag_AnyCase_True(string arg)
    {
        Assert.True(StartupArgs.HasMinimizedFlag(new[] { arg }));
    }

    [Fact]
    public void HasMinimizedFlag_AmongOtherArgs_True()
    {
        Assert.True(StartupArgs.HasMinimizedFlag(new[] { "--foo", "--minimized", "--bar" }));
    }

    [Fact]
    public void HasMinimizedFlag_Absent_False()
    {
        Assert.False(StartupArgs.HasMinimizedFlag(new[] { "--foo", "--bar" }));
    }

    [Fact]
    public void HasMinimizedFlag_Empty_False()
    {
        Assert.False(StartupArgs.HasMinimizedFlag(Array.Empty<string>()));
    }

    [Fact]
    public void HasMinimizedFlag_SubstringNotExactMatch_False()
    {
        Assert.False(StartupArgs.HasMinimizedFlag(new[] { "--minimized2", "notminimized", "-minimized" }));
    }
}
