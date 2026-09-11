# Graph effects — implementation evidence

T3 (harness + docs) of `docs/superpowers/plans/2026-09-11-graph-effects.md`, per
`docs/superpowers/specs/2026-09-11-graph-effects-design.md`'s "Preview harness" and "Acceptance → Captures"
sections. Adapted from `docs/ui-polish/EVIDENCE_TEMPLATE.md` — this feature has no separate routing/task ledger,
so only the sections that apply are kept.

## Candidate

- Date/time: 2026-09-11 (Windows 11 Pro, this session's worktree)
- Repo/branch: `feature/graph-effects` (worktree `C:\claude-projects\Stats-timescale`)
- Candidate commit: `2e5d5fb` ("feat(app): smooth curves, fixed time axis, glow, pulse, and eased bars/gauges" —
  Tasks 1–2). T3 (this task) adds harness/test/doc changes on top, uncommitted at capture time.
- Dirty files at capture time: `tools/Stats.UiPreview/PreviewComposition.cs`,
  `tools/Stats.UiPreview/SubstateCatalog.cs`, `tools/Stats.UiPreview/Views/CaptureHost.cs`,
  `tools/Stats.UiPreview/captures/baseline.json`, `tests/Stats.UiPreview.Tests/GraphEffectsSubstateTests.cs` (new).
- .NET SDK: 9.0.316 (targeting `net8.0-windows`); Windows 11 Pro 10.0.26220.
- Overall status: implemented, captured, visually inspected.

## What changed in the harness (Task 3 scope)

- `tools/Stats.UiPreview/Views/CaptureHost.cs`: `GraphStyle.Apply(...)` is now called next to
  `ThemeManager.Apply(...)`, in two phases, so captures honour the fixture's `SmoothLines`/`GraphEffects`
  settings. Phase 1 (before `window.Show()`) applies the fixture's real `SmoothLines` but forces
  `GraphEffects = false`; phase 2 (after `WaitForSettled` + a frame pump) applies the real settings. This avoids a
  genuine timing hazard: `Sparkline`'s last-value pulse starts a real-time 500 ms `DoubleAnimation` the first time
  its `Values` binding changes from `null` to the fixture's initial history, if `GraphStyle.Motion` is already
  `true` at that moment — and this harness has no way to fast-forward WPF's animation clock, so a capture taken
  shortly after would show a mid-flight pulse ring. Deferring the real `GraphEffects` value to after the first
  bind means that transition never sees `Motion = true`, so no pulse starts; the deferred apply still fires
  `GraphStyle.Changed` (`InvalidateVisual`, no animation) so glow/gradients still render correctly.
  `LevelBar`/`ArcGauge` are unaffected either way — their own `firstAssignment` guard already skips easing on the
  first bind, regardless of `GraphStyle.Motion`.
- `tools/Stats.UiPreview/PreviewComposition.cs`: new dashboard substates `graphs-plain` (both settings off),
  `graphs-effects` (both on — documentary, since `AppSettings.SmoothLines`/`GraphEffects` already default `true`),
  and `graphs-warmup`; new details substates `detail-plain`/`detail-smooth` (same toggle, different name for the
  detail window per the spec). `graphs-warmup` is handled specially in `Build()`, *before* the `DashboardViewModel`
  is constructed from the `MetricStore` (substates normally run after that, which would be too late to change how
  many samples are in the ring buffer): only the most recent 30 of the fixture's 60 ticks are applied to the
  capacity-120 store, so the buffer sits at exactly 25%.
- `tools/Stats.UiPreview/SubstateCatalog.cs`: the five new substates added to the `dashboard`/`details` allow-lists.
- `tools/Stats.UiPreview/captures/baseline.json`: 7 new entries (below).
- `tests/Stats.UiPreview.Tests/GraphEffectsSubstateTests.cs` (new, 13 tests): asserts each new substate's effect on
  `PreviewComposition.Settings`/the store it produces, and that `SubstateCatalog.Validate` accepts them — the same
  composition-only, no-WPF-window style every other substate test in this project uses.

## Deviation from the spec's literal substate/entry count

The design's "Preview harness" section says "Three new `baseline.json` entries", but its own "Acceptance →
Captures" section asks for "`graphs-plain` vs `graphs-effects` at 1180×720 Dark Amber and Light" (4 entries) plus
a warm-up capture (1 more) — 5 for the dashboard alone, before the two `detail-plain`/`detail-smooth` entries. I
followed the more specific Acceptance section (7 entries total) rather than the earlier count, which reads like
it predates the theme-pair requirement being spelled out.

## Captures

Command: `dotnet run --project tools/Stats.UiPreview -- --batch <manifest> ` with `"Method": "rtb"` per entry
(`--method screen` returned blank frames in an earlier session per `docs/ui-polish/PREVIEW_HARNESS.md`'s note).
DPI 96×96 (logical == physical at `UiScale 1.0`), Invariant culture, fixed fixture time
`2026-09-06T14:30:00Z`, seed `1234`. Every sidecar's `Warnings` array is empty; `SimulatedServices` lists only the
fake reader/backend/marker/startup/update/settings entries (no production service resolved) — batch reported
`7 capture(s) written, 0 entries failed`. All under `artifacts/graph-effects/` (git-ignored, matching the existing
`artifacts/ui-polish/` convention), one PNG + one JSON sidecar per entry.

| Case | Scenario/substate | Theme | Size | File | Observation |
| --- | --- | --- | --- | --- | --- |
| Before (v1.9.2 look) | `normal` / `dashboard` / `graphs-plain` | Dark Amber | 1180×720 | `v13-dashboard-graphs-plain-dark-amber.png` | Jagged polyline sparklines/gauges, flat fill under the line, no arc glow cap — matches the pre-effects look. |
| After (default) | `normal` / `dashboard` / `graphs-effects` | Dark Amber | 1180×720 | `v13-dashboard-graphs-effects-dark-amber.png` | Same data; pixel diff against the plain capture confirms a real render difference (3,041 of ~212k sampled pixels differ, max channel delta 189) concentrated around the sparkline fills and gauge arc ends — the glow/gradient/soft-cap effects the plain capture lacks. Curve smoothing on this noisy CPU-temp series is visually subtle (monotone-cubic still touches every sample so a spike still reads as a spike), matching the design's own "still reads as a spike" intent rather than indicating a bug. |
| Before | `normal` / `dashboard` / `graphs-plain` | Light | 1180×720 | `v13-dashboard-graphs-plain-light.png` | Same plain look on the Light preset; bar/gauge fills flat orange-brown, no highlight. |
| After | `normal` / `dashboard` / `graphs-effects` | Light | 1180×720 | `v13-dashboard-graphs-effects-light.png` | Gauge arcs show a visibly brighter/softer end cap versus the plain Light capture (most visible on the GPU Load gauge); readable on both the dark accent-orange and the light background. |
| Warm-up | `normal` / `dashboard` / `graphs-warmup` | Dark Amber | 1180×720 | `v13-dashboard-graphs-warmup-dark-amber.png` | Every tile's sparkline occupies only the right ~25% of its track with empty space to the left, and the line's right end sits flush with the track's right edge — confirms `SampleAxis`'s fixed-scale, right-anchored layout with a 30/120-sample buffer (vs. the ~50%-full buffer every other scenario capture shows, since the fixture only ever applies 60 of the store's 120-capacity ticks). |
| Detail plain | `normal` / `details` / `detail-plain` | Dark Amber | 640×420 | `v13-details-detail-plain-dark-amber.png` | HistoryChart line has visible sharp kinks at each sample (straight-segment polyline), flat fill, plain right-edge dot — the detail window's own "before" look. |
| Detail smooth | `normal` / `details` / `detail-smooth` | Dark Amber | 640×420 | `v13-details-detail-smooth-dark-amber.png` | Same series, visibly rounded curve tops through each peak/valley instead of sharp kinks — the monotone-cubic smoothing is much more apparent at this chart's larger scale than on the small dashboard sparklines. |

## What the harness could not show

- **Last-value pulse** — a one-shot 500 ms ring animation on a new sample. Entirely time-based; a static PNG can't
  show it, and (per the "Deviation" note above and the CaptureHost comment) the harness deliberately avoids
  triggering it during the initial bind so captures are at rest, per the spec's requirement.
- **Bar/gauge easing feel** — `LevelBar`/`ArcGauge` ease their fraction toward a new value over 250 ms; a still
  capture only shows the resting end-state, not the motion.
- **CPU cost at idle** — the spec's owner checklist calls for a Task Manager check (~1% or less) that only makes
  sense against the running production app, not this fixture-driven preview host.

## Gate outcomes

| Gate | Result | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors. |
| Tests | Pass | `dotnet test --nologo` — Stats.Core.Tests 714/714, Stats.UiPreview.Tests 185/185 (172 pre-existing + 13 new in `GraphEffectsSubstateTests.cs`), 0 failures. |
| Captures | Pass | 7/7 written, 0 failed, every sidecar `Warnings: []`. |

## Final handoff

- Concrete change: the preview harness now applies `GraphStyle` from each capture's settings (it previously never
  did, so every capture silently used whatever `GraphStyle`'s static defaults happened to be), plus 5 new
  substates and 7 new `baseline.json` entries covering both graph-effects settings, both themes, a warm-up state,
  and the detail window.
- Design adjustment: none to the App/Core code from Tasks 1–2 — this task only touched the harness
  (`tools/Stats.UiPreview`), its tests, `README.md` verification (no change needed — the existing Task 2 bullet
  already covers Smooth lines/Glow and motion effects/fixed time axis), and this evidence doc.
- Simulation-only evidence: all 7 captures above (fake sensors/fan backend, no hardware).
- Real Windows/hardware checks performed: none in this task — build/tests ran directly in this environment;
  no `dotnet run --project src/Stats.App` smoke test (elevation prompt can't be satisfied from this shell, per
  `CLAUDE.md` rule 8).
- Remaining checks: the owner's checklist items in the spec that need a live, running Stats instance (pulse
  timing/feel, bar/gauge easing feel by eye, "Glow and motion effects" off restoring the v1.9.2 look end-to-end,
  idle CPU cost) are unverified here — they need the real app, not the preview harness.
