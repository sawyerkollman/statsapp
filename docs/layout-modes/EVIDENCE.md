# Dashboard layout modes — implementation evidence

Follows the structure of `docs/ui-polish/EVIDENCE_TEMPLATE.md`, scoped to this feature's four tasks
(plan: `docs/superpowers/plans/2026-09-11-dashboard-layout-modes.md`; design/contract:
`docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md`).

## Candidate

- Date/time: 2026-09-11
- Repo/branch: `C:\claude-projects\Stats`, `feature/dashboard-layout-modes`
- Baseline commit: `75115aa` (v1.9.2, base the design was written against)
- Final candidate commit: **branch tip + this fix wave (uncommitted)** — `docs/layout-modes/REVIEW.md`'s
  whole-branch review at `c301dff`, plus the fix wave addressing its blockers/should-fix/nits (B1,
  B1b, B2, S1-S11, N1-N3, N5-N7; N4 skipped). Not committed per this task's instructions.
- Dirty files/diff identity: working tree has the fix wave's uncommitted changes (see
  `docs/layout-modes/REVIEW.md`'s "Fix wave" section for the full file list) — not committed by this
  task per its instructions
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
| `dotnet test --nologo` | 0 | 0 | `Stats.Core.Tests`: 759/759 passed (740 pre-fix-wave + 19 new/rewritten for B1b/S2/S3/S7/S8/S9/S10/S11). `Stats.UiPreview.Tests`: 182/182 passed (S8's rewritten test is one of the 182, not an addition) |
| Seven individual capture runs (below) | 0 | 0 across all 7 | 7/7 captures re-written, 0 failed, `Warnings` empty in every sidecar |

The 7-capture batch run used a scratch manifest (not `baseline.json` itself, to avoid re-running the
~60 pre-existing `before/` entries against this environment) containing the six new `baseline.json`
entries plus a seventh ad hoc entry: `dense` scenario, `dashboard` view, default (Auto) substate,
Dark Amber, `rtb`, output `artifacts/ui-polish/layout/v13-layout-auto-dense-dark-amber.png` — the
"Auto unchanged" comparison capture. Each of the six `baseline.json` entries can be reproduced
individually with, e.g.:

    dotnet run --project tools/Stats.UiPreview -- --scenario dense --view dashboard --substate layout-free --theme "Dark Amber" --width 1180 --height 720 --method rtb --output artifacts/ui-polish/layout/v13-layout-free-dark-amber.png

## Visual evidence

**Candidate for this section: branch tip + this fix wave (uncommitted)** — B1/B1b/B2 and the
should-fix items below, re-captured after the fix (the previous revision of this section described
the pre-fix captures and was factually wrong per `docs/layout-modes/REVIEW.md`; see that file's "Fix
wave" section for the full item list).

All captures: scenario `dense` (16-core matrix + CPU/GPU/memory/storage/network misc tiles),
1180×720 logical, DPI 96, UI scale 1.0, method `rtb`, `Warnings: []` in every sidecar, re-run after
the fix wave (`SourceCommit`/`DirtyDiffIdentity` in each sidecar JSON reflect the branch tip plus the
uncommitted fix-wave diff).

| Case | PNG | Theme | Observation |
| --- | --- | --- | --- |
| `layout-free` | `artifacts/ui-polish/layout/v13-layout-free-dark-amber.png` | Dark Amber | **B1/B2 fixed, confirmed visually.** The core-matrix block ("Cores — load · clock · temp") occupies y≈75…258; row 1 of tiles (Tctl/Tdie, Package Power, TDC Current) now starts at y≈285, fully below the block — no overlap, where the pre-fix capture had row 1 at y≈73 covering the block's bottom two-thirds. The left edge of both the block and every tile sits at x≈16 (the canvas's own margin), i.e. flush with the window's left edge — not the ≈143px centred gutter the pre-fix capture showed (B2's `HorizontalAlignment="Left"` fix). |
| `layout-free` | `artifacts/ui-polish/layout/v13-layout-free-light.png` | Light | Same corrected layout (block above, row 1 clear below it, left-anchored) in Light theme — white tile backgrounds, dark text, amber/brown accent preserved on gauges/sparklines. |
| `layout-grid` | `artifacts/ui-polish/layout/v13-layout-grid-dark-amber.png` | Dark Amber | **B1b fixed.** Same corrected below-the-block layout as `layout-free` (visually indistinguishable from Free at this zoom, since the seed pack's own placements already land on 16px lines); grid alignment after a simulated `SetCoreMatrixSize` re-measurement is confirmed on-grid by `DashboardLayoutSubstateTests.LayoutGrid_SetsGridMode_AndEverySeededPositionIsSnapped`'s new post-shift assertion (S8) and by `DashboardLayoutModeTests.SetCoreMatrixSize_GridMode_ReSnapsTheShiftedPosition` (B1b) — neither of which the pre-fix code passed. |
| `layout-grid` | `artifacts/ui-polish/layout/v13-layout-grid-light.png` | Light | Same as above, Light theme. |
| `layout-free-placed` | `artifacts/ui-polish/layout/v13-layout-free-placed-dark-amber.png` | Dark Amber | The deliberately overlapping pair is plainly visible in the empty top-middle area: Package Power (PPT) at (680,110) sits over the lower-right of Tctl/Tdie at (600,40). TDC Current is at the top-right, the core-matrix block has been moved to the bottom-right (its badges peek in at the bottom edge), and the seeded tiles remain packed below the block's original slot. Overlap is allowed by design and is also asserted by `DashboardLayoutSubstateTests.LayoutFreePlaced_HasOneDeliberatelyOverlappingPair_AndAMovedCoreMatrixBlock` (bounding boxes intersect). |
| `layout-free-placed` | `artifacts/ui-polish/layout/v13-layout-free-placed-light.png` | Light | Same moved/overlapping arrangement, Light theme. |
| Auto (comparison) | `artifacts/ui-polish/layout/v13-layout-auto-dense-dark-amber.png` | Dark Amber | **Auto unchanged, re-confirmed.** Group headers ("Cpu (4)", "Gpu (7)"), core-matrix cell values/colors, tile order/values, and client-area layout are unchanged by the fix wave — B1/B1b/B2/S1-S11 only touch Free/Snap's canvas and position-tracking code, none of which Auto's `RebuildSections` path (group sections, WrapPanel) exercises. Matches the pre-fix capture and `artifacts/ui-polish/after/v2-dashboard-dense.png` below the title bar. |

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
| Tests | Pass | `dotnet test --nologo` — 759/759 (`Stats.Core.Tests`) + 182/182 (`Stats.UiPreview.Tests`), 0 failures, 0 warnings |
| Preview isolation | Pass (unchanged) | `IsolationMetadataTests`/`CompositionIsolationTests` still pass; the fix wave touches only `AppSettings`/`DashboardViewModel`/`DashboardWindow`, no new service dependency |
| Captures | Pass | 7/7 re-written after the fix wave, 0 warnings in any sidecar |
| Visual (Auto unchanged) | Pass | Re-confirmed after the fix wave — see table above |
| Visual (B1/B1b/B2 fixed) | Pass | Row 1 of tiles now sits fully below the core-matrix block in both Free and Grid captures (was overlapping pre-fix), and the canvas is left-anchored at x≈16 in both (was centred with a ≈143px gutter pre-fix) — see table above |
| Visual (Free-placed overlap) | Pass | The overlapping pair (Tctl/Tdie under Package Power) is visible in both placed captures; also asserted by `DashboardLayoutSubstateTests.LayoutFreePlaced_HasOneDeliberatelyOverlappingPair_AndAMovedCoreMatrixBlock` |
