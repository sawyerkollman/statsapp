# Tile resize by drag — Task 3 (harness + evidence)

Follows the structure of `docs/layout-modes/EVIDENCE.md`, scoped to this task
(plan: `docs/superpowers/plans/2026-09-11-tile-resize.md` Task 3; design/contract:
`docs/superpowers/specs/2026-09-11-tile-resize-design.md`, "Preview harness" and
"Acceptance -> Tests (harness) / Captures" sections).

## Candidate

- Date/time: 2026-09-12
- Repo/branch: `C:\claude-projects\Stats-wt\tile-resize`, `feature/tile-resize`
- Baseline commit: `181b2f2` ("feat(app): resize grip, outline adorner, Ctrl+Plus/Minus,
  canvas-coordinate drags" — Task 2, the last commit before this task)
- Final candidate: **`181b2f2` + this task's uncommitted files** (not committed, per this
  task's instructions):
  - `tools/Stats.UiPreview/PreviewComposition.cs` (modified)
  - `tools/Stats.UiPreview/SubstateCatalog.cs` (modified)
  - `tools/Stats.UiPreview/Views/CaptureHost.cs` (modified)
  - `tools/Stats.UiPreview/captures/baseline.json` (modified)
  - `tests/Stats.UiPreview.Tests/TileResizeSubstateTests.cs` (new)
  - `docs/tile-resize/EVIDENCE.md` (this file, new)
- Windows version, .NET SDK: Windows 11 Pro 10.0.26220, `dotnet 9.0.316` (repo targets
  `net8.0-windows`)
- Desktop availability: yes — captures below used `--method rtb` (RenderTargetBitmap), the
  method every prior ui-polish/layout-modes/graph-effects evidence doc in this repo has used.
- Overall status: implemented / verified (build + tests green) / visually accepted (the three
  new captures opened and inspected, plus a pixel-diff corroborating the grip glyph and the
  resized-tile region) / regression-checked (existing captures re-taken and confirmed
  byte-identical to their pre-Task-3 renders).

## Task ledger (this task only)

| Delivered | Files | Validation |
| --- | --- | --- |
| `layout-resized` (VM-level: Free mode + `SetTileSize` on 3 seeded tiles) and `layout-resize-grip` (visual: focuses the first Free container) substates; catalog entries; 3 `baseline.json` entries; harness tests; re-taken regression captures; this evidence file | see "Files changed" below | `dotnet build`/`dotnet test` below; captures inspected + pixel-diffed below |

## Files changed

- `tools/Stats.UiPreview/PreviewComposition.cs` — `ApplySubstate` gained two cases:
  `"layout-resized"` (calls a new `ApplyLayoutResized` helper) and `"layout-resize-grip"`
  (no-op placeholder — the visual-tree work happens in `CaptureHost`, same pattern as the
  existing `"tile-menu"` case). `ApplyLayoutResized` sets `LayoutMode = Free` (seeds every
  tile/core-matrix position) then calls `DashboardViewModel.SetTileSize` on the first three
  tiles in the `"dense"` fixture's seed order.
- `tools/Stats.UiPreview/SubstateCatalog.cs` — added `"layout-resized"`, `"layout-resize-grip"`
  to the `dashboard` view's valid-substate list.
- `tools/Stats.UiPreview/Views/CaptureHost.cs` — `ApplyVisualSubstates` gained a case for
  `"layout-resize-grip"`: a new `FocusFirstFreeTileContainer` helper finds the Free canvas by
  its `x:Name` (`"FreeTilesList"`, the `TileCanvasItemsControl` Task 2 introduced), then the
  first `ContentControl` container inside it whose `DataContext` is a `MetricTileViewModel`,
  and `Keyboard.Focus()`es it, then `WaitFrames()`.
- `tools/Stats.UiPreview/captures/baseline.json` — three new entries, `dense` scenario,
  1180×720, `Method: "rtb"`, under `artifacts/ui-polish/layout/`, inserted directly after the
  existing `v13-layout-free-placed-*` entries: `layout-resized` × Dark Amber
  (`v14-layout-resized-dark-amber.png`) and Light (`v14-layout-resized-light.png`), and
  `layout-free+layout-resize-grip` × Dark Amber (`v14-layout-resize-grip-dark-amber.png`).
- `tests/Stats.UiPreview.Tests/TileResizeSubstateTests.cs` — new file, 4 tests (the four named
  in the spec's Acceptance section): `SubstateCatalog_AcceptsBothTileResizeSubstates_ForDashboard`,
  `SubstateCatalog_RejectsTileResizeSubstates_ForOtherViews` (theory: picker/settings/fans),
  `LayoutResized_SeedsEveryTilePosition_AppliesTheThreeSizes_AndExtendsTheCanvas`,
  `IsResizable_TrueForEveryTileInFree_FalseAfterSwitchingBackToAuto`. 203 → total in
  `Stats.UiPreview.Tests` (199 pre-existing + 4 new).
- `docs/tile-resize/EVIDENCE.md` — this file (new directory).

## Design decisions / notes

- **Which three tiles, and why**: the spec asks for "three seeded tiles: one to S, one to L,
  one S→M" from tiles the `dense` fixture actually has. Reading `Scenarios.Dense()`
  (`tools/Stats.UiPreview/Fixtures/Scenarios.cs`), each non-core tile is added via a local
  `Tile()` helper that assigns `Size = sizes[n % sizes.Length]` with `sizes = [S, M, L]` and `n`
  incrementing per call — so the fixture's first three tiles (`cpu.temp.tctl`,
  `cpu.power.package`, `cpu.power.tdc`, which are also `Dashboard.Tiles[0..2]`, confirmed by the
  existing `DashboardLayoutSubstateTests`/`docs/layout-modes/EVIDENCE.md` visual evidence for
  `layout-free-placed`) start at S, M, L respectively. `ApplyLayoutResized` therefore does:
  - `cpu.temp.tctl`: S → M ("one S-to-M")
  - `cpu.power.package`: M → L ("one to L")
  - `cpu.power.tdc`: L → S ("one to S")

  reusing the same three tiles `ApplyLayoutFreePlaced` already repositions, so both substates
  read from one well-understood set of ids.
- **Finding the Free container for `layout-resize-grip`**: a broad "first `FrameworkElement`
  whose `DataContext` is a `MetricTileViewModel`" search (the pattern `OpenFirstTileContextMenu`
  uses) is not safe here, because Auto's `SectionsList` also realizes `ContentPresenter`
  containers with the same `DataContext` type even while `Collapsed` (bound to
  `IsAutoLayout`/its inverse), and `Keyboard.Focus` on a collapsed/invisible element is a
  documented no-op in WPF — it would silently fail to focus anything and the grip would never
  render. Scoping the search to the `FreeTilesList` element's own subtree (found by its
  `x:Name`, the same technique `SelectSettingsCategory`/`OpenThemeDropdown` already use for
  `SettingsCategoryTabs`/the theme `ComboBox`) guarantees the visible, focusable Free container
  is the one found.
- **Substate combination**: `layout-resize-grip`'s `baseline.json` entry uses
  `"Substate": "layout-free+layout-resize-grip"` — the same `"+"`-combination convention the
  existing `"theme-dropdown+category-appearance"` entries use — so `PreviewComposition`'s
  `"layout-free"` case switches `LayoutMode` to `Free` (composition-time) before `CaptureHost`'s
  `"layout-resize-grip"` case focuses the first container (visual-tree time, after the window
  exists).

## Commands and results

| Command | Exit | Warnings | Result |
| --- | --- | --- | --- |
| `dotnet build --nologo` | 0 | 0 | Build succeeded — 5 projects |
| `dotnet test --nologo` | 0 | 0 | `Stats.Core.Tests`: 816/816 passed. `Stats.UiPreview.Tests`: 203/203 passed (199 pre-existing + 4 new `TileResizeSubstateTests`) |
| `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json` | 0 | 0 across every sidecar | 67/67 manifest entries succeeded (72 files written — the `theme-cycle` entry expands to 6), **0 failed** |

## Pre/post-resize pixel comparison

`artifacts/` is git-ignored and this is a fresh worktree, so no pre-existing capture PNGs were
present to compare against directly. Per this task's instructions (git-stash-free method): the
three harness source files this task touches
(`PreviewComposition.cs`/`Views/CaptureHost.cs`/`SubstateCatalog.cs`) were temporarily
overwritten with their **committed HEAD** content (`git show HEAD:<path> > <path>` — no
`git stash`, no branch switch, no commit), producing a genuine build of Task 1+2's state. That
build ran the 7 affected captures (the Auto `dense` dashboard capture,
`artifacts/ui-polish/before/v2-dashboard-dense.png`, plus the six existing
`layout-free`/`layout-grid`/`layout-free-placed` × Dark Amber/Light entries under
`artifacts/ui-polish/layout/`) into their normal output paths; those seven PNGs were copied to
`artifacts/ui-polish/layout/pre-resize/`. The three files were then restored from an
in-session backup (verified via `git diff --stat` to match this task's actual changes
byte-for-byte), the project rebuilt (0 warnings), and the full `baseline.json` batch above was
run, overwriting the same 7 outputs. Pre- and post-batch PNGs were then compared by SHA-256.

| Capture | Pre-resize SHA-256 | Post-batch SHA-256 | Result |
| --- | --- | --- | --- |
| `v2-dashboard-dense.png` (Auto) | `42e915b5…9a8e2141` | `42e915b5…9a8e2141` | **Identical** |
| `v13-layout-free-dark-amber.png` | `ffb17b6c…91514abcdb` | `ffb17b6c…91514abcdb` | **Identical** |
| `v13-layout-free-light.png` | `341dc945…f0506d82503ce` | `341dc945…f0506d82503ce` | **Identical** |
| `v13-layout-grid-dark-amber.png` | `5d584688…5422dfa84f987e` | `5d584688…5422dfa84f987e` | **Identical** |
| `v13-layout-grid-light.png` | `4fdc9484…fbee800885a1d91` | `4fdc9484…fbee800885a1d91` | **Identical** |
| `v13-layout-free-placed-dark-amber.png` | `ea9d6143…733ad23cae3ca48` | `ea9d6143…733ad23cae3ca48` | **Identical** |
| `v13-layout-free-placed-light.png` | `5dbcba7b…8718eae3db920f4bd` | `5dbcba7b…8718eae3db920f4bd` | **Identical** |

All seven are byte-identical. This confirms two things: (1) this task's harness-only additions
(new substates, new `baseline.json` entries, no changes to any existing `case` body) do not
alter any existing capture's rendering, exactly as expected since nothing existing was edited;
and (2) Task 2's App-level change (the Free canvas becoming a `TileCanvasItemsControl` with
`ContentControl` containers plus the resize grip's `ControlTemplate`) does not perturb the Auto
path (untouched by design) or the Free/Grid captures when no tile is hovered or keyboard-focused
(the grip's `Visibility` triggers both evaluate false), matching the spec's owner decision #2/#3
("the Auto `ItemsControl` and every `DataTemplate` are untouched … the grip is visible on hover
and keyboard focus, hidden otherwise").

## Visual evidence

All three new captures: scenario `dense`, 1180×720 logical, DPI 96, UI scale 1.0, method `rtb`,
`Warnings: []` in every sidecar.

| Case | PNG | Theme | Observation |
| --- | --- | --- | --- |
| `layout-resized` | `artifacts/ui-polish/layout/v14-layout-resized-dark-amber.png` | Dark Amber | Mixed sizes with top-left anchoring, as designed: Tctl/Tdie (S→M) now shows a sparkline where it previously showed only a value; Package Power (PPT) (M→L) is now a wide gauge tile whose enlarged footprint visually overlaps into TDC Current's old slot (position never changes on resize — owner decision #8, overlap allowed); TDC Current (L→S) shrank to a small value-only box, leaving empty canvas behind/right of it where its old L footprint used to be. A pixel-diff against the unresized `layout-free` capture (below) confirms the only changed region is exactly this row. |
| `layout-resized` | `artifacts/ui-polish/layout/v14-layout-resized-light.png` | Light | Same mixed-size/overlap arrangement, Light theme — white tile backgrounds, dark text, amber/brown accent preserved on the gauge/sparklines. |
| `layout-resize-grip` | `artifacts/ui-polish/layout/v14-layout-resize-grip-dark-amber.png` | Dark Amber | Same layout as the plain `layout-free` capture (sizes unchanged — this substate doesn't resize anything, only focuses), but the first tile (Tctl/Tdie, S-sized) now shows the resize-grip glyph at its bottom-right corner. A pixel-diff against `v13-layout-free-dark-amber.png` (identical substate, minus the focus) isolates the change to a single 12×12 pixel region at (167,349)-(178,360) — precisely the tile's bottom-right corner, 57 pixels total — with every other pixel in the 1180×720 frame unchanged. This confirms the grip's `IsKeyboardFocusWithin` trigger (DashboardWindow.xaml) is the only thing rendering, and it renders in exactly the place the design specifies (14×14 border, margin `0,0,8,8`, bottom-right of the tile). |

Full sidecar JSONs (`*.json` next to each PNG) record source commit, fixture time/seed, DPI,
culture, exact launch command, and simulated-services list per `PREVIEW_HARNESS.md`'s contract.

## What the harness could not show

Copied verbatim from the design spec's "Preview harness" section ("Cannot be captured" bullet):

> **Cannot be captured** (owner checklist): the in-flight outline adorner and the cursor shape
> (no `SendInput`), the snap on release, Esc-cancel, Ctrl+Plus/Minus (modifiers are read from
> the real device), focus restoration, and the UI-scale 1:1 correction.

These are implemented in Task 2 (`src/Stats.App/Controls/ResizeOutlineAdorner.cs`,
`DashboardWindow.xaml.cs`'s `FreeTile_PreviewMouseMove`/`CommitFreeResize`/`FocusTileById`) and
covered at the view-model level by `tests/Stats.Core.Tests` (`DashboardViewModelTests`,
`DashboardLayoutModeTests`), but none of them are demonstrable from a static screenshot — they
need a real interactive run to verify, and belong on the PR owner checklist per the design's
Acceptance section:

- **In-flight outline adorner / cursor shape** — `ResizeOutlineAdorner` draws the dashed raw
  rectangle and the accent candidate outline with the S/M/L letter only while a drag is in
  progress; a settled capture only shows before/after states.
- **Snap on release** — the harness applies the final size directly via `SetTileSize` (the same
  call a real drag-release makes), never exercising the drag-to-nearest-preset computation
  itself; that's covered by `TileDimensionsTests.Nearest_*` at the unit level, not visually.
- **Esc-cancel** — requires a real key event mid-drag; not exercised here.
- **Ctrl+Plus/Minus** — modifiers are read from the real input device
  (`Keyboard.Modifiers`/routed `KeyDown`), which this headless harness cannot synthesize
  reliably; `StepTileSize`/`TileDimensions.Step` are covered at the VM/Core level instead.
- **Focus restoration after a size-triggered rebuild** — `FocusTileById`'s
  `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, …)` re-focus needs a real rebuild+render
  cycle timed against actual dispatcher priorities; the harness's own
  `layout-resize-grip` substate sidesteps this by focusing directly, once, after the substates
  are already applied — it does not exercise the restore-after-rebuild path.
- **UI-scale 1:1 correction** — the canvas-coordinate delta fix for `FreeTile_*`/
  `CoreMatrixBlock_*` move drags only matters while a mouse is actually moving; a static capture
  at any single `--ui-scale` value can't show whether deltas track 1:1, only that layout renders
  correctly at that scale (already covered by the pre-existing `v3-*-scale13`/`v5-*-scale09`
  captures, unaffected by this branch).

None of the above are blockers for this task (its contract is the preview harness, catalog
entries, captures, and this evidence report); they are flagged here so they land on the Task 4
(PR) owner checklist, per the design's Acceptance section.

## Gate outcomes

| Gate | Pass / fail / blocked | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors |
| Tests | Pass | `dotnet test --nologo` — 816/816 (`Stats.Core.Tests`) + 203/203 (`Stats.UiPreview.Tests`), 0 failures, 0 warnings |
| Captures | Pass | 67/67 `baseline.json` entries succeeded (72 files written), 0 failed, `Warnings: []` in every sidecar |
| Visual (`layout-resized`) | Pass | Mixed S/M/L sizes, top-left anchoring, overlap where a tile grew, empty space where one shrank — see table above |
| Visual (`layout-resize-grip`) | Pass | Grip glyph renders at the first tile's bottom-right corner on keyboard focus; pixel-diff isolates the change to exactly that 12×12 region |
| Regression (Auto unchanged) | Pass | `v2-dashboard-dense.png` byte-identical (SHA-256) pre- vs post-Task-3 |
| Regression (Free/Grid/Free-placed unchanged) | Pass | All six `v13-layout-*.png` entries byte-identical (SHA-256) pre- vs post-Task-3 |
