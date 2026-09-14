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
        // S11: one save — the rebuild's own seed pack save (it persists cpu.temp's freshly-seeded position, now
        // that RebuildSections runs in non-Auto layout for the first time) already covers persisting the mode
        // change too, so OnLayoutModeChanged's own save is skipped rather than redundantly saving a second time.
        Assert.Equal(1, saves());
    }

    [Fact]
    public void SetLayoutModeCommand_StillSavesOnce_WhenTheSeedPackHasNothingToPlace()
    {
        // Every tile (and the block) is already positioned by the time this mode switch happens, so the seed
        // pack's own save (S11's usual path) never fires — the mode change must still end up persisted via the
        // unconditional fallback save in OnLayoutModeChanged, not silently dropped.
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        int before = saves(); // construction already seeded+saved cpu.temp once

        vm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Grid);

        Assert.Equal(DashboardLayoutMode.Grid, s.DashboardLayoutMode);
        Assert.Equal(before + 1, saves()); // fallback save — nothing was seeded, but the mode itself still persisted
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
    public void SetCoreMatrixSize_UpdatesExtent_AndNeverSavesTheSizeItself()
    {
        // No tiles selected here (unlike most of this group) specifically so construction's seed pack places
        // nothing below the block and therefore has nothing pending for SetCoreMatrixSize to shift — isolating the
        // plain extent/no-save contract from the below-the-block shift covered by
        // SetCoreMatrixSize_ShiftsTilesSeededBelowAnUnmeasuredBlock.
        var (vm, _, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: true);
        int before = saves();
        vm.SetCoreMatrixSize(300, 200);
        Assert.Equal(before, saves()); // size is never persisted, and nothing was pending a shift
        Assert.True(vm.CanvasWidth >= 300 + DashboardLayout.Gap);
        Assert.True(vm.CanvasHeight >= 200 + DashboardLayout.Gap);
    }

    [Fact]
    public void SetCoreMatrixSize_ReappliesTheShift_OnEveryCall_UntilTheFinalMeasurementWins()
    {
        // Construction seeds cpu.temp at (0, Gap) because the block is freshly seeded but still unmeasured (0x0) —
        // see SeedPack_CoreMatrixBlock_PlacedAtOrigin_TilesPackBelowIt. WPF fires SizeChanged more than once while
        // the block settles into its final layout — the first firing can carry a partial/intermediate height (B1) —
        // so a *later, larger* measurement must re-apply baseY + newHeight, not leave the tile at the first
        // shift's value. See SetCoreMatrixSize_DoesNotShiftATileMovedExplicitlyAfterSeeding for the converse case
        // (an explicit move supersedes the seed and is never reshifted).
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp");
        Assert.Equal(DashboardLayout.Gap, s.TilePrefs["cpu.temp"].Y); // sanity: the "unmeasured" seed position
        int before = saves();

        vm.SetCoreMatrixSize(300, 61); // an intermediate/partial measurement, e.g. WPF's first SizeChanged firing

        Assert.Equal(DashboardLayout.Gap + 61, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(before + 1, saves());

        vm.SetCoreMatrixSize(300, 200); // the later, real measurement re-applies against the same baseY, not 61

        Assert.Equal(DashboardLayout.Gap + 200, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(DashboardLayout.Gap + 200, vm.Tiles.Single(t => t.Definition.Id == "cpu.temp").Y);
        Assert.Equal(before + 2, saves()); // each shift that actually moved something is persisted

        vm.SetCoreMatrixSize(300, 200); // re-reporting the same height moves nothing — no extra save
        Assert.Equal(before + 2, saves());
    }

    [Fact]
    public void SetCoreMatrixSize_GridMode_ReSnapsTheShiftedPosition()
    {
        // B1b: the shift used to add a raw measured height without re-snapping, so in Grid layout every tile
        // seeded below an unmeasured block ended up off-grid once the real height arrived. The shifted Y must go
        // back through the same snap the seed pack and every drag use.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Grid, showCoreMatrix: true, "cpu.temp");
        Assert.Equal(0, s.TilePrefs["cpu.temp"].Y % DashboardLayout.GridSize); // sanity: seeded on-grid

        vm.SetCoreMatrixSize(300, 61); // a height that would land off-grid if added raw (Gap=12 + 61 = 73)

        var y = s.TilePrefs["cpu.temp"].Y!.Value;
        Assert.Equal(0, y % DashboardLayout.GridSize);
        Assert.Equal(y, vm.Tiles.Single(t => t.Definition.Id == "cpu.temp").Y);
    }

    [Fact]
    public void SetCoreMatrixSize_DoesNotShiftATileMovedExplicitlyAfterSeeding()
    {
        // Same seeded-under-an-unmeasured-block setup as above, but the user (or a harness substate) drags the
        // tile before the block reports its size: the explicit position wins and must not be shifted afterwards.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp");
        vm.SetTilePosition("cpu.temp", 700, 420);

        vm.SetCoreMatrixSize(300, 200);

        Assert.Equal(420, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(420, vm.Tiles.Single(t => t.Definition.Id == "cpu.temp").Y);
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
    public void SeedPack_NewlyAddedTile_SeedsBelowExistingPlacedTiles_NotOnTopOfThem()
    {
        // S3: enabling a second metric after the first is already placed must not seed it at the same (0, startY)
        // spot the first tile occupies — that reads as "nothing happened" since the new tile is invisible under
        // the old one. It must land below the bottom edge of everything already placed.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        var firstBottom = s.TilePrefs["cpu.temp"].Y!.Value + vm.Tiles.Single().Height;

        s.DashboardMetrics.Add("gpu.clock"); // simulates the picker enabling a new metric
        vm.RebuildSections();

        var gpuTile = vm.Tiles.Single(t => t.Definition.Id == "gpu.clock");
        Assert.True(s.TilePrefs["gpu.clock"].Y >= firstBottom);
        Assert.True(gpuTile.Y >= firstBottom);
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

    [Fact]
    public void ResetPositionsCommand_WithZeroTilesAndNoCoreMatrix_StillPersists()
    {
        // S2: with nothing selected and no core matrix, PlaceUnpositioned places nothing and therefore never
        // saves on its own — ResetPositions must still persist unconditionally, or the reset is silently lost on
        // the next launch.
        var (vm, _, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: false);
        int before = saves();

        vm.ResetPositionsCommand.Execute(null);

        Assert.True(saves() > before);
        Assert.Equal(0, vm.CanvasWidth);
        Assert.Equal(0, vm.CanvasHeight);
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

    [Fact]
    public void StatusLines_UnchangedGroupStatus_DoesNotChurnTheCollection()
    {
        // S1: SetGroupStatus fires on every poll tick (App.xaml.cs's coalesced refresh), and used to
        // unconditionally Clear()+refill StatusLines even when the text for that group hadn't changed — a
        // Reset + N Adds per tick that tears down and rebuilds the bound ItemsControl's containers for nothing.
        var (vm, _, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        vm.SetGroupStatus(MetricGroup.Cpu, "same text");
        var before = vm.StatusLines;
        int resets = 0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        before.CollectionChanged += handler;

        vm.SetGroupStatus(MetricGroup.Cpu, "same text"); // identical text on the next poll tick

        before.CollectionChanged -= handler;
        Assert.Equal(0, resets);
        Assert.Same(before, vm.StatusLines); // same ObservableCollection instance, never replaced
    }

    // ---- S9: missing negative cases ----

    [Fact]
    public void ModeRoundTrip_FreeToAutoToFree_DoesNotReSeedOrReSave()
    {
        // Per spec acceptance: switching Free -> Auto -> Free must not re-run the seed pack (every tile already
        // has a position from the first Free entry) or save again — RebuildSections' seed pack is correctly a
        // no-op here only because PlaceUnpositioned no-ops when everything is already placed; this asserts that
        // holds across a full mode round-trip, not just a same-mode re-run.
        var (vm, s, _, saves) = Make(DashboardLayoutMode.Free, showCoreMatrix: true, "cpu.temp", "gpu.clock");
        var cpuX = s.TilePrefs["cpu.temp"].X;
        var cpuY = s.TilePrefs["cpu.temp"].Y;
        var gpuX = s.TilePrefs["gpu.clock"].X;
        var gpuY = s.TilePrefs["gpu.clock"].Y;
        var coreX = s.CoreMatrixX;
        var coreY = s.CoreMatrixY;
        int before = saves();

        vm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Auto);
        vm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Free);

        Assert.Equal(cpuX, s.TilePrefs["cpu.temp"].X);
        Assert.Equal(cpuY, s.TilePrefs["cpu.temp"].Y);
        Assert.Equal(gpuX, s.TilePrefs["gpu.clock"].X);
        Assert.Equal(gpuY, s.TilePrefs["gpu.clock"].Y);
        Assert.Equal(coreX, s.CoreMatrixX);
        Assert.Equal(coreY, s.CoreMatrixY);
        // Two saves: Auto's own fallback save (nothing to seed, seed pack no-ops) and Free's own fallback save
        // (everything already placed, seed pack no-ops again) — never a re-seed's save.
        Assert.Equal(before + 2, saves());
    }

    [Fact]
    public void TileSizeChange_InFreeMode_UpdatesCanvasExtent()
    {
        // S9: a tile grown S -> L at the canvas edge must not be clipped until the next unrelated rebuild —
        // RecomputeCanvasExtent must actually run off a tile size change, not just off position writes.
        var (vm, s, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp");
        var tile = vm.Tiles.Single();
        vm.SetTilePosition("cpu.temp", 0, 0);
        var extentBefore = vm.CanvasWidth;

        vm.SetTileSizeEditCommand.Execute(new TileSizeEdit("cpu.temp", TileSize.L));

        var grown = vm.Tiles.Single(t => t.Definition.Id == "cpu.temp");
        Assert.True(grown.Width > tile.Width); // sanity: L is actually wider than M
        Assert.True(vm.CanvasWidth >= grown.X + grown.Width + DashboardLayout.Gap);
        Assert.True(vm.CanvasWidth > extentBefore);
    }

    // ---- IsResizable ----

    [Fact]
    public void IsResizable_TrueInFreeAndGrid_FalseInAuto_AndAfterModeSwitch()
    {
        var (freeVm, _, _, _) = Make(DashboardLayoutMode.Free, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        Assert.All(freeVm.Tiles, t => Assert.True(t.IsResizable));

        var (gridVm, _, _, _) = Make(DashboardLayoutMode.Grid, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        Assert.All(gridVm.Tiles, t => Assert.True(t.IsResizable));

        var (autoVm, _, _, _) = Make(DashboardLayoutMode.Auto, showCoreMatrix: false, "cpu.temp", "gpu.clock");
        Assert.All(autoVm.Tiles, t => Assert.False(t.IsResizable));

        // RebuildSections recreates every MetricTileViewModel, so the flag must be re-applied to the new
        // instances on a mode switch too, not just at construction.
        autoVm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Free);
        Assert.All(autoVm.Tiles, t => Assert.True(t.IsResizable));

        freeVm.SetLayoutModeCommand.Execute(DashboardLayoutMode.Auto);
        Assert.All(freeVm.Tiles, t => Assert.False(t.IsResizable));
    }
}
