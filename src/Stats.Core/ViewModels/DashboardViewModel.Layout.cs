using CommunityToolkit.Mvvm.Input;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

/// <summary>Free/Snap dashboard canvas support (dashboard layout modes): <see cref="DashboardViewModel.LayoutMode"/>
/// switching, per-tile/core-matrix position writes, the deterministic seed pack that gives every newly-unplaced
/// tile a position, and the canvas extent those positions drive. Kept in its own partial file — the main
/// DashboardViewModel.cs already covers picker/tile/section concerns and this is a self-contained slice of them
/// (see the design: docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md).</summary>
public sealed partial class DashboardViewModel
{
    /// <summary>Four M-tile columns wide (4 · 224 + 3 · 12), the seed pack's row-wrap width — independent of the
    /// dashboard's actual window width; it's just a deterministic, reasonably square packing target.</summary>
    private const double PackWidth = 4 * 224 + 3 * 12;

    /// <summary>Core-matrix block's last size reported by the view via <see cref="SetCoreMatrixSize"/>; 0×0 until
    /// then. Not persisted — the view re-measures and re-reports it on every launch.</summary>
    private double _coreMatrixWidth;
    private double _coreMatrixHeight;

    /// <summary>(id, baseY) for tiles the seed pack (<see cref="PlaceUnpositioned"/>) placed while the core-matrix
    /// block was being freshly seeded in that same call but its height was still unmeasured — <c>baseY</c> is the
    /// pre-shift seed Y. <see cref="SetCoreMatrixSize"/> re-applies <c>baseY + height</c> for every tile still in
    /// this list on <em>every</em> call (WPF fires more than one <c>SizeChanged</c> as the block settles into its
    /// final layout), so the reservation stays correct no matter which measurement is the real one. An entry drops
    /// out on an explicit <see cref="SetTilePosition"/>, <see cref="ResetPositions"/>, or a fresh seed pass.</summary>
    private readonly List<(string Id, double BaseY)> _tilesSeededBelowUnmeasuredBlock = new();

    /// <summary>Whether <see cref="PlaceUnpositioned"/> saved during the most recent <see cref="RebuildSections"/>
    /// call — <see cref="OnLayoutModeChanged"/> reads this so it never double-saves when the seed pack already did
    /// (S11), while still guaranteeing the mode itself is always persisted when the seed pack had nothing to do.</summary>
    private bool _seedPackSavedLastRebuild;

    public bool IsAutoLayout => LayoutMode == DashboardLayoutMode.Auto;
    public bool IsGridLayout => LayoutMode == DashboardLayoutMode.Grid;

    /// <summary>The per-group status texts (<see cref="_groupStatus"/>), in <see cref="GroupOrder"/> order, so a
    /// Free/Snap dashboard (whose canvas has no group headers) can still show e.g. why FPS metrics are blank. Kept
    /// in sync by <see cref="SetGroupStatus"/> and after every <see cref="RebuildSections"/>.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> StatusLines { get; } = new();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _coreMatrixX;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _coreMatrixY;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _canvasWidth;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private double _canvasHeight;

    /// <summary>Persists the new mode, rebuilds (Auto's grouped sections vs. Free/Snap's flat canvas both read
    /// <see cref="LayoutMode"/> during <see cref="RebuildSections"/>, and the seed pack only runs when it's not
    /// Auto), and re-raises the two mode-flag properties the View menu's checkable items and the canvas visibility
    /// binding depend on.
    ///
    /// Saves only when the rebuild's own seed pack didn't already (<see cref="PlaceUnpositioned"/> saves once,
    /// only if it placed anything — e.g. switching to a mode where every tile already has a position, or back to
    /// Auto, places nothing). Either way the mode change itself always ends up persisted.</summary>
    partial void OnLayoutModeChanged(DashboardLayoutMode value)
    {
        if (_applyingLayoutProfile || _restoringLayoutUndo) return;
        if (!BeginLayoutEdit(undoable: false))
        {
            if (LayoutMode != _settings.DashboardLayoutMode) LayoutMode = _settings.DashboardLayoutMode;
            return;
        }
        _settings.DashboardLayoutMode = value;
        ClearLayoutUndo();
        MarkLayoutModified();
        RebuildSections();
        if (!_seedPackSavedLastRebuild) _saveSettings();
        OnPropertyChanged(nameof(IsAutoLayout));
        OnPropertyChanged(nameof(IsGridLayout));
    }

    /// <summary>View menu's Auto/Free/Snap items bind to this.</summary>
    [RelayCommand]
    private void SetLayoutMode(DashboardLayoutMode mode) => LayoutMode = mode;

    /// <summary>Clears every persisted tile and core-matrix position, then rebuilds — which, since every position is
    /// now null, re-seeds all of them from scratch via <see cref="PlaceUnpositioned"/>. Only meaningful (and only
    /// enabled in the View menu) outside Auto layout.</summary>
    [RelayCommand]
    private void ResetPositions()
    {
        if (!BeginLayoutEdit(undoable: true)) return;
        foreach (var pref in _settings.TilePrefs.Values)
        {
            pref.X = null;
            pref.Y = null;
        }
        _settings.CoreMatrixX = null;
        _settings.CoreMatrixY = null;
        _tilesSeededBelowUnmeasuredBlock.Clear();
        MarkLayoutModified();
        RebuildSections(); // re-seeds (outside Auto) and recomputes the canvas extent unconditionally
        RecomputeCanvasExtent();
        _saveSettings(); // unconditional — must persist even with zero tiles/no core matrix, or in Auto layout
    }

    /// <summary>Moves one tile to (<paramref name="x"/>, <paramref name="y"/>): clamps to the non-negative canvas,
    /// snaps to <see cref="DashboardLayout.GridSize"/> in Grid layout, persists to the tile's <see cref="TilePref"/>,
    /// updates the live tile VM so the binding shows the final (possibly snapped/clamped) value, recomputes the
    /// canvas extent, and saves once. The view only calls this from Free/Snap containers — Auto's WrapPanel tiles
    /// have no drag/nudge handlers wired to it.</summary>
    public void SetTilePosition(string id, double x, double y)
    {
        if (!BeginLayoutEdit(undoable: true)) return;
        var tile = Tiles.FirstOrDefault(t => t.Definition.Id == id);
        if (tile is null) { ClearLayoutUndo(); return; }

        (x, y) = ClampAndMaybeSnap(x, y);

        // An explicit move supersedes the seed position — the tile must not be shifted again when the block's
        // measured height arrives (see SetCoreMatrixSize).
        _tilesSeededBelowUnmeasuredBlock.RemoveAll(t => t.Id == id);

        var pref = _settings.PrefFor(id);
        pref.X = x;
        pref.Y = y;
        tile.X = x;
        tile.Y = y;

        MarkLayoutModified();
        RecomputeCanvasExtent();
        _saveSettings();
    }

    /// <summary>Same contract as <see cref="SetTilePosition"/> for the core-matrix block, which has no tile VM of
    /// its own — its position lives directly on this view model as <see cref="CoreMatrixX"/>/<see cref="CoreMatrixY"/>.</summary>
    public void SetCoreMatrixPosition(double x, double y)
    {
        if (!BeginLayoutEdit(undoable: true)) return;
        (x, y) = ClampAndMaybeSnap(x, y);

        _settings.CoreMatrixX = x;
        _settings.CoreMatrixY = y;
        CoreMatrixX = x;
        CoreMatrixY = y;

        MarkLayoutModified();
        RecomputeCanvasExtent();
        _saveSettings();
    }

    /// <summary>Records the core-matrix block's measured pixel size, reported by the view once it has laid the
    /// block out, so the seed pack and the canvas extent can account for it. Never persisted — position is the only
    /// thing saved for the block; size is re-measured every launch.
    ///
    /// Also fixes up the one case the seed pack couldn't get right the first time: if <see cref="PlaceUnpositioned"/>
    /// ran before the view had ever reported a size, it placed tiles starting at y = <see cref="DashboardLayout.Gap"/>
    /// (treating the still-unmeasured block as 0 tall) rather than under the block's real height. WPF fires
    /// <c>SizeChanged</c> more than once while the block settles into its final layout — the first firing can carry
    /// a partial/intermediate height — so this re-applies <c>baseY + height</c> (re-snapping in Grid layout) for
    /// every tile still in <see cref="_tilesSeededBelowUnmeasuredBlock"/> on <em>every</em> call, not just the
    /// first, and only that final re-measurement's value survives. Saves once per call, only when something
    /// actually moved, so the per-measure churn is zero once the height settles.</summary>
    public void SetCoreMatrixSize(double width, double height)
    {
        _coreMatrixWidth = width;
        _coreMatrixHeight = height;

        if (height > 0 && _tilesSeededBelowUnmeasuredBlock.Count > 0)
        {
            bool changed = false;
            foreach (var (id, baseY) in _tilesSeededBelowUnmeasuredBlock)
            {
                var newY = baseY + height;
                if (IsGridLayout) newY = DashboardLayout.Snap(newY);

                var pref = _settings.PrefFor(id);
                if (pref.Y == newY) continue;
                pref.Y = newY;
                var tile = Tiles.FirstOrDefault(t => t.Definition.Id == id);
                if (tile is not null) tile.Y = newY;
                changed = true;
            }
            if (changed) _saveSettings();
        }

        RecomputeCanvasExtent();
    }

    private (double X, double Y) ClampAndMaybeSnap(double x, double y)
    {
        x = DashboardLayout.Clamp(x);
        y = DashboardLayout.Clamp(y);
        if (IsGridLayout)
        {
            x = DashboardLayout.Snap(x);
            y = DashboardLayout.Snap(y);
        }
        return (x, y);
    }

    /// <summary>Canvas extent = the max right/bottom edge over every tile and the core-matrix block, plus one
    /// <see cref="DashboardLayout.Gap"/> of breathing room, floored at 0 (nothing placed yet). Called after every
    /// position/size write and at the end of every <see cref="RebuildSections"/>.</summary>
    private void RecomputeCanvasExtent()
    {
        double right = 0, bottom = 0;
        foreach (var tile in Tiles)
        {
            right = Math.Max(right, tile.X + tile.Width);
            bottom = Math.Max(bottom, tile.Y + tile.Height);
        }
        if (CoreMatrix is not null)
        {
            right = Math.Max(right, CoreMatrixX + _coreMatrixWidth);
            bottom = Math.Max(bottom, CoreMatrixY + _coreMatrixHeight);
        }
        CanvasWidth = right > 0 ? right + DashboardLayout.Gap : 0;
        CanvasHeight = bottom > 0 ? bottom + DashboardLayout.Gap : 0;
    }

    /// <summary>Seeds a deterministic position for every tile (and the core-matrix block) that doesn't have one yet.
    /// Run at the end of <see cref="RebuildSections"/> whenever <see cref="LayoutMode"/> isn't Auto.
    ///
    /// The core-matrix block, when present and unplaced (either coordinate still null), is placed first at (0, 0)
    /// and the pack starts below it — at <c>_coreMatrixHeight + Gap</c>. This applies even when the block's
    /// measured size is still 0×0 (the view hasn't reported it yet): the pack still starts a full <see cref="DashboardLayout.Gap"/>
    /// below y = 0 rather than letting the very first tile land exactly under where the block will end up once
    /// measured. A block that already has a persisted position (placed by a previous run, or dragged since) is left
    /// alone and does not reserve any space — Free/Snap explicitly allows overlap, so tiles pack from y = 0 in
    /// that case.
    ///
    /// Tiles are placed in <see cref="Tiles"/> order (already <see cref="GroupOrder"/>-then-user order — see
    /// <see cref="RebuildSections"/>), left-to-right, wrapping to a new row once a row would exceed
    /// <see cref="PackWidth"/>; a row's height is its tallest placed tile, and <see cref="DashboardLayout.Gap"/>
    /// separates every neighbour (row-to-row too). Only tiles whose <see cref="TilePref.X"/>/<see cref="TilePref.Y"/>
    /// are still null are touched; already-placed tiles are left exactly where they are. Positions are snapped when
    /// <see cref="IsGridLayout"/>, same as a drag. Saves once, only if anything was actually placed.</summary>
    private void PlaceUnpositioned()
    {
        bool placedAny = false;
        double startY = 0;
        // True only when the block is being freshly seeded in THIS call and the view hasn't reported a size yet —
        // see SetCoreMatrixSize for why that combination needs a later fix-up.
        bool blockFreshlySeededUnmeasured = false;

        if (CoreMatrix is not null && (_settings.CoreMatrixX is null || _settings.CoreMatrixY is null))
        {
            _settings.CoreMatrixX = 0;
            _settings.CoreMatrixY = 0;
            CoreMatrixX = 0;
            CoreMatrixY = 0;
            startY = _coreMatrixHeight + DashboardLayout.Gap;
            placedAny = true;
            blockFreshlySeededUnmeasured = _coreMatrixHeight == 0;
        }
        if (blockFreshlySeededUnmeasured) _tilesSeededBelowUnmeasuredBlock.Clear();

        // S3: a newly-added tile must not land under an existing one. Start the pack below the bottom edge of
        // everything already placed — every already-positioned tile, plus the core-matrix block if it already has
        // a position — not just below the block. Simplest deterministic rule: one shared start row, not a
        // per-column skyline.
        double maxBottom = startY;
        foreach (var t in Tiles)
        {
            var placedPref = _settings.PrefFor(t.Definition.Id);
            if (placedPref.X is not null && placedPref.Y is not null)
                maxBottom = Math.Max(maxBottom, placedPref.Y.Value + t.Height);
        }
        if (CoreMatrix is not null && _settings.CoreMatrixX is not null && _settings.CoreMatrixY is not null)
            maxBottom = Math.Max(maxBottom, _settings.CoreMatrixY.Value + _coreMatrixHeight);

        double x = 0, y = maxBottom > startY ? maxBottom + DashboardLayout.Gap : startY, rowHeight = 0;
        foreach (var tile in Tiles)
        {
            var pref = _settings.PrefFor(tile.Definition.Id);
            if (pref.X is not null && pref.Y is not null) continue; // already placed — untouched

            if (x > 0 && x + tile.Width > PackWidth)
            {
                x = 0;
                y += rowHeight + DashboardLayout.Gap;
                rowHeight = 0;
            }

            var (px, py) = IsGridLayout ? (DashboardLayout.Snap(x), DashboardLayout.Snap(y)) : (x, y);
            pref.X = px;
            pref.Y = py;
            tile.X = px;
            tile.Y = py;
            if (blockFreshlySeededUnmeasured) _tilesSeededBelowUnmeasuredBlock.Add((tile.Definition.Id, py));

            rowHeight = Math.Max(rowHeight, tile.Height);
            x += tile.Width + DashboardLayout.Gap;
            placedAny = true;
        }

        _seedPackSavedLastRebuild = placedAny;
        if (placedAny) _saveSettings();
    }

    private void RebuildStatusLines()
    {
        // S1: SetGroupStatus fires on every poll tick, and used to unconditionally Clear()+refill this collection
        // even when nothing changed — a Reset + N Adds every tick, tearing down and rebuilding the bound
        // ItemsControl's containers for no reason. Compute the new list and only touch the collection when it
        // actually differs.
        var updated = GroupOrder.Where(g => _groupStatus.ContainsKey(g)).Select(g => _groupStatus[g]).ToList();
        if (updated.SequenceEqual(StatusLines)) return;
        StatusLines.Clear();
        foreach (var text in updated) StatusLines.Add(text);
    }
}
