# Dashboard layout modes — design

Date: 2026-09-11. Base: master @ `75115aa` (v1.9.2). Owner decisions already made (do not re-litigate):
Free and Snap modes use **one whole-dashboard canvas** (group headers disappear in those modes); Snap rounds
to a **fine 16-unit grid and allows overlap**; Auto keeps today's grouped WrapPanel layout unchanged.

## Goal

Let the user choose how dashboard tiles are arranged:

| Mode | Behavior |
| --- | --- |
| **Auto** (default, today) | Collapsible groups in `GroupOrder`, tiles flow in a WrapPanel per group, same-group drag reorder. Unchanged. |
| **Free** | One scrollable canvas. Every tile (and the core matrix block) sits at a persisted X/Y and can be dragged anywhere. |
| **Snap to grid** | Same as Free, but a dropped or nudged position rounds to the nearest multiple of `DashboardLayout.GridSize` (16). Overlap is allowed. |

Switching modes never loses anything: Auto ignores positions, Free/Snap ignore group order. Positions are
seeded from a deterministic pack the first time a tile has none.

## Settings (rule 3: every field defaulted, null-guarded, old files load)

- `AppSettings.DashboardLayoutMode` — new enum `DashboardLayoutMode { Auto, Free, Grid }` (append-only,
  serialized as a string like the other enums; unknown/missing → `Auto`).
- `TilePref.X`, `TilePref.Y` — `double?`, null = not yet placed. Written only by Free/Snap interactions and
  by the seed pack. Never touched in Auto.
- `AppSettings.CoreMatrixX`, `CoreMatrixY` — `double?`, same semantics for the core-matrix block.
- `SettingsService.Normalize`: sanitize positions — NaN/negative/absurd (> 100 000) → null.

## Core

- New `Stats.Core/Settings/TileDimensions.cs`: `static (double Width, double Height) Of(TileSize)` holding
  S 160×80, M 224×144, L 460×192 (moved from `TileSizeToLengthConverter`, which now delegates). Also
  `CoreMatrixBlockSize` is *not* known to Core — the view reports the block's actual size (see below).
- New `Stats.Core/ViewModels/DashboardLayout.cs` (static): `GridSize = 16`, `Gap = 12`,
  `Snap(double v) => Math.Round(v / GridSize) * GridSize`, `Clamp(double v) => Math.Max(0, v)`.
- `MetricTileViewModel`: `[ObservableProperty] double X, Y` (from pref in `Refresh`, default 0) and
  `Width`/`Height` computed from `TileDimensions.Of(Size)` (observable, raised when Size changes).
- `DashboardViewModel`:
  - `[ObservableProperty] DashboardLayoutMode LayoutMode` (init from settings; setter persists via
    `_saveSettings`, then `RebuildSections()`), `bool IsAutoLayout => LayoutMode == Auto`,
    `bool IsGridLayout`.
  - `SetLayoutModeCommand(DashboardLayoutMode)` for the View menu; `ResetPositionsCommand` clears every
    `TilePref.X/Y` and the core-matrix position, then re-seeds.
  - `SetTilePosition(string id, double x, double y)`: clamp ≥ 0, snap when `IsGridLayout`, write pref,
    update the tile's X/Y, recompute canvas extent, save.
  - `SetCoreMatrixPosition(double x, double y)`, plus `CoreMatrixX/Y` observable on the VM (the block has
    no tile VM). The view passes the block's measured size to `SetCoreMatrixSize(w, h)` after layout so the
    pack and the extent can account for it (default 0×0 until measured).
  - Seed pack (`PlaceUnpositioned()`), run at the end of `RebuildSections` whenever `LayoutMode != Auto`:
    rows of width `PackWidth` (constant 4 M columns = 4·224 + 3·12 = 932) filled left-to-right in
    `GroupOrder` then user order, each tile placed with `Gap` between neighbors, row height = tallest tile
    in the row; the core matrix block (if present and unplaced) goes first at (0,0) and the pack starts
    below it. Only tiles with null X/Y are placed; placed tiles are untouched. Snapping is applied when
    `IsGridLayout` (row/column steps are multiples of 4, so the 12 gap becomes 16 under snap — fine).
  - `CanvasWidth`/`CanvasHeight` observable: max(right)/max(bottom) over tiles + block, plus one `Gap`;
    minimum 0. Recomputed by `SetTilePosition`, `SetCoreMatrixPosition/Size`, and after every rebuild.
  - `StatusLines` (`ObservableCollection<string>`): the per-group status texts (`_groupStatus`), so a
    Free/Snap dashboard still shows e.g. the PresentMon reason above the canvas. Kept in sync by
    `SetGroupStatus`.
  - `MoveTile` (same-group reorder) keeps working in Auto only; in Free/Snap the view never calls it.
  - Group expansion state (`CollapsedGroups`) is untouched by Free/Snap.

## View (`DashboardWindow`)

- The content area becomes a `Grid` with two children switched by `IsAutoLayout` visibility: the existing
  sections `ItemsControl` (unchanged), and a new `ScrollViewer` (both scrollbars Auto) containing an
  `ItemsControl x:Name="FreeCanvas"` whose `ItemsPanel` is a `Canvas` with `Width`/`Height` bound to
  `CanvasWidth`/`CanvasHeight`. Its `ItemsSource` is a `CompositeCollection`: the core-matrix block (when
  `CoreMatrix` is non-null) and `Tiles`. The `ItemContainerStyle` binds `Canvas.Left`/`Canvas.Top` to
  `X`/`Y` (for the block: `DataContext.CoreMatrixX/Y` via the window) and reuses the same
  `Focusable`/`IsTabStop`/`FocusVisualStyle`, `MouseRightButtonUp`, `Button.Click`, and `PreviewKeyDown`
  EventSetters as the Auto containers, so right-click, the "…" button, Shift+F10, and double-click Details
  work identically. Templates come from the existing `TileSelector`; the block uses the existing
  `CoreMatrixView` DataTemplate.
- Dragging in Free/Snap: `PreviewMouseLeftButtonDown` records the offset and captures the mouse on the
  container; `PreviewMouseMove` sets `Canvas.Left/Top` directly for live feedback (no VM writes per pixel);
  `PreviewMouseLeftButtonUp` releases capture and calls `vm.SetTilePosition(id, left, top)` (or
  `SetCoreMatrixPosition`) — the VM snaps/clamps and the binding re-applies the final value. A drag below
  `SystemParameters.MinimumHorizontalDragDistance` is a click (double-click still opens Details). The Auto
  mode's `DragDrop.DoDragDrop` path is not used in Free/Snap.
- Keyboard: with a container focused in Free/Snap, arrow keys nudge by `GridSize` (Shift = 4×) via the
  same `SetTilePosition`; Ctrl+Arrow is not used. Announce nothing extra; the tile's `AutomationProperties.Name`
  already carries its identity.
- `StatusLines` render above the canvas as the existing warn-styled status text (same look as the group
  status line), one per line, collapsed when empty.
- View menu (`ViewButton_Click`): add a separator and three checkable items — Auto arrange, Free, Snap to
  grid — bound to `LayoutMode` via `SetLayoutModeCommand`; Collapse all / Expand all are disabled when not
  in Auto; add "Reset tile positions…" (enabled in Free/Snap) → `ResetPositionsCommand`.
- The empty state (`IsEmpty`) applies to all modes. The UI-scale `LayoutTransform` wraps both children as
  today.
- `README.md`: describe the three modes, drag/keyboard nudge, snap size, Reset, and that positions persist.

## Preview harness

`PreviewComposition`: dashboard substates `layout-free`, `layout-grid` (set `DashboardLayoutMode`,
leave positions null so the seed pack runs) and `layout-free-placed` (seeded plus a few explicit
positions, one overlapping pair, to show overlap is allowed). `baseline.json` gains three entries.

## Non-goals

No collision resolution, no resizing by drag, no per-mode tile sizes, no group labels on the canvas, no
overlay changes, no new NuGet packages.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green.
- Tests: `TileDimensions` matches the converter; `DashboardLayout.Snap` rounds half-up and clamps;
  `SetTilePosition` snaps only in Grid, clamps negatives, persists to `TilePref`, saves once; the seed pack
  places unplaced tiles in group-then-user order without overlap and below the block; already-placed tiles
  are untouched; `ResetPositions` nulls everything and re-seeds; canvas extent equals the max
  right/bottom + gap; `LayoutMode` round-trips through `SettingsService` (unknown string → Auto; NaN
  position → null); `StatusLines` mirrors `SetGroupStatus`.
- Captures: `layout-free`, `layout-grid`, `layout-free-placed` at 1180×720 Dark Amber and Light, plus
  Auto unchanged vs the after-gallery.
- Owner checklist: drag feel, snapping, keyboard nudge, Details on double-click in Free mode, mode
  persistence across restart, core-matrix block drag.
