using Stats.Core.Settings;
using Stats.Core.ViewModels;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Covers the tile-resize-by-drag preview substates (docs/superpowers/specs/2026-09-11-tile-resize-design.md
/// "Preview harness"): <c>layout-resized</c> and <c>layout-resize-grip</c>. Built the same way
/// <see cref="DashboardLayoutSubstateTests"/> covers the dashboard-layout-modes substates — no window, straight
/// through <see cref="PreviewComposition.Build"/>, since <c>layout-resized</c> is pure view-model/settings state.
/// <c>layout-resize-grip</c> itself needs a real visual tree (its focus-and-render behavior is verified instead by
/// opening the actual capture PNG, per PREVIEW_HARNESS.md's contract) — what's checked here is its catalog
/// membership, matching how the dashboard-layout-modes tests treat <c>tile-menu</c>-style visual substates.</summary>
public class TileResizeSubstateTests : IDisposable
{
    private readonly string _root;

    public TileResizeSubstateTests()
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
    public void SubstateCatalog_AcceptsBothTileResizeSubstates_ForDashboard()
    {
        SubstateCatalog.Validate("dashboard", new[] { "layout-resized" });
        SubstateCatalog.Validate("dashboard", new[] { "layout-free", "layout-resize-grip" });
    }

    [Theory]
    [InlineData("picker")]
    [InlineData("settings")]
    [InlineData("fans")]
    public void SubstateCatalog_RejectsTileResizeSubstates_ForOtherViews(string view)
    {
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "layout-resized" }));
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate(view, new[] { "layout-resize-grip" }));
    }

    /// <summary>"dense"'s seed pack (Scenarios.Dense) cycles TileSize S, M, L, S, M, L, … across its Tile() calls, so
    /// the first three tiles (cpu.temp.tctl / cpu.power.package / cpu.power.tdc — the same three
    /// DashboardLayoutSubstateTests.LayoutFreePlaced_* repositions) start at S / M / L respectively.
    /// ApplyLayoutResized resizes them to M / L / S — one S-to-M, one to L, one to S, exactly as the design's
    /// "Preview harness" section specifies.</summary>
    [Fact]
    public void LayoutResized_SeedsEveryTilePosition_AppliesTheThreeSizes_AndExtendsTheCanvas()
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { "layout-resized" });

        Assert.Equal(DashboardLayoutMode.Free, c.Dashboard.LayoutMode);
        Assert.NotEmpty(c.Dashboard.Tiles);
        foreach (var tile in c.Dashboard.Tiles)
        {
            var pref = c.Settings.TilePrefs[tile.Definition.Id];
            Assert.NotNull(pref.X);
            Assert.NotNull(pref.Y);
        }

        var tiles = c.Dashboard.Tiles;
        Assert.True(tiles.Count >= 3);
        Assert.Equal("cpu.temp.tctl", tiles[0].Definition.Id);
        Assert.Equal("cpu.power.package", tiles[1].Definition.Id);
        Assert.Equal("cpu.power.tdc", tiles[2].Definition.Id);
        Assert.Equal(TileSize.M, tiles[0].Size); // S -> M
        Assert.Equal(TileSize.L, tiles[1].Size); // M -> L
        Assert.Equal(TileSize.S, tiles[2].Size); // L -> S

        double maxRight = 0, maxBottom = 0;
        foreach (var tile in tiles)
        {
            maxRight = Math.Max(maxRight, tile.X + tile.Width);
            maxBottom = Math.Max(maxBottom, tile.Y + tile.Height);
        }
        Assert.True(c.Dashboard.CanvasWidth >= maxRight + DashboardLayout.Gap - 0.01);
        Assert.True(c.Dashboard.CanvasHeight >= maxBottom + DashboardLayout.Gap - 0.01);
    }

    [Fact]
    public void IsResizable_TrueForEveryTileInFree_FalseAfterSwitchingBackToAuto()
    {
        var c = PreviewComposition.Build("dense", NewSubRoot(), new[] { "layout-resized" });

        Assert.NotEmpty(c.Dashboard.Tiles);
        foreach (var tile in c.Dashboard.Tiles) Assert.True(tile.IsResizable);

        c.Dashboard.LayoutMode = DashboardLayoutMode.Auto;
        foreach (var tile in c.Dashboard.Tiles) Assert.False(tile.IsResizable);
    }
}
