# Overlay sparklines and status line — implementation evidence

Task 3 (harness + docs) of `docs/superpowers/plans/2026-09-11-overlay-sparklines.md`, per
`docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md`'s "Preview harness" and "Acceptance → Captures /
Harness tests" sections. Adapted from `docs/graph-effects/EVIDENCE.md`'s format — this feature has no separate
routing/task ledger, so only the sections that apply are kept.

## Candidate

- Date/time: 2026-09-12 (Windows 11 Pro, this session's worktree).
- Repo/branch: `feature/overlay-sparklines` (worktree `C:\claude-projects\Stats-wt\overlay-sparklines`).
- Candidate commit: branch tip `e740b1b` ("feat(app): overlay sparklines and status strip", Task 2)
  **+ this task's uncommitted files** (Task 3, harness + docs): `tools/Stats.UiPreview/SubstateCatalog.cs`,
  `tools/Stats.UiPreview/PreviewComposition.cs`, `tools/Stats.UiPreview/captures/baseline.json`,
  `tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs` (new), `README.md`, this document (new). Tasks 1
  (`f2bbb5f`) and 2 (`e740b1b`) are committed; nothing else in the worktree is dirty.
- .NET SDK: targeting `net8.0-windows`; Windows 11 Pro 10.0.26220.
- Overall status: implemented, captured, visually inspected.

## What changed in the harness (Task 3 scope)

- `tools/Stats.UiPreview/SubstateCatalog.cs`: `ByView["overlay"]` gains five new substates — `sparklines`,
  `sparklines-off`, `sparklines-warmup`, `status-line`, `status-line-warn` — appended after the existing overlay
  substates (`move-mode`, `vertical`, `light-parent`, `opacity-min`, `opacity-max`, `long-value`); combinable with
  those via `+` exactly like every other substate.
- `tools/Stats.UiPreview/PreviewComposition.cs`:
  - `Build()`'s warm-up trim condition (previously `graphs-warmup || detail-warmup`) now also checks
    `sparklines-warmup`, so the store applies only the most recent 25% of the fixture's 60 ticks before any view
    model is constructed — the overlay's `Sparkline` (fed from the same `MetricStore` the dashboard uses) then
    shows the same right-anchored, 25%-full fixed `SampleAxis` as `graphs-warmup`/`detail-warmup`.
  - New `// ---- overlay sparklines ----` block in `ApplySubstate`: `sparklines`/`sparklines-off` set
    `AppSettings.OverlayGraphs` and call `OverlayViewModel.ApplyLayout()` (`sparklines` is documentary — the
    default is already `Sparkline` — matching how `graphs-effects` is documentary against the dashboard's
    already-true defaults); `sparklines-warmup` does no `ApplySubstate` work (the trim already happened in
    `Build()`); `status-line`/`status-line-warn` set `AppSettings.OverlayStatusLine = true`, call `ApplyLayout()`,
    then push a status through the real `OverlayStatusComposer.Compose(...)` with the spec's fixture inputs (no
    fake composer, no `GameModeSwitcher` exposure) — `status-line` composes
    `Compose(true, "Balanced", [Active, Idle], "Game mode: gaming (Balanced since 14:30)", null)`,
    `status-line-warn` composes
    `Compose(true, null, [WriteFailed, Active], "Game mode: desktop", "PresentMon: access denied (simulated).")`.
- `tools/Stats.UiPreview/captures/baseline.json`: 11 new entries under `artifacts/overlay-sparklines/` (below),
  all `"Method": "rtb"`.
- `tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs` (new, 14 test cases): asserts each new
  substate's effect on `PreviewComposition.Settings`/`Overlay`/the store it produces, and that
  `SubstateCatalog.Validate` accepts the five new names for `overlay` and rejects them for other views — the same
  composition-only, no-WPF-window style `GraphEffectsSubstateTests`/`DashboardLayoutSubstateTests` use.
- `README.md`: the "▣ Overlay" bullet (~line 183) gains one sentence each for the sparkline (Settings → Overlay →
  "Sparklines beside each value", on by default; beside the value horizontally, below it vertically; same
  history window and Smooth lines / Glow and motion effects as the dashboard) and the status line ("Status line",
  off by default; active fan profile with write-failed / source-unavailable faults, game mode, and why FPS is
  unavailable; only appears when one of those has something to say).

## Captures

Command: `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`.
Batch result: 75 manifest entries → 80 capture(s) written (one entry, `v6-theme-cycle`, fans out into 6 themed
captures), **0 entries failed**. DPI 96×96 (logical == physical at `UiScale 1.0` for every view except `overlay`,
which is `SizeToContent` and so reports its measured physical size), Invariant culture, fixed fixture time
`2026-09-06T14:30:00Z`, seed `1234`. Every one of the 11 new sidecars' `Warnings` array is `[]`; `SourceCommit` in
each sidecar reads `e740b1bac0fa52828e018dca4027b259abd881c7` (the Task 2 tip, confirming these were captured
against the candidate described above). All 11 PNGs + sidecars are under `artifacts/overlay-sparklines/`
(git-ignored, matching the existing `artifacts/graph-effects/`/`artifacts/ui-polish/` convention) — this document
is the committed evidence.

| Scenario/substate | Theme | Size | File | Launch command | Observation |
| --- | --- | --- | --- | --- | --- |
| `normal` / `overlay` / `sparklines` | Dark Amber | 400×200 | `v13-overlay-sparklines-dark-amber.png` | `dotnet run --project tools/Stats.UiPreview -- --scenario normal --view overlay --substate sparklines --theme "Dark Amber" --width 400 --height 200 --ui-scale 1 --method rtb --output artifacts/overlay-sparklines/v13-overlay-sparklines-dark-amber.png` | Each of the three tiles (Tctl/Tdie, GPU Core, FPS) shows its sparkline immediately to the right of the value on the same line, stroked in the value's own (Normal/white) colour, sized to the value text's line height. |
| `normal` / `overlay` / `sparklines` | Light | 400×200 | `v13-overlay-sparklines-light.png` | `... --theme "Light" ...` | Pixel-for-pixel the same dark overlay panel and white sparkline strokes as the Dark Amber capture — the overlay's pinned colours are untouched by the app-wide Light preset, confirming owner decision 1 (`Overlay*` brushes, never theme-tinted). |
| `normal` / `overlay` / `sparklines-off` | Dark Amber | 400×200 | `v13-overlay-sparklines-off-dark-amber.png` | `... --substate sparklines-off ...` | Plain text only — no sparkline column, tiles measure to just the value text's width (239 physical px vs. 425 for the `sparklines` capture at the same logical size), confirming the off path collapses the `Sparkline` rather than reserving its space. |
| `normal` / `overlay` / `vertical+sparklines` | Dark Amber | 200×400 | `v13-overlay-vertical-sparklines-dark-amber.png` | `... --substate vertical+sparklines ... --width 200 --height 400 ...` | Each sparkline sits directly under its value (left-aligned) instead of beside it, confirming the value row's `OverlayOrientation` `DataTrigger` swaps margin/alignment correctly in vertical mode. |
| `thresholds` / `overlay` / `sparklines` | Dark Amber | 400×200 | `v13-overlay-sparklines-severity-dark-amber.png` | `--scenario thresholds --view overlay --substate sparklines ...` | Tctl/Tdie (88.0 °C, Warn) renders in amber/orange, GPU Core (90.0 °C, Crit) in red, FPS (45 fps, Warn) in amber — each sparkline's stroke matches its own tile's severity tint, confirming the `Severity`→`SeverityToBrush` binding drives both the value text and the sparkline from the same brush. |
| `normal` / `overlay` / `sparklines-warmup` | Dark Amber | 400×200 | `v13-overlay-sparklines-warmup-dark-amber.png` | `... --substate sparklines-warmup ...` | Every sparkline occupies only its track's right ~25% with empty space to the left and its right end flush with the track's right edge — confirms the fixed, right-anchored `SampleAxis` on a 15-of-60-sample buffer, the overlay counterpart of `graphs-warmup`. |
| `normal` / `overlay` / `status-line` | Dark Amber | 400×200 | `v13-overlay-status-line-dark-amber.png` | `... --substate status-line ...` | A new line appears below the tiles reading `Fans: Balanced · Game mode: gaming (Balanced since 14:30)` in the secondary (gray) text colour, no glyph — the informational style. |
| `normal` / `overlay` / `status-line-warn` | Dark Amber | 400×200 | `v13-overlay-status-line-warn-dark-amber.png` | `... --substate status-line-warn ...` | The strip reads `Fans: Custom — write failed · Game mode: desktop · PresentMon: access denied (simulated)` in amber/warn text with the triangular `Icon.Warn` glyph to its left — the warning style, severity not conveyed by colour alone. |
| `normal` / `overlay` / `status-line-warn` | Light | 400×200 | `v13-overlay-status-line-warn-light.png` | `... --substate status-line-warn --theme "Light" ...` | Same warning strip/glyph/text as the Dark Amber capture — pinned `OverlayWarn` survives the Light preset the same way the sparkline strokes do. |
| `normal` / `overlay` / `vertical+status-line-warn` | Dark Amber | 200×400 | `v13-overlay-vertical-status-line-warn-dark-amber.png` | `... --substate vertical+status-line-warn --width 200 --height 400 ...` | (Re-taken after the fix wave.) The warning text wraps inside the tiles' width — the panel keeps the same width as the `vertical+sparklines` capture and only grows in height; before the fix the strip's outer margin sat outside its MaxWidth and the panel widened by ~87 px (review B1). |
| `settings` / `settings` / `category-overlay` | Dark Amber | 1180×900 | `v13-settings-overlay-dark-amber.png` | `... --scenario settings --view settings --substate category-overlay ... --width 1180 --height 900 ...` | The Overlay settings tab shows both new checkboxes directly after Click-through: "Sparklines beside each value" (checked, matching the `Sparkline` default) and "Status line (fan control, game mode, FPS source)" (unchecked, matching the `false` default). |

## What the harness can show

Sparkline presence/size/placement per orientation, stroke = value colour at Normal/Warn/Crit, the fixed axis
during warm-up, the off path's narrower measured width, both themes, the strip's text/glyph/colour/wrapping and
that it does not widen the panel (true only after the review fix wave, see below), the Settings checkboxes.

## What the harness cannot show

Verbatim from the spec's "Acceptance → Owner checklist" preamble (`docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md`, "Preview harness" section):

> **What it cannot show** (owner checklist): the layered window on screen (`Window.Opacity` 0.3/1.0 legibility),
> click-through, `DragMove`, the hotkey, move mode over a real desktop, the pulse/motion (captured at rest by
> design; `--interactive` shows live pulses but pushes no status and is not evidence), `SizeToContent` behaviour
> over time (jitter), real PresentMon/fan hardware strings, tray interaction, CPU cost.

These remain the owner's checklist items in the spec (Task 4/the owner, not this task) — they need a live,
running Stats instance, not this fixture-driven preview host (CLAUDE.md rules 7/8: PresentMon/ETW and the app's
elevation prompt cannot be exercised from this shell).

## Gate outcomes

| Gate | Result | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors. |
| Tests | Pass | `dotnet test --nologo` — Stats.Core.Tests 822/822, Stats.UiPreview.Tests 211/211 (197 pre-existing + 14 new in `OverlaySparklinesSubstateTests.cs`), 0 failures, 0 warnings. |
| Captures | Pass | Full `baseline.json` batch: 75 entries → 80 capture(s) written, 0 failed; all 11 new `artifacts/overlay-sparklines/` sidecars have `Warnings: []`. |

## Final handoff

- Concrete change: five new overlay preview substates (`sparklines`, `sparklines-off`, `sparklines-warmup`,
  `status-line`, `status-line-warn`), the store-level warm-up trim extended to cover the overlay, 11 new
  `baseline.json` entries, 14 new harness tests, and this evidence document.
- Design adjustment: none to the App/Core code from Tasks 1–2 — this task only touched the harness
  (`tools/Stats.UiPreview`), its tests, `README.md`, and this document.
- Simulation-only evidence: all 11 captures above (fake sensors/fan backend, no hardware; status text composed by
  the real `OverlayStatusComposer` with fixture inputs, not a real `FanController`/`GameModeSwitcher`/PresentMon).
- Real Windows/hardware checks performed: none in this task — build/tests/captures ran directly in this
  environment; no `dotnet run --project src/Stats.App` smoke test (elevation prompt can't be satisfied from this
  shell, per CLAUDE.md rule 8).
- Remaining checks: Task 4 (whole-branch review) and the spec's owner checklist — both need the real, running
  Stats app and are out of this task's scope.

## Fix wave (after `docs/overlay-sparklines/REVIEW.md`)

- Candidate is now the branch tip (the fix-wave commit after `81f303c`).
- B1: `vertical+status-line-warn` re-taken — 100×392, i.e. the tiles' own width (`vertical+sparklines` is 100×202)
  plus height; the strip wraps inside the tiles' width and breaks the two over-wide tokens. Before the fix the
  panel widened to 187 px. `status-line-warn` (horizontal) re-taken: 425×85, unchanged width vs `sparklines`.
- S1: new capture `v13-overlay-vertical-sparklines-off-dark-amber.png` (`vertical+sparklines-off`, 97×132) —
  the vertical off path: plain stacked values, no sparkline column.
- Off path re-checked after the fix: `v13-overlay-sparklines-off-dark-amber.png` is byte-identical to the base's
  `artifacts/ui-polish/before/v12-overlay-light-parent.png` (the base's plain overlay).
- S2/S3/S4: status-only fan accessor, glyph alignment, single-period trim — not capturable beyond the strip
  captures above (owner checklist).
- Observation for the owner: at the vertical overlay's natural width (~100 px with sparklines) the strip is very
  narrow and long tokens get emergency-broken; if that reads badly on a real screen, the alternative is a wider
  minimum for the strip in vertical mode (a spec change, not a bug).
