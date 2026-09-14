using Stats.Core.Settings;
using Stats.Core.ViewModels;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Covers the three dashboard-layout-modes substates (docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md
/// "Preview harness"): <c>layout-free</c>, <c>layout-grid</c>, <c>layout-free-placed</c>. Built the same way
/// <see cref="CompositionIsolationTests"/> covers the fan substates — no window, straight through
/// <see cref="PreviewComposition.Build"/> — since these substates are pure view-model/settings state (no popup or
/// visual-tree work, unlike "tile-menu").</summary>
public class DashboardLayoutSubstateTests : IDisposable
{
    private readonly string _root;

    public DashboardLayoutSubstateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Fact]
    public void SubstateCatalog_AcceptsAllThreeLayoutSubstates_ForDashboard()
    {
        SubstateCatalog.Validate("dashboard", new[] { "layout-free", "layout-grid", "layout-free-placed" });
    }

    [Theory]
    [InlineData("picker")]
    [InlineData("settings")]
    [InlineData("fans")]
    public void SubstateCatalog_RejectsLayoutSubstates_ForOtherViews(string view)
    {
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "layout-free" }));
    }

    [Fact]
    public void LayoutFree_SetsFreeMode_AndSeedsEveryTileAndTheCoreMatrixBlock()
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { "layout-free" });

        Assert.Equal(DashboardLayoutMode.Free, c.Dashboard.LayoutMode);
        Assert.True(c.Dashboard.IsGridLayout == false);
        Assert.NotEmpty(c.Dashboard.Tiles);
        foreach (var tile in c.Dashboard.Tiles)
        {
            var pref = c.Settings.TilePrefs[tile.Definition.Id];
            Assert.NotNull(pref.X);
            Assert.NotNull(pref.Y);
        }

        // "dense" has a full 16-core matrix, so the seed pack places the block first at (0,0).
        Assert.NotNull(c.Dashboard.CoreMatrix);
        Assert.Equal(0d, c.Dashboard.CoreMatrixX);
        Assert.Equal(0d, c.Dashboard.CoreMatrixY);
    }

    [Fact]
    public void LayoutGrid_SetsGridMode_AndEverySeededPositionIsSnapped()
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { "layout-grid" });

        Assert.Equal(DashboardLayoutMode.Grid, c.Dashboard.LayoutMode);
        Assert.True(c.Dashboard.IsGridLayout);
        foreach (var tile in c.Dashboard.Tiles)
        {
            Assert.Equal(0d, tile.X % DashboardLayout.GridSize);
            Assert.Equal(0d, tile.Y % DashboardLayout.GridSize);
        }

        // S8: the substate only asserted the VM's pre-SizeChanged state, which the app never actually renders —
        // the core-matrix block always reports its measured size once laid out (DashboardWindow.xaml.cs's
        // CoreMatrixBlock_SizeChanged -> SetCoreMatrixSize), and in Grid layout that shift must re-snap (B1b).
        // Simulate the view reporting the block's real size and re-assert every position is still on-grid.
        c.Dashboard.SetCoreMatrixSize(300, 188);
        foreach (var tile in c.Dashboard.Tiles)
        {
            Assert.Equal(0d, tile.X % DashboardLayout.GridSize);
            Assert.Equal(0d, tile.Y % DashboardLayout.GridSize);
        }
    }

    [Fact]
    public void LayoutFreePlaced_HasOneDeliberatelyOverlappingPair_AndAMovedCoreMatrixBlock()
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { "layout-free-placed" });

        Assert.Equal(DashboardLayoutMode.Free, c.Dashboard.LayoutMode);
        Assert.True(c.Dashboard.Tiles.Count >= 3);

        // The first two tiles' bounding boxes intersect (offset by less than a tile) — overlap is allowed in
        // Free/Snap, and the offset keeps both visible in the capture.
        var first = c.Dashboard.Tiles[0];
        var second = c.Dashboard.Tiles[1];
        Assert.True(second.X < first.X + first.Width && first.X < second.X + second.Width);
        Assert.True(second.Y < first.Y + first.Height && first.Y < second.Y + second.Height);

        // Every tile still has a position (seeded or explicit) — nothing was left unplaced.
        foreach (var tile in c.Dashboard.Tiles)
        {
            var pref = c.Settings.TilePrefs[tile.Definition.Id];
            Assert.NotNull(pref.X);
            Assert.NotNull(pref.Y);
        }

        // The core-matrix block was dragged off its seeded (0,0) spot.
        Assert.NotNull(c.Dashboard.CoreMatrix);
        Assert.NotEqual(0d, c.Dashboard.CoreMatrixX);
        Assert.NotEqual(0d, c.Dashboard.CoreMatrixY);
    }

    public static IEnumerable<object[]> LayoutSubstates => new[]
    {
        new object[] { "layout-free" }, new object[] { "layout-grid" }, new object[] { "layout-free-placed" },
    };

    [Theory]
    [MemberData(nameof(LayoutSubstates))]
    public void EveryLayoutSubstate_ProducesAPositiveCanvasExtent(string substate)
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { substate });
        Assert.True(c.Dashboard.CanvasWidth > 0);
        Assert.True(c.Dashboard.CanvasHeight > 0);
    }
}
