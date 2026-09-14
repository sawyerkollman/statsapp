using Stats.Core.Settings;
using Stats.UiPreview;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Task 3 of docs/superpowers/plans/2026-09-11-overlay-sparklines.md: the harness's five new overlay
/// substates (docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md "Preview harness") —
/// <c>sparklines</c>, <c>sparklines-off</c>, <c>sparklines-warmup</c>, <c>status-line</c>, <c>status-line-warn</c>.
/// Built the same way <see cref="GraphEffectsSubstateTests"/> covers its substates — no window, straight through
/// <see cref="PreviewComposition.Build"/> — since these substates are pure view-model/settings state (status is
/// injected through the real <c>OverlayStatusComposer</c>, not a fake).</summary>
public class OverlaySparklinesSubstateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("sparklines")]
    [InlineData("sparklines-off")]
    [InlineData("sparklines-warmup")]
    [InlineData("status-line")]
    [InlineData("status-line-warn")]
    public void SubstateCatalog_AcceptsTheNewOverlaySubstates(string substate)
    {
        SubstateCatalog.Validate("overlay", new[] { substate }); // throws on failure
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("settings")]
    [InlineData("fans")]
    public void SubstateCatalog_RejectsThemForDashboard(string view)
    {
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "sparklines" }));
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "status-line-warn" }));
    }

    [Fact]
    public void NoSubstate_DefaultsSparklinesOnAndStatusLineOff()
    {
        // AppSettings.OverlayGraphs defaults to Sparkline and OverlayStatusLine defaults to false (Task 1) — "sparklines"
        // is documentary, not the only way to get this look, same as "graphs-effects" for the dashboard.
        var c = PreviewComposition.Build("normal", NewSubRoot(), Array.Empty<string>());
        Assert.Equal(OverlayGraphs.Sparkline, c.Settings.OverlayGraphs);
        Assert.True(c.Overlay.ShowSparklines);
        Assert.False(c.Settings.OverlayStatusLine);
        Assert.False(c.Overlay.ShowStatusLine);
        Assert.False(c.Overlay.HasStatus);
    }

    [Fact]
    public void SparklinesOff_SetsNoneAndClearsVmFlag()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "sparklines-off" });
        Assert.Equal(OverlayGraphs.None, c.Settings.OverlayGraphs);
        Assert.False(c.Overlay.ShowSparklines);
    }

    [Fact]
    public void StatusLine_ComposesInformationalText()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "status-line" });
        Assert.True(c.Settings.OverlayStatusLine);
        Assert.True(c.Overlay.ShowStatusLine);
        Assert.Equal("Fans: Balanced · Game mode: gaming (Balanced since 14:30)", c.Overlay.StatusText);
        Assert.False(c.Overlay.StatusIsWarning);
        Assert.True(c.Overlay.HasStatus);
    }

    [Fact]
    public void StatusLineWarn_ComposesWarningText()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "status-line-warn" });
        Assert.True(c.Settings.OverlayStatusLine);
        Assert.Equal("Fans: Custom — write failed · Game mode: desktop · PresentMon: access denied (simulated)", c.Overlay.StatusText);
        Assert.True(c.Overlay.StatusIsWarning);
        Assert.True(c.Overlay.HasStatus);
    }

    [Fact]
    public void SparklinesWarmup_TrimsOverlayHistoryToOneQuarter()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "sparklines-warmup" });
        var def = c.Definitions.First(d => c.Settings.OverlayMetrics.Contains(d.Id));
        Assert.True(c.Store.TryGet(def.Id, out var history));
        Assert.Equal(60, history.Capacity); // full fixture tick count (review S2 of graph-effects, same store)
        Assert.Equal(15, history.ToArray().Length); // 25% of the 60-sample capacity
        Assert.NotNull(history.Current); // still lands on the fixture's exact "current" value
    }

    [Fact]
    public void VerticalPlusStatusLine_AppliesBoth()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "vertical", "status-line" });
        Assert.Equal(OverlayOrientation.Vertical, c.Settings.OverlayOrientation);
        Assert.Equal(OverlayOrientation.Vertical, c.Overlay.Orientation);
        Assert.True(c.Overlay.HasStatus);
        Assert.False(c.Overlay.StatusIsWarning);
        Assert.Equal("Fans: Balanced · Game mode: gaming (Balanced since 14:30)", c.Overlay.StatusText);
    }
}
