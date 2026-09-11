using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

/// <summary>Task 2 of docs/superpowers/plans/2026-09-11-dashboard-layout-modes.md — DashboardViewModel's Free/Snap
/// canvas: LayoutMode switching, SetTilePosition/SetCoreMatrixPosition/SetCoreMatrixSize, the seed pack
/// (PlaceUnpositioned, run from RebuildSections), ResetPositions, canvas extent, and StatusLines. See
/// docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md's "Core → DashboardViewModel" and
/// "Acceptance" sections for the contract these exercise.</summary>
public class DashboardLayoutModeTests
{
    // Two "Core #n" defs (cpu.l1/cpu.l2) so CoreMatrix.HasCores is true and the core-matrix block participates in
    // the seed pack/extent — mirrors DashboardViewModelTests.Defs.
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.l1", "CPU Core #1", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.l2", "CPU Core #2", MetricGroup.Cpu, "Ryzen", "%"),
        new("gpu.clock", "GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.temp", "GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("disk.c", "SSD-C · Activity", MetricGroup.Storage, "SSD-C", "%"),
    };

    private static (DashboardViewModel Vm, AppSettings S, MetricStore Store, Func<int> Saves) Make(
        DashboardLayoutMode mode, bool showCoreMatrix, params string[] selected)
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings
        {
            DashboardMetrics = selected.ToList(),
            DashboardLayoutMode = mode,
            ShowCoreMatrix = showCoreMatrix,
        };
        int saves = 0;
        var vm = new DashboardViewModel(store, s, () => saves++);
        return (vm, s, store, () => saves);
    }

    // ---- LayoutMode ----

    [Fact]
    public void LayoutMode_InitializesFromSettings_WithoutDoublingTheConstructorRebuild()
    {
        var (vm, _, _, saves) = Make(DashboardLayoutMode.Grid, showCoreMatrix: false, "cpu.temp");
        Assert.Equal(DashboardLayoutMode.Grid, vm.LayoutMode);
        Assert.True(vm.IsGridLayout);
        Assert.False(vm.IsAutoLayout);
        // The constructor assigns the backing field directly (bypassing the LayoutMode property setter's
        // OnLayoutModeChanged hook) before its own single RebuildSections() call — so only the seed pack's own
        // save fires here, not a second one from the hook redundantly wrapping the same rebuild.
        Assert.Equal(1, saves());
    }

    [Fact]
    public void SetLayoutModeCommand_Persists_Rebuilds_AndRaisesModeFlags_SavesOnce()
    {
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Auto, showCoreMatrix: false, "cpu.temp");

        vm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Free);

        Assert.Equal(DashboardLayoutMode.Free, vm.LayoutMode);
        Assert.Equal(DashboardLayoutMode.Free, s.DashboardLayoutMode);
        Assert.True(vm.Sections.Count > 0); // RebuildSections ran again (cheap smoke check it didn't throw/empty)
        // Two saves: OnLayoutModeChanged's own (persists the mode) plus the seed pack's (persists cpu.temp's
        // freshly-seeded position, now that RebuildSections runs in non-Auto layout for the first time).
        Assert.Equal(2, saves());
    }

    [Fact]
    public void AutoMode_NeverWritesPositions_EvenAcrossMultipleRebuilds()
    {
        var (vm, s, _, _) = Make(DashboardLayoutMode.Auto, showCoreMatrix: true, "cpu.temp", "gpu.clock");
        vm.RebuildSections();
        vm.RebuildSections();
        Assert.Empty(s.TilePrefs); // PlaceUnpositioned never even ran — no TilePref entries were created for it
        Assert.Null(s.CoreMatrixX);
        Assert.Null(s.CoreMatrixY);
        Assert.All(vm.Tiles, t => Assert.Equal(0, t.X));
    }

    // ---- SetTilePosition ----

    [Fact]
    public void SetTilePosition_FreeMode_DoesNotSnap()
    {
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        int before = saves(); // construction already seeded+saved this single tile once
        vm.SetTilePosition("cpu.temp", 101, 53);
        Assert.Equal(101, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(53, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(101, vm.Tiles.Single().X);
        Assert.Equal(53, vm.Tiles.Single().Y);
        Assert.Equal(before + 1, saves());
    }

    [Fact]
    public void SetTilePosition_GridMode_SnapsToGridSize()
    {
        var (vm, s, _, _) = Make(DashboardLayoutMode.Grid, showCoreMatrix: false, "cpu.temp");
        vm.SetTilePosition("cpu.temp", 101, 53); // nearest 16: 96, 48
        Assert.Equal(96, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(48, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(96, vm.Tiles.Single().X);
        Assert.Equal(48, vm.Tiles.Single().Y);
    }

    [Fact]
    public void SetTilePosition_ClampsNegativeCoordinates_ToZero_InBothModes()
    {
        var (freeVm, freeS, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        freeVm.SetTilePosition("cpu.temp", -40, -5);
        Assert.Equal(0, freeS.TilePrefs["cpu.temp"].X);
        Assert.Equal(0, freeS.TilePrefs["cpu.temp"].Y);

        var (gridVm, gridS, _, _) = Make(DashboardLayoutMode.Grid, showCoreMatrix: false, "cpu.temp");
        gridVm.SetTilePosition("cpu.temp", -40, -5);
        Assert.Equal(0, gridS.TilePrefs["cpu.temp"].X);
        Assert.Equal(0, gridS.TilePrefs["cpu.temp"].Y);
    }

    [Fact]
    public void SetTilePosition_UnknownId_IsNoOp_NoSave()
    {
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        int before = saves(); // construction already seeded+saved cpu.temp once
        vm.SetTilePosition("nope", 10, 10);
        Assert.DoesNotContain("nope", s.TilePrefs.Keys);
        Assert.Equal(before, saves());
    }

    // ---- SetCoreMatrixPosition / SetCoreMatrixSize ----

    [Fact]
    public void SetCoreMatrixPosition_SnapsClampsAndPersists_SavesOnce()
    {
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Grid, showCoreMatrix: true, "cpu.temp");
        int before = saves(); // construction already seeded+saved the block position (and the tile) once
        vm.SetCoreMatrixPosition(-10, 101); // clamp then snap: 0, 96
        Assert.Equal(0, s.CoreMatrixX);
        Assert.Equal(96, s.CoreMatrixY);
        Assert.Equal(0, vm.CoreMatrixX);
        Assert.Equal(96, vm.CoreMatrixY);
        Assert.Equal(before + 1, saves());
    }

    [Fact]
    public void SetCoreMatrixSize_UpdatesExtent_ButNeverSaves()
    {
        var (vm, _, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp");
        int before = saves();
        vm.SetCoreMatrixSize(300, 200);
        Assert.Equal(before, saves()); // size is never persisted
        // Block now occupies (0,0)-(300,200) (seeded at 0,0 by RebuildSections); extent must grow to cover it.
        Assert.True(vm.CanvasWidth >= 300 + DashboardLayout.Gap);
        Assert.True(vm.CanvasHeight >= 200 + DashboardLayout.Gap);
    }

    // ---- seed pack (PlaceUnpositioned) ----

    [Fact]
    public void SeedPack_PlacesUnplacedTiles_GroupThenUserOrder_BelowTheBlock_NoOverlapWithinRow()
    {
        // No core matrix here (isolates the tile-packing math); block-below case covered separately below.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "gpu.clock", "cpu.temp", "disk.c");
        // Tiles order = GroupOrder (Cpu, Gpu, ..., Storage) then user order within a group: cpu.temp, gpu.clock, disk.c.
        // All default TileSize.M = 224x144; PackWidth = 932 fits all three in one row (0, 236, 472).
        Assert.Equal(new[] { "cpu.temp", "gpu.clock", "disk.c" }, vm.Tiles.Select(t => t.Definition.Id));
        Assert.Equal(0, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(0, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(236, s.TilePrefs["gpu.clock"].X); // 224 width + 12 gap
        Assert.Equal(0, s.TilePrefs["gpu.clock"].Y);
        Assert.Equal(472, s.TilePrefs["disk.c"].X); // 236 + 224 + 12
        Assert.Equal(0, s.TilePrefs["disk.c"].Y);

        // No horizontal overlap: each tile's right edge is <= the next tile's left edge.
        var xs = vm.Tiles.Select(t => t.X).OrderBy(v => v).ToList();
        for (int i = 0; i < xs.Count - 1; i++) Assert.True(xs[i] + 224 <= xs[i + 1]);
    }

    [Fact]
    public void SeedPack_CoreMatrixBlock_PlacedAtOrigin_TilesPackBelowIt()
    {
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp");
        Assert.Equal(0, s.CoreMatrixX);
        Assert.Equal(0, s.CoreMatrixY);
        Assert.Equal(0, vm.CoreMatrixX);
        Assert.Equal(0, vm.CoreMatrixY);
        // Block size unmeasured (0x0) — pack still starts a Gap below y = 0, not at y = 0.
        Assert.Equal(0, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(DashboardLayout.Gap, s.TilePrefs["cpu.temp"].Y);
    }

    [Fact]
    public void SeedPack_CoreMatrixBlockMeasured_TilesStartBelowItsMeasuredHeight()
    {
        // Start Auto (construction's RebuildSections skips the seed pack entirely), measure the block, then switch
        // to Free so the first-ever seed pack run sees the already-known height — unlike the "unmeasured" case
        // above, nothing has been auto-seeded yet to be left untouched.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Auto, showCoreMatrix: true, "cpu.temp");
        vm.SetCoreMatrixSize(400, 250);
        vm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Free);
        Assert.Equal(250 + DashboardLayout.Gap, s.TilePrefs["cpu.temp"].Y);
    }

    [Fact]
    public void SeedPack_WrapsToNewRow_WhenRowWouldExceedPackWidth()
    {
        var many = Enumerable.Range(1, 5).Select(i => $"gpu.m{i}").ToArray();
        var defs = many.Select(id => new MetricDefinition(id, id, MetricGroup.Gpu, "RTX", "MHz")).ToList();
        var store = new MetricStore(defs);
        var s = new AppSettings { DashboardMetrics = many.ToList(), DashboardLayoutMode = DashboardLayoutMode.Free, ShowCoreMatrix = false };
        var vm = new DashboardViewModel(store, s, () => { });

        // 4 M tiles (224 each) fit exactly at x = 0, 236, 472, 708 (708 + 224 = 932 == PackWidth, still fits);
        // the 5th would push to 944 > 932, so it wraps to row 2 at (0, 144 + 12).
        Assert.Equal(0, s.TilePrefs["gpu.m1"].X);
        Assert.Equal(708, s.TilePrefs["gpu.m4"].X);
        Assert.Equal(0, s.TilePrefs["gpu.m4"].Y);
        Assert.Equal(0, s.TilePrefs["gpu.m5"].X);
        Assert.Equal(144 + DashboardLayout.Gap, s.TilePrefs["gpu.m5"].Y);
    }

    [Fact]
    public void SeedPack_LeavesAlreadyPlacedTilesUntouched()
    {
        // Free from construction, so both tiles are already seeded (cpu.temp at x=0, gpu.clock at x=236) before
        // this test does anything.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        Assert.Equal(236, s.TilePrefs["gpu.clock"].X); // sanity: seeded next to cpu.temp at construction

        vm.SetTilePosition("cpu.temp", 500, 500); // re-place explicitly, away from its seeded spot
        vm.RebuildSections(); // seed pack runs again — both tiles already have positions, so neither moves

        Assert.Equal(500, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(500, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(236, s.TilePrefs["gpu.clock"].X); // untouched by the re-run, not re-seeded to 0
        Assert.Equal(0, s.TilePrefs["gpu.clock"].Y);
    }

    [Fact]
    public void SeedPack_GridMode_SnapsSeededPositions()
    {
        // Tile widths (224) aren't multiples of GridSize's neighbour math in every case, but each individual
        // coordinate is still snapped — verify no seeded coordinate needs further snapping.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Grid, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        foreach (var pref in s.TilePrefs.Values)
        {
            Assert.Equal(DashboardLayout.Snap(pref.X!.Value), pref.X.Value);
            Assert.Equal(DashboardLayout.Snap(pref.Y!.Value), pref.Y.Value);
        }
    }

    [Fact]
    public void SeedPack_SavesOnce_OnlyWhenSomethingWasPlaced()
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings { DashboardMetrics = { "cpu.temp" }, DashboardLayoutMode = DashboardLayoutMode.Free, ShowCoreMatrix = false };
        int saves = 0;
        var vm = new DashboardViewModel(store, s, () => saves++); // construction itself triggers the initial seed
        Assert.Equal(1, saves);

        vm.RebuildSections(); // everything already placed — no-op seed pack, no extra save
        Assert.Equal(1, saves);
    }

    // ---- ResetPositions ----

    [Fact]
    public void ResetPositionsCommand_NullsEverything_ThenReSeeds()
    {
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp", "gpu.clock");
        vm.SetTilePosition("cpu.temp", 500, 500);
        vm.SetCoreMatrixPosition(300, 300);

        vm.ResetPositionsCommand.Execute(null);

        // Re-seeded back to the deterministic pack, not left at the old dragged positions.
        Assert.Equal(0, s.CoreMatrixX);
        Assert.Equal(0, s.CoreMatrixY);
        Assert.Equal(0, s.TilePrefs["cpu.temp"].X);
        Assert.NotEqual(500, s.TilePrefs["cpu.temp"].X);
    }

    // ---- canvas extent ----

    [Fact]
    public void CanvasExtent_EqualsMaxRightBottomPlusGap()
    {
        var (vm, _, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        vm.SetTilePosition("cpu.temp", 0, 0);
        vm.SetTilePosition("gpu.clock", 300, 50);
        // gpu.clock: right = 300 + 224 = 524, bottom = 50 + 144 = 194 — the far corner.
        Assert.Equal(524 + DashboardLayout.Gap, vm.CanvasWidth);
        Assert.Equal(194 + DashboardLayout.Gap, vm.CanvasHeight);
    }

    [Fact]
    public void CanvasExtent_ZeroWhenNothingPlaced()
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings { DashboardMetrics = new(), ShowCoreMatrix = false, DashboardLayoutMode = DashboardLayoutMode.Auto };
        var vm = new DashboardViewModel(store, s, () => { });
        Assert.Equal(0, vm.CanvasWidth);
        Assert.Equal(0, vm.CanvasHeight);
    }

    // ---- StatusLines ----

    [Fact]
    public void StatusLines_MirrorsSetGroupStatus_InGroupOrder()
    {
        var (vm, _, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        vm.SetGroupStatus(MetricGroup.Gpu, "GPU busy");
        vm.SetGroupStatus(MetricGroup.Cpu, "CPU busy");
        // GroupOrder is Cpu, Gpu, ... — StatusLines must follow that order, not insertion order.
        Assert.Equal(new[] { "CPU busy", "GPU busy" }, vm.StatusLines);

        vm.SetGroupStatus(MetricGroup.Cpu, null);
        Assert.Equal(new[] { "GPU busy" }, vm.StatusLines);
    }

    [Fact]
    public void StatusLines_SurvivesRebuildSections()
    {
        var (vm, _, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        vm.SetGroupStatus(MetricGroup.Cpu, "still here");
        vm.RebuildSections();
        Assert.Equal(new[] { "still here" }, vm.StatusLines);
    }
}
