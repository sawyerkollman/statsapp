# Game tiles — implementation evidence

Task 4 (harness + docs) of `docs/superpowers/plans/2026-09-11-game-tiles.md`, per
`docs/superpowers/specs/2026-09-11-game-tiles-design.md`'s "Preview harness" and "Acceptance → Captures /
harness tests / owner checklist" sections. Format of `docs/graph-effects/EVIDENCE.md` — this feature has no
separate routing/task ledger, so only the sections that apply are kept.

## Candidate

- Date/time: 2026-09-12 (Windows 11 Pro, this session's worktree).
- Repo/branch: `feature/game-tiles` (worktree `C:\claude-projects\Stats-wt\game-tiles`).
- Candidate commit: branch tip `51b0746` ("feat(app): histogram and FPS-summary tiles", Task 3) **+ this task's
  uncommitted files** (Task 4 was cut off mid-way by a previous implementer; this pass finished it on top of the
  same tree, not a new commit):
  - `tools/Stats.UiPreview/Fixtures/Scenarios.cs` — `game` scenario (already complete from the prior pass, kept
    as-is).
  - `tools/Stats.UiPreview/SubstateCatalog.cs` — the four `game-*` dashboard substates allow-listed (already
    complete, kept as-is).
  - `tools/Stats.UiPreview/PreviewComposition.cs` — the `game-tiles`/`game-tiles-large`/`game-tiles-small`/
    `game-summary-orphan` `case`s were already wired in the switch, but they called a not-yet-written
    `ApplyGameTiles(c)` helper (`error CS0103`, harness did not compile); this pass added that helper.
  - `tools/Stats.UiPreview/captures/baseline.json` — 10 new entries under `artifacts/game-tiles/` (this pass).
  - `tests/Stats.UiPreview.Tests/GameTilesSubstateTests.cs` (new, this pass).
  - `README.md` — Tiles paragraph + new Game-tiles bullet (this pass).
- .NET SDK: 9.0.316 (targeting `net8.0-windows`); Windows 11 Pro 10.0.26220.
- Overall status: implemented, captured, visually inspected, all gates green.

## What changed in the harness (Task 4 scope)

- `tools/Stats.UiPreview/PreviewComposition.cs`: added the missing `ApplyGameTiles(PreviewComposition c)` helper
  (private static, next to `ApplyLayoutFreePlaced`). It adds `FrameMetrics.FrameTimeId` to
  `Settings.DashboardMetrics` when not already present, sets `PrefFor(FpsId).Kind = FpsSummary` and
  `PrefFor(FrameTimeId).Kind = Histogram` through the public pref API, then calls `Dashboard.RebuildSections()` —
  exactly the "settings + RebuildSections" shape every other settings-level substate in this file uses. The
  `"game-tiles"` `case` was already calling this helper (uncommitted from the prior pass); `"game-tiles-large"`/
  `"game-tiles-small"` layer a `TileSize` override and their own `RebuildSections()` call on top, unchanged from
  what was already there. Nothing else in the file changed — the `game` scenario builder, the substate catalog
  entries, and the other three `case` bodies were already correct and are kept verbatim.
- `tools/Stats.UiPreview/captures/baseline.json`: 10 new entries (below). Two needed a taller `Height` than the
  spec's literal table cell — see "Deviation from the spec's literal capture heights" below.
- `tests/Stats.UiPreview.Tests/GameTilesSubstateTests.cs` (new, 8 tests): `SubstateCatalog_AcceptsGameSubstates_ForDashboard`,
  `SubstateCatalog_RejectsGameSubstates_ForOtherViews` (theory, 4 other views), `GameScenario_PresetsFpsSummaryAndHistogramPrefs`,
  `GameScenario_FrameTimeSeriesHasSpikes_AndP99AboveBaseline`, `GameTilesSubstate_OnMissing_AddsFrameTime_AndShowsDashes`,
  `GameTilesLargeAndSmall_SetSizes` (theory, L/S), `GameSummaryOrphan_OnGallery_SetsFpsSummary_WithDashSlots`,
  `GameScenario_GameSectionIsLast_AndHasThreeTiles` — 12 test cases total (theories expand), asserting on
  `PreviewComposition.Settings`/`Dashboard.Tiles`/`Dashboard.Sections`, the same composition-only, no-WPF-window
  style every other substate test in this project uses.
- `README.md`: "Tiles" paragraph's kind list gains ", Histogram, plus FPS summary on the FPS tile"; new "Game
  tiles" bullet after the FPS-counter bullet describing what each kind shows and the downgrade hazard.

## Deviation from the spec's literal capture heights

The spec's capture table lists `1180×720 unless noted` and doesn't note a taller height for case 7
(`gallery`/`game-summary-orphan`) or case 8 (`missing`/`game-tiles`). At 720 px, neither capture actually shows
the tile the case exists to demonstrate:

- Case 7: `gallery`'s `Cpu` section alone has 13 tiles (the kind×size gallery plus the power-limit bar), which
  already exceeds 720 px of vertical space before the `Gpu`/`Storage`/`Game` sections even start — the
  `gallery.inverted` FPS-summary tile (the whole point of this capture) never enters the frame.
  Its `Height` is `1660` here (found by capturing at successively taller heights until the tile's ratio bar was
  fully visible with margin).
- Case 8: `missing`'s degraded/sensor-failure banners plus four groups (`Cpu`, `Gpu`, `Memory`, `Game`) push the
  `Game` section (the FPS-summary/Histogram all-gap tiles) past 720 px too. Its `Height` is `1100`.
  Every other case fits the default `1180×720` and is captured at that size, matching the table.
  This mirrors `docs/graph-effects/EVIDENCE.md`'s own precedent of documenting a deviation from the spec's
  literal capture parameters when the literal reading doesn't serve the capture's purpose.

## Captures

Command: `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`,
`"Method": "rtb"` on every entry. DPI 96×96, Invariant culture, fixed fixture time `2026-09-06T14:30:00Z`, seed
`1234`. Batch result: **74 manifest entries → 84 capture(s) written** (`v6-theme-cycle` and the new
`v14-dashboard-game-tiles-theme-cycle` entry each fan out into 6 themed captures), **0 entries failed**. Every
sidecar's `Warnings` array is empty (checked on all 15 new sidecars under `artifacts/game-tiles/` — the 9 single
captures plus the 6-frame `theme-cycle` fan-out; `smoke-dense.json`/`smoke-settings.json` from Task 3 left
untouched). All new captures under
`artifacts/game-tiles/` (git-ignored, matching `artifacts/graph-effects/`/`artifacts/ui-polish/`).

| # | Scenario/substate | Theme | Size | File | Observation |
| --- | --- | --- | --- | --- | --- |
| 1 | `game` / — | Dark Amber | 1180×720 | `v14-dashboard-game-tiles-dark-amber.png` | Game section (3 tiles) sits last, after Cpu(2)/Gpu(2); FPS summary shows "144 fps" large, "1% low 92 fps · 6.9 ms" small, and an orange ratio bar at "64% of avg"; the Histogram tile shows a short cluster of 12 bars with one tall bar at the far left (bin 0 holds most samples) with the p99 marker line standing alone near the right edge and the spike bins as thin slivers (the shaped spike data), a "p99 17.6 ms" marker/label above it, and range labels "6.5 ms"/"17.6 ms" under the bars. |
| 2 | `game` / — | Light | 1180×720 | `v14-dashboard-game-tiles-light.png` | Same layout and values on the Light preset; accent-orange bars/marker/ratio-bar read clearly against the light background, text stays legible. |
| 3 | `game` / `game-tiles-large` | Dark Amber | 1180×1000 | `v14-dashboard-game-tiles-large-dark-amber.png` | FPS and Frame Time tiles both render at L size (visibly wider/taller than the M "1% Low FPS" tile beside them); the FPS summary's ratio bar/caption row is pushed toward the bottom of the taller tile, consistent with L's extra vertical space. |
| 4 | `game` / `game-tiles-small` | Dark Amber | 1180×720 | `v14-dashboard-game-tiles-small-dark-amber.png` | FPS and Frame Time tiles both fall back to the plain `TileCompact` look (name + bare value only — "144 fps", "6.9 ms", no sparkline/bars/ratio bar), exactly like every other kind at S; the untouched "1% Low FPS" tile stays at M with its full sparkline. |
| 5 | `game` / `graphs-plain` | Dark Amber | 1180×720 | `v14-dashboard-game-tiles-plain-dark-amber.png` | Same data as case 1 with `SmoothLines`/`GraphEffects` off — histogram bars and the marker/ratio-bar read as flat fills without the gradient/highlight/glow case 1 has; still fully legible. |
| 6 | `game` / `graphs-warmup` | Dark Amber | 1180×720 | `v14-dashboard-game-tiles-warmup-dark-amber.png` | Store trimmed to the most recent 15 of 60 ticks (25%): the sparklines occupy only their track's right quarter; the Histogram's marker reads "p99 15.3 ms" (not 17.6 ms) because the tick-41 spike (12.9 ms) falls outside the last 15 samples while the tick-52 spike (15.3 ms) is still included — the bin/marker maths responding correctly to a smaller sample window. |
| 7 | `gallery` / `game-summary-orphan` | Dark Amber | 1180×1660 | `v14-dashboard-game-summary-orphan-dark-amber.png` | The `Game` section's lone tile ("Simulated FPS") shows "28 fps" with its own warn triangle, "1% low — · —" (both sibling slots dashed, no `fps.low1`/`fps.frametime` in this fixture), and a fully empty (0%) ratio bar — exactly the "no siblings" degradation the substate exists to prove. |
| 8 | `missing` / `game-tiles` | Dark Amber | 1180×1100 | `v14-dashboard-game-tiles-gap-dark-amber.png` | `fps.frametime` was added to `DashboardMetrics` by the substate (not selected by the `missing` fixture itself) and now shows a Histogram tile with baseline only — empty bars, no min/max text, no marker; the FPS tile shows a bare dash for its own value plus "1% low — · —" and an empty ratio bar, since `missing`'s `fps.avg` series ends in a full trailing gap and has no `fps.low1`/`fps.frametime` series at all. |
| 9 | `game` / `layout-free` | Dark Amber | 1180×720 | `v14-dashboard-game-tiles-layout-free-dark-amber.png` | Free/Snap layout auto-packs all 8 tiles (no `CoreMatrix` in this fixture) into a single row of 4×2; the FPS-summary and Histogram tiles render identically to the Auto-layout case, just repositioned onto the free canvas — proves the new kinds aren't layout-mode-specific. |
| 10 | `game` / `theme-cycle` | (fans out to 6) | 1180×720 | `v14-dashboard-game-tiles-theme-cycle-{0..5}-*.png` | Spot-checked dark-amber (0), dark-green (2), dark-blue (1), dark-purple (3), Light (4), dark-amber-custom (5): in every frame the Histogram's bars/marker and the FPS-summary's ratio bar recolour to that theme's accent (verified green/blue/purple/teal-custom directly, Light keeps the accent-orange legible on the light background) — confirms the control-drawn marker/label and the ratio bar both re-run through `ThemeManager.Changed`/`SeverityToBrush` rather than being baked in at first render. |

## What the harness could not show

- **The "Tile kind" submenu** with "Histogram"/"FPS summary" entries, the "FPS summary" gating (only offered on
  the FPS-role tile), and switching kinds live — `--method rtb` renders only the window's own visual tree; the
  `tile-menu` substate opens the context menu but not a hover submenu, and `--method screen` returned blank
  frames in this shell (per `docs/graph-effects/EVIDENCE.md`).
- **Live PresentMon values, a real stutter widening the histogram's tail, or the p99 marker moving during
  play** — everything here is a static, seeded fixture; CLAUDE.md rules 7–8 (PresentMon/ETW can't run and the
  app can't be launched from this shell) apply exactly as they did for graph-effects.
- **The 1.9.x downgrade behaviour** — needs an actually-installed older build reading a v1.10 `settings.json`;
  can't be simulated from a fixture that only ever runs the current build's code.
- **Idle CPU cost** with a Histogram/FpsSummary tile on screen — needs Task Manager against the running app.

All four are owner-checklist items (3, 2, 8, 5 respectively in the spec's numbering) rather than harness gaps
that could be closed with more fixture work. Composition-only tests
(`tests/Stats.UiPreview.Tests/GameTilesSubstateTests.cs`) cover the substates' VM/settings effects headlessly,
independent of whether the affected tile happens to sit inside a particular capture's viewport.

## Gate outcomes

| Gate | Result | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors (all 5 projects). |
| Tests | Pass | `dotnet test --nologo` — Stats.Core.Tests 836/836, Stats.UiPreview.Tests 219/219 (207 pre-existing + 12 new `GameTilesSubstateTests` cases), 0 failures, 0 warnings. |
| Captures | Pass | Full `baseline.json` batch: 74 entries → 84 capture(s) written, 0 failed; all 15 new sidecars under `artifacts/game-tiles/` have `Warnings: []`. |

## Final handoff

- Concrete change: the harness now compiles and captures the `game` scenario and its four dashboard substates;
  `ApplyGameTiles` was the only missing piece (the scenario, substate catalog, and switch `case`s were already
  correct from the interrupted prior pass).
- Design adjustment: none to `Stats.Core`/`Stats.App` from Tasks 1–3 — this task only touched the harness
  (`tools/Stats.UiPreview`), its tests, `README.md`, and this evidence doc, per the plan's disjoint file
  ownership.
- Two `baseline.json` entries (cases 7 and 8 above) use a taller `Height` than the spec's literal table cell so
  the tile each capture exists to demonstrate is actually inside the frame — see "Deviation" above.
- Simulation-only evidence: all 15 distinct PNGs above (9 single captures + the 6-frame theme-cycle fan-out),
  fake sensors/fan backend, no hardware.
- Real Windows/hardware checks performed: none — build/tests/captures ran directly in this environment; no
  `dotnet run --project src/Stats.App` smoke test (elevation prompt can't be satisfied from this shell, CLAUDE.md
  rule 8).
- Remaining checks: the spec's 8-item owner checklist (submenu gating/behaviour, live stutter/marker movement,
  theme switch on the real app, effects-off/idle-CPU, Free/Snap drag feel, PresentMon-not-running state via the
  real app, and the 1.9.x downgrade reset) all need the real app or a real older build — none can be
  discharged from this preview harness.

## Fix wave (after `docs/game-tiles/REVIEW.md`)

- Candidate is now the branch tip (the fix-wave commit after `d98e820`).
- S1: `HistogramBars` keys its geometry cache on the bins' sum and max as well as the array reference, so the
  alternating buffers can no longer replay a stale distribution after skipped renders.
- S2: the histogram's rule lookup only falls back to the settings scan when there is no threshold index.
- S3: README no longer claims frame time is a histogram "by default" (Auto is unchanged; the kind is offered).
- S4: capture 3 (`game-tiles-large`) re-taken at 1180×1000 so the L histogram tile is fully inside the frame.
- S5: capture 1's description corrected (tall bar at the far left; the thin element on the right is the p99 marker).
- S6: new capture `v14-dashboard-game-histogram-fps-dark-amber.png` (substate `game-histogram-fps`, Histogram on
  `fps.avg`): the marker reads "p1 57 fps" at the left edge with the marker line at the low end — the p1 branch.
- N2: the marker line is clamped 0.5 px inside the control at fraction 0 and 1.
- Gates after the fix wave: build 0 warnings; 836 + 219 tests green; both re-taken captures `Warnings: []`.
