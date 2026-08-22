using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class PresentMonCsvParserTests
{
    private const string V2Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,AnimationError";
    private const string V1Header =
        "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed";
    private const string StartOnlyHeader = "Application,ProcessID,CPUStartTime,Other";

    private static PresentMonCsvParser Primed(string header)
    {
        var p = new PresentMonCsvParser();
        Assert.Null(p.Parse(header));
        Assert.True(p.HeaderParsed);
        return p;
    }

    [Fact]
    public void V2_FrameTimeColumn_YieldsPidAndFrameTime()
    {
        var p = Primed(V2Header);
        var s = p.Parse("game.exe,1234,0x000001,DXGI,1,0,0,Hardware: Independent Flip,12.345678,16.667,10.1,6.5,NA,NA,NA,NA,NA,NA,NA");
        Assert.Equal(new FrameSample(1234, 16.667), s);
    }

    [Fact]
    public void V1_msBetweenPresentsColumn_YieldsPidAndFrameTime()
    {
        var p = Primed(V1Header);
        var s = p.Parse("game.exe,42,0x0,DXGI,1,0,0,Composed: Flip,0,1.5,0.2,8.333,5.0,9.1");
        Assert.Equal(new FrameSample(42, 8.333), s);
    }

    [Fact]
    public void HeaderMatching_IsCaseInsensitive()
    {
        var p = Primed("application,processid,MSBETWEENPRESENTS");
        Assert.Equal(new FrameSample(7, 4.0), p.Parse("x.exe,7,4.0"));
    }

    [Fact]
    public void CpuStartTimeFallback_DerivesDeltasPerPid_FirstFrameYieldsNothing()
    {
        var p = Primed(StartOnlyHeader);
        Assert.Null(p.Parse("a.exe,1,100.000,x"));      // first frame for pid 1: no interval yet
        Assert.Null(p.Parse("b.exe,2,100.005,x"));      // first frame for pid 2
        var a = p.Parse("a.exe,1,100.016,x");
        var b = p.Parse("b.exe,2,100.010,x");
        Assert.Equal(1, a!.Value.Pid);
        Assert.Equal(16.0, a.Value.FrameTimeMs, 6);     // (100.016 - 100.000) s → ms, within fp noise
        Assert.Equal(2, b!.Value.Pid);
        Assert.Equal(5.0, b.Value.FrameTimeMs, 6);
        Assert.Equal(0, p.SkippedLines);
    }

    [Theory]
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,NA,NA,NA,NA,NA,NA,NA,NA,NA,NA")]   // NA frame time
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,-3.0,NA,NA,NA,NA,NA,NA,NA,NA,NA")] // negative
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,0,NA,NA,NA,NA,NA,NA,NA,NA,NA")]    // zero
    [InlineData("game.exe,abc,0x1,DXGI,1,0,0,Mode,1.0,16.0,NA,NA,NA,NA,NA,NA,NA,NA,NA")]  // bad pid
    [InlineData("too,short")]
    [InlineData("")]
    public void MalformedDataLines_AreSkippedAndCounted(string line)
    {
        var p = Primed(V2Header);
        Assert.Null(p.Parse(line));
        Assert.Equal(1, p.SkippedLines);
    }

    [Fact]
    public void LeadingBlankLines_BeforeHeader_AreIgnored()
    {
        var p = new PresentMonCsvParser();
        Assert.Null(p.Parse(""));
        Assert.Null(p.Parse("   "));
        Assert.False(p.HeaderParsed);
        Assert.Null(p.Parse(V2Header));
        Assert.True(p.HeaderParsed);
    }

    [Fact]
    public void Header_WithoutProcessId_IsPreamble_ThrowsOnlyWhenTheBudgetRunsOut()
    {
        // No ProcessID field → not a header candidate, so it is treated as preamble, not an immediate error.
        var p = new PresentMonCsvParser();
        for (int i = 0; i < PresentMonCsvParser.MaxPreambleLines; i++)
            Assert.Null(p.Parse("Application,FrameTime"));
        Assert.False(p.HeaderParsed);
        Assert.Equal(PresentMonCsvParser.MaxPreambleLines, p.PreambleLinesSkipped);
        Assert.Throws<PresentMonFormatException>(() => p.Parse("Application,FrameTime"));
    }

    [Fact]
    public void PreambleLines_BeforeHeader_AreSkipped()
    {
        var p = new PresentMonCsvParser();
        Assert.Null(p.Parse("PresentMon 2.5.1"));
        Assert.Null(p.Parse("Capturing all processes..."));
        Assert.False(p.HeaderParsed);
        Assert.Null(p.Parse(V2Header));
        Assert.True(p.HeaderParsed);
        var s = p.Parse("game.exe,1234,0x000001,DXGI,1,0,0,Hardware: Independent Flip,12.345678,16.667,10.1,6.5,NA,NA,NA,NA,NA,NA,NA");
        Assert.Equal(new FrameSample(1234, 16.667), s);
        Assert.Equal(2, p.PreambleLinesSkipped);
        Assert.Equal(0, p.SkippedLines);
    }

    [Fact]
    public void PreambleBudgetExhausted_Throws()
    {
        var p = new PresentMonCsvParser();
        for (int i = 0; i < 20; i++) Assert.Null(p.Parse($"junk line {i}"));
        Assert.Equal(20, p.PreambleLinesSkipped);
        var ex = Assert.Throws<PresentMonFormatException>(() => p.Parse("junk line 20"));
        Assert.Contains("junk line 20", ex.Message);
    }

    [Fact]
    public void HeaderLike_LineWithoutTimingColumn_StillThrows() =>
        Assert.Throws<PresentMonFormatException>(() => new PresentMonCsvParser().Parse("Application,ProcessID,Runtime"));

    [Fact]
    public void Header_WithoutAnyTimingColumn_Throws() =>
        Assert.Throws<PresentMonFormatException>(() => new PresentMonCsvParser().Parse("Application,ProcessID,Runtime"));

    [Fact]
    public void Values_ParseWithInvariantCulture()
    {
        var p = Primed("ProcessID,FrameTime");
        Assert.Equal(new FrameSample(3, 1234.5), p.Parse("3,1234.5"));
    }
}
