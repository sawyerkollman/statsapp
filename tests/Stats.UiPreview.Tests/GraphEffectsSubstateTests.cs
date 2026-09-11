using Stats.UiPreview;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>T3 of docs/superpowers/plans/2026-09-11-graph-effects.md: the harness's "graphs-plain"/"graphs-effects"/
/// "graphs-warmup" (dashboard) and "detail-plain"/"detail-smooth" (details) substates, tested the same way the
/// other settings-level substates in <see cref="CompositionIsolationTests"/> are — building a composition (no
/// window, no WPF) and asserting on <see cref="PreviewComposition.Settings"/>/the store it produced. GraphStyle
/// itself (the static flag pair CaptureHost applies from these settings) is App-only UI state exercised by the
/// controls it drives, not retestable headlessly here.</summary>
public class GraphEffectsSubstateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Fact]
    public void GraphsPlainSubstate_TurnsBothSettingsOff()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "graphs-plain" });
        Assert.False(c.Settings.SmoothLines);
        Assert.False(c.Settings.GraphEffects);
    }

    [Fact]
    public void GraphsEffectsSubstate_TurnsBothSettingsOn()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "graphs-effects" });
        Assert.True(c.Settings.SmoothLines);
        Assert.True(c.Settings.GraphEffects);
    }

    [Fact]
    public void NoGraphsSubstate_DefaultsBothSettingsOn()
    {
        // AppSettings.SmoothLines/GraphEffects both default true — "graphs-effects" is documentary, not the only
        // way to get this look.
        var c = PreviewComposition.Build("normal", NewSubRoot(), Array.Empty<string>());
        Assert.True(c.Settings.SmoothLines);
        Assert.True(c.Settings.GraphEffects);
    }

    [Theory]
    [InlineData("detail-plain", false)]
    [InlineData("detail-smooth", true)]
    public void DetailSubstates_ToggleTheSameSettingsAsTheDashboardOnes(string substate, bool expected)
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { substate });
        Assert.Equal(expected, c.Settings.SmoothLines);
        Assert.Equal(expected, c.Settings.GraphEffects);
    }

    [Fact]
    public void GraphsWarmupSubstate_FillsHistoryBufferToOneQuarterOfCapacity()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "graphs-warmup" });
        var def = c.Definitions.First(d => c.Settings.DashboardMetrics.Contains(d.Id));
        Assert.True(c.Store.TryGet(def.Id, out var history));
        // Review S2: store capacity now matches the fixture's tick count (60) instead of a hardcoded 120, so
        // every non-warmup capture is a full buffer; "graphs-warmup" still sits at exactly 25% of that capacity.
        Assert.Equal(60, history.Capacity);
        Assert.Equal(15, history.ToArray().Length); // 25% of the 60-sample capacity
        Assert.NotNull(history.Current); // still lands on the fixture's exact "current" value
    }

    [Fact]
    public void NoWarmupSubstate_FillsHistoryBufferToAllSixtyFixtureTicks()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), Array.Empty<string>());
        var def = c.Definitions.First(d => c.Settings.DashboardMetrics.Contains(d.Id));
        Assert.True(c.Store.TryGet(def.Id, out var history));
        Assert.Equal(60, history.ToArray().Length);
        Assert.Equal(60, history.Capacity); // full buffer — capacity == sample count (review S2)
    }

    [Fact]
    public void GraphsWarmupCombinedWithGraphsPlain_AppliesBoth()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "graphs-warmup", "graphs-plain" });
        Assert.False(c.Settings.SmoothLines);
        Assert.False(c.Settings.GraphEffects);
        var def = c.Definitions.First(d => c.Settings.DashboardMetrics.Contains(d.Id));
        Assert.True(c.Store.TryGet(def.Id, out var history));
        Assert.Equal(15, history.ToArray().Length);
    }

    [Theory]
    [InlineData("dashboard", "graphs-plain")]
    [InlineData("dashboard", "graphs-effects")]
    [InlineData("dashboard", "graphs-warmup")]
    [InlineData("details", "detail-plain")]
    [InlineData("details", "detail-smooth")]
    [InlineData("details", "detail-warmup")]
    public void SubstateCatalog_AcceptsTheNewSubstatesForTheirView(string view, string substate)
    {
        SubstateCatalog.Validate(view, new[] { substate }); // throws on failure
    }

    [Fact]
    public void DetailWarmupSubstate_FillsHistoryBufferToOneQuarterOfCapacity()
    {
        // Details-view counterpart of GraphsWarmupSubstate_FillsHistoryBufferToOneQuarterOfCapacity — same
        // store-level trim, so HistoryChart (fed from the same MetricStore) shows the same 25%-full buffer.
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "detail-warmup" });
        var def = c.Definitions.First(d => c.Settings.DashboardMetrics.Contains(d.Id));
        Assert.True(c.Store.TryGet(def.Id, out var history));
        Assert.Equal(60, history.Capacity);
        Assert.Equal(15, history.ToArray().Length); // 25% of the 60-sample capacity
    }
}
