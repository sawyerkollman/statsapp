using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

/// <summary>Task 2 of the game-tiles design: the Histogram/FpsSummary outputs on <see cref="MetricTileViewModel"/>,
/// gated on an optional <see cref="MetricStore"/> constructor argument and the tile's resolved <see cref="Kind"/>.</summary>
public class GameTileViewModelTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");

    [Fact]
    public void Histogram_Refresh_ComputesBins_Range_MarkerFraction_MarkerText()
    {
        var store = new MetricStore(new[] { CpuTemp });
        var history = store["cpu.temp"];
        for (float v = 0; v <= 11; v++) history.Add(v);
        var settings = new AppSettings();
        settings.PrefFor("cpu.temp").Kind = TileKind.Histogram;
        var tile = new MetricTileViewModel(CpuTemp, history, settings, store);

        tile.Refresh();

        Assert.Equal(TileKind.Histogram, tile.Kind);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, tile.HistogramBins);
        Assert.Equal("0.0 °C", tile.HistogramMinText);
        Assert.Equal("11.0 °C", tile.HistogramMaxText);
        Assert.Equal(1.0, tile.HistogramMarkerFraction);
        Assert.Equal("p99 11.0 °C", tile.HistogramMarkerText);
    }

    [Fact]
    public void Histogram_BinsReferenceAlternates_EveryRefresh()
    {
        var store = new MetricStore(new[] { CpuTemp });
        var history = store["cpu.temp"];
        var settings = new AppSettings();
        settings.PrefFor("cpu.temp").Kind = TileKind.Histogram;
        var tile = new MetricTileViewModel(CpuTemp, history, settings, store);

        history.Add(1f);
        tile.Refresh();
        var first = tile.HistogramBins;

        history.Add(2f);
        tile.Refresh();
        var second = tile.HistogramBins;

        history.Add(3f);
        tile.Refresh();
        var third = tile.HistogramBins;

        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Same(first, third); // alternates back to the same slot every other refresh
    }

    [Fact]
    public void Histogram_LowerIsWorseRule_UsesP1_AndLabelsIt()
    {
        var def = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FpsId); // "fps", LowerIsWorse group rule
        var store = new MetricStore(new[] { def });
        var history = store[def.Id];
        for (float v = 0; v <= 11; v++) history.Add(v);
        var settings = new AppSettings { ThresholdRules = ThresholdDefaults.Rules() };
        settings.PrefFor(def.Id).Kind = TileKind.Histogram;
        var tile = new MetricTileViewModel(def, history, settings, store);

        tile.Refresh();

        Assert.Equal(0.0, tile.HistogramMarkerFraction);
        Assert.Equal("p1 0 fps", tile.HistogramMarkerText);
    }

    [Fact]
    public void Histogram_AllGaps_EmptyOutputs()
    {
        var store = new MetricStore(new[] { CpuTemp });
        var history = store["cpu.temp"];
        history.Add(null);
        history.Add(null);
        var settings = new AppSettings();
        settings.PrefFor("cpu.temp").Kind = TileKind.Histogram;
        var tile = new MetricTileViewModel(CpuTemp, history, settings, store);

        tile.Refresh();

        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, tile.HistogramBins);
        Assert.Equal("", tile.HistogramMinText);
        Assert.Equal("", tile.HistogramMaxText);
        Assert.True(double.IsNaN(tile.HistogramMarkerFraction));
        Assert.Equal("", tile.HistogramMarkerText);
    }

    [Fact]
    public void Histogram_OnSparklineKind_OutputsStayDefault()
    {
        var store = new MetricStore(new[] { CpuTemp });
        var history = store["cpu.temp"];
        history.Add(5f);
        var settings = new AppSettings(); // Kind stays Auto -> Sparkline for a non-% Cpu metric
        var tile = new MetricTileViewModel(CpuTemp, history, settings, store);

        tile.Refresh();

        Assert.Equal(TileKind.Sparkline, tile.Kind);
        Assert.Empty(tile.HistogramBins);
        Assert.Equal("", tile.HistogramMinText);
        Assert.Equal("", tile.HistogramMaxText);
        Assert.True(double.IsNaN(tile.HistogramMarkerFraction));
        Assert.Equal("", tile.HistogramMarkerText);
    }

    [Fact]
    public void Histogram_WithoutStore_OutputsStayDefault()
    {
        var history = new MetricHistory();
        history.Add(5f);
        var settings = new AppSettings();
        settings.PrefFor("cpu.temp").Kind = TileKind.Histogram;
        var tile = new MetricTileViewModel(CpuTemp, history, settings); // overlay path: no store

        tile.Refresh();

        Assert.Equal(TileKind.Histogram, tile.Kind); // preferred != Auto still resolves through
        Assert.Empty(tile.HistogramBins);
        Assert.Equal("", tile.HistogramMinText);
        Assert.Equal("", tile.HistogramMaxText);
        Assert.True(double.IsNaN(tile.HistogramMarkerFraction));
        Assert.Equal("", tile.HistogramMarkerText);
    }

    [Fact]
    public void FpsSummary_ResolvesSiblingsByRole_FormatsLowAndFrameTime_RatioAndSeverity()
    {
        var store = new MetricStore(FrameMetrics.Definitions);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        {
            [FrameMetrics.FpsId] = 96f,
            [FrameMetrics.LowId] = 61f,
            [FrameMetrics.FrameTimeId] = 10.4f,
        }, DateTime.UtcNow));
        var avgDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FpsId);
        var settings = new AppSettings { ThresholdOverrides = ThresholdDefaults.Overrides() };
        settings.PrefFor(FrameMetrics.FpsId).Kind = TileKind.FpsSummary;
        var tile = new MetricTileViewModel(avgDef, store[FrameMetrics.FpsId], settings, store);

        tile.Refresh();

        Assert.Equal(TileKind.FpsSummary, tile.Kind);
        Assert.Equal("61 fps", tile.FpsLowText);
        Assert.Equal("10.4 ms", tile.FpsFrameTimeText);
        Assert.Equal(0.635f, tile.FpsLowRatio, 3);
        Assert.Equal("64% of avg", tile.FpsRatioText);
        Assert.Equal(Severity.Normal, tile.FpsLowSeverity);

        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        {
            [FrameMetrics.FpsId] = 96f,
            [FrameMetrics.LowId] = 12f,
            [FrameMetrics.FrameTimeId] = 10.4f,
        }, DateTime.UtcNow));
        tile.Refresh();

        Assert.Equal(Severity.Crit, tile.FpsLowSeverity); // 30/15 LowerIsWorse override on fps.low1
    }

    [Fact]
    public void FpsSummary_MissingSiblings_ReadDash_RatioZero()
    {
        var def = new MetricDefinition("game.fps.solo", "Solo FPS", MetricGroup.Game, "App", "fps", "F0");
        var store = new MetricStore(new[] { def }); // lone Game/"fps" def — no siblings exist at all
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["game.fps.solo"] = 100f }, DateTime.UtcNow));
        var settings = new AppSettings();
        settings.PrefFor(def.Id).Kind = TileKind.FpsSummary;
        var tile = new MetricTileViewModel(def, store[def.Id], settings, store);

        tile.Refresh();

        Assert.Equal(TileKind.FpsSummary, tile.Kind);
        Assert.Equal("—", tile.FpsLowText);
        Assert.Equal("—", tile.FpsFrameTimeText);
        Assert.Equal(0f, tile.FpsLowRatio);
        Assert.Equal("", tile.FpsRatioText);
        Assert.Equal(Severity.Normal, tile.FpsLowSeverity);
    }

    [Fact]
    public void FpsSummary_SiblingGap_ReadsDash()
    {
        var store = new MetricStore(FrameMetrics.Definitions); // siblings exist, but never given a value
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { [FrameMetrics.FpsId] = 96f }, DateTime.UtcNow));
        var avgDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FpsId);
        var settings = new AppSettings();
        settings.PrefFor(FrameMetrics.FpsId).Kind = TileKind.FpsSummary;
        var tile = new MetricTileViewModel(avgDef, store[FrameMetrics.FpsId], settings, store);

        tile.Refresh();

        Assert.Equal("—", tile.FpsLowText);
        Assert.Equal("—", tile.FpsFrameTimeText);
        Assert.Equal(0f, tile.FpsLowRatio);
        Assert.Equal("", tile.FpsRatioText);
    }

    [Fact]
    public void FpsSummary_PrefOnNonFpsRole_ResolvesToSparkline()
    {
        var store = new MetricStore(FrameMetrics.Definitions);
        var lowDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.LowId);
        var lowSettings = new AppSettings();
        lowSettings.PrefFor(FrameMetrics.LowId).Kind = TileKind.FpsSummary;
        var lowTile = new MetricTileViewModel(lowDef, store[FrameMetrics.LowId], lowSettings, store);
        lowTile.Refresh();
        Assert.Equal(TileKind.Sparkline, lowTile.Kind);

        var cpuStore = new MetricStore(new[] { CpuTemp });
        var cpuSettings = new AppSettings();
        cpuSettings.PrefFor("cpu.temp").Kind = TileKind.FpsSummary;
        var cpuTile = new MetricTileViewModel(CpuTemp, cpuStore["cpu.temp"], cpuSettings, cpuStore);
        cpuTile.Refresh();
        Assert.Equal(TileKind.Sparkline, cpuTile.Kind);
    }

    [Fact]
    public void FpsSummary_AutomationLabel_IncludesLowFrameTimeAndRatio()
    {
        var store = new MetricStore(FrameMetrics.Definitions);
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        {
            [FrameMetrics.FpsId] = 96f,
            [FrameMetrics.LowId] = 61f,
            [FrameMetrics.FrameTimeId] = 10.4f,
        }, DateTime.UtcNow));
        var avgDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FpsId);
        var settings = new AppSettings();
        settings.PrefFor(FrameMetrics.FpsId).Kind = TileKind.FpsSummary;
        var tile = new MetricTileViewModel(avgDef, store[FrameMetrics.FpsId], settings, store);

        tile.Refresh();

        Assert.Equal("FPS, 96 fps, Normal, 1% low 61 fps, frame time 10.4 ms, 64% of avg", tile.AutomationLabel);
    }

    [Fact]
    public void RaiseSeverityRefresh_AlsoRaisesFpsLowSeverity()
    {
        var tile = new MetricTileViewModel(CpuTemp, new MetricHistory(), new AppSettings());
        var raised = new List<string>();
        tile.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        tile.RaiseSeverityRefresh();

        Assert.Contains(nameof(MetricTileViewModel.Severity), raised);
        Assert.Contains(nameof(MetricTileViewModel.FpsLowSeverity), raised);
    }

    [Fact]
    public void GameRole_ComputedFromDefinition()
    {
        var fpsDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FpsId);
        var lowDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.LowId);
        var frameTimeDef = FrameMetrics.Definitions.First(d => d.Id == FrameMetrics.FrameTimeId);

        Assert.Equal(GameMetricRole.Fps, new MetricTileViewModel(fpsDef, new MetricHistory(), new AppSettings()).GameRole);
        Assert.Equal(GameMetricRole.LowFps, new MetricTileViewModel(lowDef, new MetricHistory(), new AppSettings()).GameRole);
        Assert.Equal(GameMetricRole.FrameTime, new MetricTileViewModel(frameTimeDef, new MetricHistory(), new AppSettings()).GameRole);
        Assert.Equal(GameMetricRole.None, new MetricTileViewModel(CpuTemp, new MetricHistory(), new AppSettings()).GameRole);
    }
}
