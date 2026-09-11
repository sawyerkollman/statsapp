# Dashboard layout modes — implementation evidence

Follows the structure of `docs/ui-polish/EVIDENCE_TEMPLATE.md`, scoped to this feature's four tasks
(plan: `docs/superpowers/plans/2026-09-11-dashboard-layout-modes.md`; design/contract:
`docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md`).

## Candidate

- Date/time: 2026-09-11
- Repo/branch: `C:\claude-projects\Stats`, `feature/dashboard-layout-modes`
- Baseline commit: `75115aa` (v1.9.2, base the design was written against)
- Final candidate commit (Task 4, harness + docs): built on `ecc6f38` (Task 3 — free/snap canvas)
- Dirty files/diff identity: working tree has Task 4's uncommitted changes (harness substates,
  `baseline.json`, new test file, `README.md`, this file) — not committed by this task per its
  instructions
- Windows version, .NET SDK: Windows 11 Pro 10.0.26220, `dotnet 9.0.316` (repo targets `net8.0-windows`)
- Desktop availability: yes — captures below used `--method rtb` (RenderTargetBitmap); `--method
  screen` was reported blank in an earlier session for this environment (per
  `docs/ui-polish/PREVIEW_HARNESS.md` guidance to record the validated method), so this task used
  `rtb` throughout instead.
- Overall status: implemented / verified (build + tests green) / visually accepted (captures opened
  and inspected below)

## Task ledger

| Task | Delivered | Files | Validation |
| --- | --- | --- | --- |
| T1 | `DashboardLayoutMode` enum, `TilePref.X/Y`, `AppSettings.CoreMatrixX/Y`, `TileDimensions`, `DashboardLayout` snap/clamp | `src/Stats.Core/Settings/DashboardLayoutMode.cs`, `TilePref.cs`, `AppSettings.cs`, `TileDimensions.cs`, `src/Stats.Core/ViewModels/DashboardLayout.cs`, `MetricTileViewModel.cs` | `tests/Stats.Core.Tests/DashboardLayoutTests.cs`, `SettingsServiceTests.cs` |
| T2 | `LayoutMode` switching, `SetTilePosition`/`SetCoreMatrixPosition`/`SetCoreMatrixSize`, seed pack, canvas extent, `StatusLines` | `src/Stats.Core/ViewModels/DashboardViewModel.Layout.cs`, `DashboardViewModel.cs` | `tests/Stats.Core.Tests/DashboardLayoutModeTests.cs` |
| T3 | Free/Snap whole-dashboard canvas, drag, keyboard nudge, View menu items | `src/Stats.App/Views/DashboardWindow.xaml(.cs)`, `Converters/AllTrueToVisibilityConverter.cs`, `Views/Theme.xaml` | `tests/Stats.Core.Tests/DashboardLayoutModeTests.cs` (extended) |
| T4 (this task) | Preview-harness substates, `baseline.json` entries, harness tests, captures, README, this evidence file | see "Files changed" below | `dotnet build`/`dotnet test` below; captures inspected below |

## Files changed (Task 4)

- `tools/Stats.UiPreview/SubstateCatalog.cs` — added `layout-free`, `layout-grid`,
  `layout-free-placed` to the `dashboard` view's valid-substate list.
- `tools/Stats.UiPreview/PreviewComposition.cs` — `ApplySubstate` gained the three cases; a new
  `ApplyLayoutFreePlaced` helper seeds Free layout then explicitly repositions three tiles
  (including one deliberately overlapping pair) and drags the core-matrix block off its seeded spot.
- `tools/Stats.UiPreview/captures/baseline.json` — six new entries (`layout-free`, `layout-grid`,
  `layout-free-placed`, each Dark Amber and Light, 1180×720, `dense` scenario, `Method: "rtb"`),
  appended after the existing `v12-overlay-vertical` entry, writing into a new
  `artifacts/ui-polish/layout/` output folder.
- `tests/Stats.UiPreview.Tests/DashboardLayoutSubstateTests.cs` — new file: `SubstateCatalog`
  accepts the three substates for `dashboard` and rejects them for other views;
  `layout-free`/`layout-grid` seed every tile and the core-matrix block with a non-null position
  (grid positions additionally checked as exact multiples of 16); `layout-free-placed` has one
  exact-duplicate-position tile pair and a core-matrix block moved off `(0,0)`; every substate
  produces a positive canvas extent. 10 new tests (172 → 182 in `Stats.UiPreview.Tests`).
- `README.md` — new "Dashboard layout" bullet under "Use" describing Auto arrange / Free / Snap to
  grid, drag, keyboard nudge (Shift = 4 steps), 16 px snap, Reset tile positions…, and per-tile
  persistence.
- `docs/layout-modes/EVIDENCE.md` — this file.

## Design decision: where the mode/positions are set

The design's "Preview harness" section left the choice open ("set `DashboardLayoutMode` before the
composition builds the dashboard VM, or set `vm.LayoutMode` afterwards — check which the harness
makes easy"). Setting `composition.Dashboard.LayoutMode` **after** the VM is built was the easier,
harness-native path: `PreviewComposition.ApplySubstate` already runs after `DashboardViewModel` is
constructed (see `PreviewComposition.Build`, substates applied in the loop at the end), and the
`LayoutMode` property setter's `OnLayoutModeChanged` hook already does exactly what's needed —
persists to `AppSettings.DashboardLayoutMode`, calls `RebuildSections()` (which runs
`PlaceUnpositioned()`, the seed pack, whenever `LayoutMode != Auto`), and re-raises
`IsAutoLayout`/`IsGridLayout`. No composition-time settings plumbing was needed.

## Commands and results

| Command | Exit | Warnings | Result |
| --- | --- | --- | --- |
| `dotnet build --nologo` | 0 | 0 | Build succeeded — 5 projects |
| `dotnet test --nologo` | 0 | 0 | `Stats.Core.Tests`: 739/739 passed. `Stats.UiPreview.Tests`: 182/182 passed (172 pre-existing + 10 new `DashboardLayoutSubstateTests`) |
| `dotnet run --project tools/Stats.UiPreview -- --batch <7-entry manifest>` | 0 | 0 across all 7 | 7/7 captures written, 0 failed, `Warnings` empty in every sidecar |

The 7-capture batch run used a scratch manifest (not `baseline.json` itself, to avoid re-running the
~60 pre-existing `before/` entries against this environment) containing the six new `baseline.json`
entries plus a seventh ad hoc entry: `dense` scenario, `dashboard` view, default (Auto) substate,
Dark Amber, `rtb`, output `artifacts/ui-polish/layout/v13-layout-auto-dense-dark-amber.png` — the
"Auto unchanged" comparison capture. Each of the six `baseline.json` entries can be reproduced
individually with, e.g.:

    dotnet run --project tools/Stats.UiPreview -- --scenario dense --view dashboard --substate layout-free --theme "Dark Amber" --width 1180 --height 720 --method rtb --output artifacts/ui-polish/layout/v13-layout-free-dark-amber.png

## Visual evidence

All captures: scenario `dense` (16-core matrix + CPU/GPU/memory/storage/network misc tiles),
1180×720 logical, DPI 96, UI scale 1.0, method `rtb`, `Warnings: []` in every sidecar.

| Case | PNG | Theme | Observation |
| --- | --- | --- | --- |
| `layout-free` | `artifacts/ui-polish/layout/v13-layout-free-dark-amber.png` | Dark Amber | Group headers are gone; one scrollable canvas holds the core-matrix block at top-left and every tile packed left-to-right, wrapping into rows below it — the seed pack's row layout is visible and no two tiles overlap. |
| `layout-free` | `artifacts/ui-polish/layout/v13-layout-free-light.png` | Light | Same packed layout and scroll behavior as the Dark Amber capture, themed correctly (white tile backgrounds, dark text, amber accent preserved on gauges/sparklines). |
| `layout-grid` | `artifacts/ui-polish/layout/v13-layout-grid-dark-amber.png` | Dark Amber | Visually identical packing to `layout-free` at this zoom (the seed pack's row/column steps are already multiples of 4, so 12px gaps land on 16px grid lines) — snap alignment is confirmed by the harness test (`tile.X/Y % 16 == 0`) rather than being visually distinguishable from Free in a static screenshot. |
| `layout-grid` | `artifacts/ui-polish/layout/v13-layout-grid-light.png` | Light | Same as above, Light theme. |
| `layout-free-placed` | `artifacts/ui-polish/layout/v13-layout-free-placed-dark-amber.png` | Dark Amber | The two explicitly-overlapping tiles (Tctl/Tdie, Package Power) scrolled out of the visible viewport at (700,420); the "TDC Current" tile is visibly moved to the top-right (980,60), and the core-matrix block's colored core badges are visible peeking in at the bottom-right edge, moved off its seeded (0,0) position to (760,540) — canvas is noticeably wider/taller than `layout-free`'s (horizontal scrollbar present), consistent with tiles/block being moved outward. Overlap itself is confirmed by the harness test asserting the two tiles share an exact X/Y, since the overlapping pair scrolled outside this particular viewport. |
| `layout-free-placed` | `artifacts/ui-polish/layout/v13-layout-free-placed-light.png` | Light | Same moved/overlapping arrangement, Light theme. |
| Auto (comparison) | `artifacts/ui-polish/layout/v13-layout-auto-dense-dark-amber.png` | Dark Amber | **Auto unchanged**: compared against `artifacts/ui-polish/after/v2-dashboard-dense.png` (the pre-existing UI-polish "after" gallery capture for the same `dense` scenario). Group headers ("Cpu (4)", "Gpu (7)"), core-matrix cell values/colors, tile order, tile values, and pixel layout in the client area are identical between the two — the only visible difference is that the `after/` capture used `--method screen` and so includes the native window title bar/chrome, while this task's `--method rtb` capture is the client area only. Below the title bar the two images match exactly. |

Full sidecar JSONs (`*.json` next to each PNG) record source commit, fixture time/seed, DPI,
culture, exact launch command, and simulated-services list per PREVIEW_HARNESS.md's contract.

## What the harness cannot show (owner checklist)

Per the design's Acceptance section ("Owner checklist: drag feel, snapping, keyboard nudge, Details
on double-click in Free mode, mode persistence across restart, core-matrix block drag"), the
following are implemented (see Task 3) and covered by `DashboardLayoutModeTests.cs` at the
view-model level, but a static screenshot cannot demonstrate them and they need a real interactive
run to verify:

- **Drag feel** — live `Canvas.Left/Top` feedback while dragging (no VM writes per pixel, committed
  once on mouse-up); a screenshot only shows a settled end state, never the in-flight motion.
- **Keyboard nudge** — arrow keys move a focused tile by `GridSize` (16px), Shift+arrow by 4 steps
  (64px); requires actual key events against a focused container, not just a rendered frame.
- **Snap-vs-Free visual distinction under drag** — this task's static `layout-grid` capture looks
  the same as `layout-free` (see table above) because the seed pack's own placements already land
  on 16px lines; snapping is only visually obvious for an *odd* dropped/nudged position, which needs
  an interactive drag to a non-grid-aligned point.
  Confirmed instead by `DashboardLayoutSubstateTests.LayoutGrid_SetsGridMode_AndEverySeededPositionIsSnapped`
  and by `DashboardLayoutModeTests` in `Stats.Core.Tests`.
- **Details on double-click in Free mode** — the Free/Snap container reuses the same
  `Button.Click`/double-click EventSetters as Auto; not exercised by this capture batch.
- **Mode persistence across restart** — `DashboardLayoutMode`/`TilePref.X/Y`/`CoreMatrixX/Y` are
  written to `settings.json` (see `SettingsServiceTests.cs` for round-trip coverage), but only a real
  app restart proves the window actually reopens in the last-used mode with the last-dragged
  positions.
- **Core-matrix block drag** — this task moved the block programmatically via
  `SetCoreMatrixPosition` (same code path a real drag ends in) to produce the `layout-free-placed`
  capture; the mouse-drag interaction itself needs an interactive run.

None of the above are blockers for this task (T4's contract is the preview harness and docs); they
are flagged here so they land on the PR owner checklist per the design's Acceptance section.

## Gate outcomes

| Gate | Pass / fail / blocked | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors |
| Tests | Pass | `dotnet test --nologo` — 739/739 (`Stats.Core.Tests`) + 182/182 (`Stats.UiPreview.Tests`), 0 failures |
| Preview isolation | Pass (unchanged) | `IsolationMetadataTests`/`CompositionIsolationTests` still pass; the new substates touch only `AppSettings`/`DashboardViewModel`, no new service dependency |
| Captures | Pass | 7/7 written, 0 warnings in any sidecar |
| Visual (Auto unchanged) | Pass | See table above — Auto's client-area content is pixel-identical to `after/v2-dashboard-dense.png` |
| Visual (Free/Grid/Free-placed) | Pass | Canvas visible, no group headers, packed rows, overlap confirmed by test (scrolled out of the placed-mode viewport) |
