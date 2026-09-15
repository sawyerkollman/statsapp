using Stats.Core.Frames;
using Stats.Core.Settings;
using Stats.Core.ViewModels;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Task 4 of docs/superpowers/plans/2026-09-11-game-tiles.md: the harness's "game" scenario and its
/// "game-tiles"/"game-tiles-large"/"game-tiles-small"/"game-summary-orphan" dashboard substates
/// (docs/superpowers/specs/2026-09-11-game-tiles-design.md's "Preview harness"), tested the same
/// composition-only, no-WPF-window way every other substate test in this project uses (pattern of
/// <see cref="GraphEffectsSubstateTests"/>). The existing <c>FixtureValidityTests</c>/<c>CompositionIsolationTests</c>
/// theories already cover "game" via <see cref="Scenarios.Names"/>.</summary>
public class GameTilesSubstateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Fact]
    public void SubstateCatalog_AcceptsGameSubstates_ForDashboard()
    {
        SubstateCatalog.Validate("dashboard", new[] { "game-tiles", "game-tiles-large", "game-tiles-small", "game-summary-orphan" });
    }

    [Theory]
    [InlineData("details")]
    [InlineData("peaks")]
    [InlineData("fans")]
    [InlineData("settings")]
    public void SubstateCatalog_RejectsGameSubstates_ForOtherViews(string view)
    {
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "game-tiles" }));
    }

    [Fact]
    public void GameScenario_PresetsFpsSummaryAndHistogramPrefs()
    {
        var c = PreviewComposition.Build("game", NewSubRoot(), Array.Empty<string>());
        Assert.Equal(TileKind.FpsSummary, c.Settings.PrefFor(FrameMetrics.FpsId).Kind);
        Assert.Equal(TileKind.Histogram, c.Settings.PrefFor(FrameMetrics.FrameTimeId).Kind);
        Assert.Equal(TileKind.Auto, c.Settings.PrefFor(FrameMetrics.LowId).Kind);

        var fps = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FpsId);
        var frameTime = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FrameTimeId);
        var low = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.LowId);
        Assert.Equal(TileKind.FpsSummary, fps.Kind);
        Assert.Equal(TileKind.Histogram, frameTime.Kind);
        Assert.Equal(TileKind.Sparkline, low.Kind); // Auto resolves to Sparkline, unchanged
    }

    [Fact]
    public void GameScenario_FrameTimeSeriesHasSpikes_AndP99AboveBaseline()
    {
        var c = PreviewComposition.Build("game", NewSubRoot(), Array.Empty<string>());
        var frameTime = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FrameTimeId);

        Assert.StartsWith("p99", frameTime.HistogramMarkerText, StringComparison.Ordinal);
        Assert.NotEqual("", frameTime.HistogramMinText);
        Assert.NotEqual("", frameTime.HistogramMaxText);
        // The fixture's baseline noise sits around 6.9 ms with spikes up to 17.6 ms, so max must read well above min.
        double min = double.Parse(frameTime.HistogramMinText.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        double max = double.Parse(frameTime.HistogramMaxText.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(max > min);
    }

    [Fact]
    public void GameTilesSubstate_OnMissing_AddsFrameTime_AndShowsDashes()
    {
        var c = PreviewComposition.Build("missing", NewSubRoot(), new[] { "game-tiles" });

        Assert.Contains(FrameMetrics.FrameTimeId, c.Settings.DashboardMetrics);
        var fps = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FpsId);
        var frameTime = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FrameTimeId);
        Assert.Equal(TileKind.FpsSummary, fps.Kind);
        Assert.Equal(TileKind.Histogram, frameTime.Kind);

        // "missing" never gives fps.avg a sibling series for fps.low1/fps.frametime, and fps.avg itself ends in a
        // full gap — every summary slot reads the dash, and the histogram tile has no finite sample to bin.
        Assert.Equal("—", fps.FpsLowText);
        Assert.Equal("—", fps.FpsFrameTimeText);
        Assert.Equal("", frameTime.HistogramMinText);
        Assert.Equal("", frameTime.HistogramMaxText);
        Assert.Equal("", frameTime.HistogramMarkerText);
        Assert.True(double.IsNaN(frameTime.HistogramMarkerFraction));
    }

    [Theory]
    [InlineData("game-tiles-large", TileSize.L)]
    [InlineData("game-tiles-small", TileSize.S)]
    public void GameTilesLargeAndSmall_SetSizes(string substate, TileSize expected)
    {
        var c = PreviewComposition.Build("game", NewSubRoot(), new[] { substate });
        var fps = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FpsId);
        var frameTime = c.Dashboard.Tiles.Single(t => t.Definition.Id == FrameMetrics.FrameTimeId);
        Assert.Equal(expected, fps.Size);
        Assert.Equal(expected, frameTime.Size);
        Assert.Equal(TileKind.FpsSummary, fps.Kind);
        Assert.Equal(TileKind.Histogram, frameTime.Kind);
    }

    [Fact]
    public void GameSummaryOrphan_OnGallery_SetsFpsSummary_WithDashSlots()
    {
        var c = PreviewComposition.Build("gallery", NewSubRoot(), new[] { "game-summary-orphan" });
        var tile = c.Dashboard.Tiles.Single(t => t.Definition.Id == "gallery.inverted");
        Assert.Equal(TileKind.FpsSummary, c.Settings.PrefFor("gallery.inverted").Kind);
        Assert.Equal(TileKind.FpsSummary, tile.Kind);
        Assert.Equal("—", tile.FpsLowText);
        Assert.Equal("—", tile.FpsFrameTimeText);
        Assert.Equal(0f, tile.FpsLowRatio);
        Assert.Equal("", tile.FpsRatioText);
    }

    [Fact]
    public void GameScenario_GameSectionIsLast_AndHasThreeTiles()
    {
        var c = PreviewComposition.Build("game", NewSubRoot(), Array.Empty<string>());
        var lastSection = c.Dashboard.Sections.Last();
        Assert.Equal(Stats.Core.Metrics.MetricGroup.Game, lastSection.Group);
        Assert.Equal(3, lastSection.Tiles.Count);
    }
}
