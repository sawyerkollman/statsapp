# `feature/graph-effects` — whole-branch review

**Verdict: needs a fix wave.** Two blockers (both are the fixed axis not being propagated to the
*other* consumers of X), plus one rendering-cost item and one harness-fixture item. Core math,
settings compatibility and the harness's two-phase `GraphStyle.Apply` are sound. Gates re-run in this
worktree: `dotnet build --nologo` → **0 warnings, 0 errors**; `dotnet test --nologo` → **714 + 185
passed, 0 failed, 0 warnings**. Matches `docs/graph-effects/EVIDENCE.md`.

## Blockers

**B1 — Hover index still uses the old stretched mapping, so the crosshair detaches from the pointer
whenever the buffer isn't full.** `src/Stats.App/Controls/Sparkline.cs:147` and
`src/Stats.App/Controls/HistoryChart.cs:175` still compute `idx = round(px / width * (count - 1))`,
the inverse of master's `X(i) = w*i/(count-1)`. `OnRender` now places that sample at
`SampleAxis.X(idx, count, Capacity, …)`. Failure scenario: fresh app start, 60 samples in a
120-capacity buffer; the user points at the middle of a sparkline → `idx ≈ 30` → the crosshair and
tooltip are drawn at ~76 % of the width, a quarter-width away from the cursor, and report the wrong
sample. Fix: invert `SampleAxis` instead — add
`SampleAxis.IndexAt(double x, int count, int capacity, double left, double width)` returning
`count - 1 - (int)Math.Round((left + width - x) * Math.Max(1, Math.Max(capacity, count) - 1) / width)`,
clamp to `[0, count-1]`, call it from both `OnMouseMove`, and test it round-trips against `X` for
full, half-full and `capacity < count`.

**B2 — The detail chart's time-axis labels span the full plot width while the data is right-anchored,
so every label is wrong during warm-up.** `src/Stats.Core/ViewModels/MetricDetailViewModel.cs:100`
builds 5 labels from `Values.Length` (`BuildTimeAxisLabels`, line 122: `totalSeconds =
(sampleCount - 1) * secondsPerSample`), and `src/Stats.App/Controls/HistoryChart.cs:255-258` spreads
them evenly across `plotW` — neither knows about `Capacity`. Failure scenario: visible in this
branch's own capture `artifacts/graph-effects/v13-details-detail-smooth-dark-amber.png` — the labels
read `-59s … -30s … now` across the whole plot, but the line only starts at the `-30s` tick, so the
oldest sample (actually 59 s old) sits under the label that says 30 s. Every reading off the time
axis is off by up to 2× until the ring buffer fills (which, at a 1 h history window, is an hour).
Fix: base the labels on the axis window, not the sample count — pass capacity through
(`totalSeconds = (max(capacity, sampleCount) - 1) * secondsPerSample`) so label *i* genuinely lands at
`plotLeft + plotW * i / (n-1)`; add a `MetricDetailViewModel` test that a 30-of-120 buffer at
1 s/sample labels the left edge `-119s`, not `-29s`.

## Should-fix

- **S1 — The pulse turns one poll tick into ~30 full chart re-renders.** `PulseProgressProperty` is
  registered `AffectsRender` (`Sparkline.cs:49-51`, `HistoryChart.cs:67-69`) and animated 0→1 over
  500 ms (`Sparkline.cs:132`), so every animation frame re-runs the whole `OnRender`: min/max scan,
  `FiniteRuns().ToList()`, a `List<Point>` per run, three `double[n]` per run inside `EmitCurve`, two
  `StreamGeometry` builds, plus a fresh `LinearGradientBrush` and 2-3 `Pen`s. At a 1 s poll that is a
  ~50 % duty cycle of full geometry rebuild on *every* visible sparkline. Idle (no new sample) really
  is zero, so the spec's non-goal holds, but `docs/ui-polish/VALIDATION.md` ("no unrelated animation
  … that materially increase continuous rendering cost") is the standard this should be measured
  against and it is currently unmeasured. Fix (cheapest): keep the line/fill `StreamGeometry` +
  brushes in instance fields, invalidated only when `Values`/`ActualWidth`/`ActualHeight`/`Stroke`/
  `GraphStyle` change, so pulse frames only re-emit the two `DrawEllipse` calls. Better: draw the
  pulse ring into its own child `DrawingVisual` so `PulseProgress` need not be `AffectsRender` at all.
- **S2 — The default fixture now renders every capture at half width.**
  `tools/Stats.UiPreview/PreviewComposition.cs:89` builds a `capacity: 120` store and
  `tools/Stats.UiPreview/Fixtures/TimeSeries.cs:12` supplies `Ticks = 60`, so with the fixed axis
  every existing `artifacts/ui-polish/**` dashboard/details capture silently changes meaning — see
  `artifacts/graph-effects/v13-dashboard-graphs-effects-dark-amber.png`, where each tile's sparkline
  occupies the right half of a track tagged "2m". That is neither the steady state the real app
  spends ~all its time in nor a deliberate warm-up case. Fix: make the default full (either
  `capacity: fixture.Ticks.Count` or raise `TimeSeries.Ticks` to 120) and keep `graphs-warmup` as the
  one substate that deliberately under-fills; re-take the affected ui-polish baselines.
- **S3 — Nothing in the tests would catch an overshoot regression.**
  `tests/Stats.Core.Tests/CurveSmoothingTests.cs` asserts tangent *properties* (extremum → 0, flat →
  0, α²+β² ≤ 9) but never evaluates the resulting cubic. Failure scenario: someone drops the clamp
  loop at `src/Stats.Core/Metrics/CurveSmoothing.cs:49-63` for a plain Catmull-Rom and only
  `MonotoneTangents_AlphaBetaClamp_…` fails, with no signal about what it means visually. Fix: add a
  test that walks a spiky series (e.g. `ys = {0, 10, 0.2, 9, 1}`), evaluates the Bézier from
  `BezierControlPoints` at t = 0…1 in 0.02 steps, and asserts every point stays within
  `[min(y_k, y_{k+1}), max(y_k, y_{k+1})]` — the property the feature actually promises.
- **S4 — A one-sample finite run draws nothing.** `Sparkline.cs:282` / `HistoryChart.cs:317`
  `BeginFigure(pts[0], …)` then `EmitCurve` returns immediately for `n <= 1`, and the fill figure is
  zero-width. Failure scenario: history `[NaN, 42, NaN, NaN, …]` — the single real reading is
  invisible (the last-value dot only covers the *newest* finite sample). The spec says "runs of length
  1 draw a dot only". Fix: in the line pass, `dc.DrawEllipse(Stroke, null, pts[0], 1.25, 1.25)` for
  single-point runs. (Runs of length 2 are correct — `EmitCurve` short-circuits to `LineTo`.)
- **S5 — Per-render unfrozen pens.** `Sparkline.cs:291`, `HistoryChart.cs:326`, `ArcGauge.cs:101` and
  `ArcGauge.cs:119` allocate a `Pen` on every render and never `Freeze()` it (the glow/pulse pens do).
  Not a correctness bug, but it compounds S1 and the eased-gauge frames. Fix: cache one frozen pen per
  (brush, thickness) in an instance field rebuilt on `Stroke`/`Thickness` change.
- **S6 — `HistoryCapacity` shadowing.** `MetricTileViewModel.cs:47` and `MetricDetailViewModel.cs:43`
  introduce an `int HistoryCapacity` property that shadows the static class
  `Stats.Core.Metrics.HistoryCapacity`, forcing `global::`-qualified call sites at
  `MetricTileViewModel.cs:98` and `MetricDetailViewModel.cs:120`. `global::` *is* robust (it cannot be
  re-bound), but the hazard is the next edit: anyone adding a second `HistoryCapacity.FormatWindow`
  call in these two files gets a confusing "cannot be used like a type" error. Fix: rename the
  property to `HistorySampleCapacity` (update the two XAML bindings and the two new tests) or the
  static class to `HistoryWindow`.

## Nits

- **N1** — `src/Stats.Core/Metrics/CurveSmoothing.cs:26-27`: a zero-`dx` segment yields `d[k] = 0`,
  which then forces *both* neighbouring tangents flat (line 40) rather than just skipping the
  degenerate segment. Unreachable today (`SampleAxis` spacing is always `stride·width/denom > 0`) but
  untested; add a `MonotoneTangents_ZeroDx_DoesNotThrowOrNaN` case documenting the chosen behaviour.
- **N2** — `CurveSmoothing.cs:23`: the `n - 1 <= 128` stackalloc guard is right but undocumented;
  say why 128 (1 KiB) in the comment.
- **N3** — `FiniteRuns`, `RunPoints`, `EmitCurve`, `FillBrushFor`, `GlowPenFor` and `PulsePenFor` are
  now duplicated verbatim between `Sparkline.cs:167-214,320-348` and `HistoryChart.cs:190-237,402-430`
  (~90 lines, differing only in two alpha constants). A `Controls/CurveRenderer.cs` internal static
  helper would keep the two in step; whoever fixes B1/S1/S4 otherwise has to fix each twice.
- **N4** — `tools/Stats.UiPreview/PreviewComposition.cs:211`: the `graphs-effects` substate is a no-op
  (both settings already default true); the comment says so, which is fine, but the paired
  `detail-smooth` at line 215 is likewise a no-op and isn't labelled.
- **N5** — `docs/graph-effects/EVIDENCE.md` records the candidate as `2e5d5fb` with T3 uncommitted;
  the captures therefore predate `7913bc7`. Content is accurate, but the final PR should restate the
  candidate as the branch tip.
- **N6** — `CaptureHost.cs:53` vs `:132`: batch captures force `GraphEffects = false` for the first
  bind while interactive mode applies the real settings immediately. That divergence is deliberate and
  documented, and it cannot change a *captured* pixel (phase 2 runs before any capture), so it is
  sound — noting it only so the next reader doesn't "fix" it.

## What checked out clean

- **Core math** — Fritsch–Carlson is textbook-correct: flat-secant zeroing, interior sign-change
  zeroing, `α<0`/`β<0` clamping and the `τ = 3/√(α²+β²)` rescale are all in the right order
  (`CurveSmoothing.cs:38-63`); the `n == 0/1/2` fast paths match the spec.
- **Rendering structure** — the fill figure closes correctly per run (`BeginFigure(…, true, true)` +
  the trailing baseline `LineTo`, `Sparkline.cs:258-261`), the stride decimation's final-sample fix-up
  is right (`(last-start) % stride != 0`), NaN runs of length 2 fall back to `LineTo`, the glow is
  drawn *before* the line, all glow/pulse/guide brushes and pens are frozen, `Capacity` default 0
  degrades to master's behaviour via `SampleAxis`'s `Math.Max(capacity, count)`, and the Y-inverted
  pixel space is handled correctly (tangents are computed in pixel space, so the monotone property is
  orientation-independent).
- **Animation lifecycle** — the classic WPF "animated value wins" trap is avoided: `LevelBar.cs:73` /
  `ArcGauge.cs:81` call `BeginAnimation(prop, null)` *before* the direct set, and `PulseProgress` is
  only ever written by an animation. The `!IsLoaded && AnimatedFraction == 0.0` first-assignment guard
  is sound for the current hosts (no `VirtualizingStackPanel`/container recycling anywhere in
  `src/Stats.App/Views`); it would need revisiting if tile hosting is ever virtualized.
- **`GraphStyle.Motion`** — `SystemParameters.ClientAreaAnimation` is a cached WPF static invalidated
  on `WM_SETTINGCHANGE`, and it is read only from the two `On*Changed` callbacks (per poll tick), never
  from `OnRender`. Cheap and live.
- **Settings compatibility (rule 3)** — both `AppSettings` bools default `true`, need no sanitation,
  and `SettingsServiceTests` covers a pre-1.10 file with neither field. `SettingsChange.Graphs` is
  appended to the enum, raised from both `partial void On*Changed` guards behind `_loaded`, and handled
  in `App.xaml.cs:794` next to the other live-apply cases; startup apply sits next to `ThemeManager.Apply`
  before any window is created. Matches the existing pattern exactly.
- **Acceptance coverage** — every acceptance bullet has a test or a capture except the overshoot
  property (S3); README's Graphs paragraph and EVIDENCE's gate table are accurate.

## Owner checklist additions

Everything below needs the real app on real hardware; none of it is decidable from build, tests or
the harness.

1. **Idle/updating CPU cost with effects on vs off** (Task Manager or ETW, identical tile set, ≥2 min
   each) — this is the acceptance evidence for S1. Compare: effects off, effects on, and effects on
   with the detail window open. A material delta means S1's geometry caching is required, not optional.
2. **Pulse timing and reach** — does one ring per poll read as a heartbeat or as flicker at a 1 s
   interval, and at a 0.5 s interval? Is 2.5 → 9 px too large on an S tile?
3. **Bar/gauge easing feel** — 250 ms quadratic ease-out against a metric that changes fast (CPU load,
   FPS): does the bar lag visibly behind the number next to it?
4. **Hover correctness after B1 is fixed** — on a genuinely half-full buffer (restart the app, wait
   ~30 s), confirm the sparkline tooltip and the detail crosshair both land on the sample under the
   cursor.
5. **Warm-up read-out after B2 is fixed** — confirm the detail window's time labels match the data
   from the first sample onward, and that `HistoryWindowTag` ("2m") still tells the truth while the
   buffer is filling.
6. **OS "animation effects" off** (Settings → Accessibility → Visual effects → Animation effects) —
   confirm glow/gradients stay and pulse/easing stop, live, without a restart.
7. **"Glow and motion effects" off restores the v1.9.2 look** — A/B against a v1.9.2 build, not just
   against the `graphs-plain` capture.
8. **Curve on real data** — a spiky FPS series and a flat temp series; confirm no visible overshoot
   below 0 % on a percentage metric and that a single-tick spike still reads as a spike.

## Fix wave

- **N3 — Fixed.** Extracted `FiniteRuns`, `RunPoints`, `EmitCurve`, `FillBrushFor`, `GlowPenFor`, `PulsePenFor`
  into `src/Stats.App/Controls/CurveRenderer.cs` (internal static, alpha constants parameterised); `Sparkline`
  and `HistoryChart` both call it, no behaviour change.
- **B1 — Fixed.** Added `SampleAxis.IndexAt(x, count, capacity, left, width)` (inverse of `X`, clamped to
  `[0, count-1]`) with round-trip tests against `X` for full/half-full/`capacity < count`; both controls'
  `OnMouseMove` now call it instead of the old stretched `round(px/width*(count-1))` mapping.
- **B2 — Fixed.** `MetricDetailViewModel.BuildTimeAxisLabels` now takes `capacity` and bases the axis window on
  `max(capacity, sampleCount)`; new test confirms a 30-of-120 buffer at 1 s/sample labels the left edge with the
  full 119-second window (`HistoryCapacity.FormatWindow(119)` = `-1m59s`), not the old 29-second one.
- **S1 — Fixed.** Both controls cache the line/fill `StreamGeometry` and the fill brush/glow pen/line pen in
  instance fields, rebuilt only when `Values`/`ActualWidth`/`ActualHeight`/`Stroke`/`Capacity`/`GraphStyle`
  change; a pulse frame now redraws the cached geometry plus the two small ellipses only.
- **S2 — Fixed.** `PreviewComposition.Build` sizes the preview `MetricStore` to `fixture.Ticks.Count` instead of
  a hardcoded 120 (fewest test changes — only `GraphEffectsSubstateTests`'s capacity assertions moved from 120
  to 60), so every non-warmup capture is a full buffer; `graphs-warmup`/new `detail-warmup` still trim to 25% of
  that capacity. Whole `baseline.json` manifest (58 entries) re-run — see EVIDENCE.md's "Fix wave re-captures".
- **S3 — Fixed.** Added `Curve_SpikySeries_NeverOvershootsItsSegmentRange`: walks the Bézier from
  `BezierControlPoints` at t = 0…1 in 0.02 steps over `{0, 10, 0.2, 9, 1}` and asserts every point stays within
  its segment's `[min, max]`.
- **S4 — Fixed.** Single-point finite runs now draw a `radius 1.25` dot in the line pass (both controls), tracked
  via a `_singlePointDots` list populated during geometry rebuild.
- **S5 — Fixed.** Folded into S1's cache for Sparkline/HistoryChart's line pen; `ArcGauge`'s track/value pens are
  now cached frozen fields too, rebuilt only on `Stroke`/`Track`/`Thickness` change (the fraction-dependent glow
  cap pen still builds fresh per render, since its sweep angle changes every eased frame).
- **S6 — Fixed.** Renamed `HistoryCapacity` → `HistorySampleCapacity` on both `MetricTileViewModel` and
  `MetricDetailViewModel` (XAML bindings in `TileTemplates.xaml`/`MetricDetailWindow.xaml` and tests updated to
  match), dropping the `global::`-qualified call sites now that the property no longer shadows the
  `Metrics.HistoryCapacity` type.
- **N1 — Fixed.** Documented the zero-`dx` behaviour in `CurveSmoothing.cs` and added
  `MonotoneTangents_ZeroDx_DoesNotThrowOrNaN`.
- **N2 — Fixed.** Commented why 128 (1 KiB stackalloc, safe under nested calls) is the stackalloc/heap cutover.
- **N4 — Fixed.** `detail-smooth`'s no-op-against-defaults status is now commented, matching `graphs-effects`.
- **N5 — Fixed.** EVIDENCE.md's candidate now reads "branch tip (`7913bc7`) + this fix wave (uncommitted)".
- **N6 — Skipped (as intended).** Noted as sound/no-op by the review itself; nothing to fix.
- Also fixed in passing (not a lettered finding, but blocked getting real re-captures): `baseline.json` had no
  `"Method"` field on any entry, so every capture silently used the default `"screen"` method, which in this
  worktree's shell captures the real desktop (not the app) — see EVIDENCE.md. Added `"Method": "rtb"` to every
  entry.
