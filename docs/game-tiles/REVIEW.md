# Game tiles — whole-branch review (`feature/game-tiles`, 4 commits on `feature/v1.10`)

**Verdict: needs a small fix wave — no code blockers, but 2 render/cost fixes and 4 doc/capture corrections
should land before the PR.** `dotnet build --nologo` 0 warnings / 0 errors; `dotnet test --nologo` green
(Stats.Core.Tests 836, Stats.UiPreview.Tests 219). Spec contract (owner decisions 1–4, A–K) is implemented as
written; CLAUDE.md rule 2 (append-only `TileKind`, pinned by `TileKindTests`), rule 3 (defaulted field + lenient
converter + old files load, pinned by three `SettingsServiceTests`) and "no idle rendering cost" (no timer, no
animation, no `Effect`; other tile kinds' `Refresh` path is byte-identical — the only removals in the diff are
the ctor signature, the `AutomationLabel` assignment and `RaiseSeverityRefresh`) all hold.

Verified clean, no finding: sibling staleness is **not** a bug — `MetricStore.Definitions` is fixed at
construction and `FrameRateReader.Discover` (`src/Stats.Core/Frames/FrameRateReader.cs:67`) returns all three
Game definitions together whenever `PresentMon.exe` exists, independent of whether tracing ever starts, so
ctor-time resolution can never miss a later-arriving `fps.low1`. `ResolveKind`'s `FpsSummary`→`Sparkline`
fallback, `GameRole` menu gating, `TileKindConverter` vs. the global `JsonStringEnumConverter` (property
attribute wins; faithful copy of `DashboardLayoutModeConverter`), the templates' binding paths and resource keys
(`AutomationProperties.Name` arrives via the `TileBorder` style; `x:Name="TileMenuButton"` matches the container
`EventSetter` in `DashboardWindow.xaml:228/320`), overlay isolation (`OverlayViewModel.cs:44` passes no store;
`OverlayWindow.xaml` uses a fixed template, so no kind leakage), `ArcGauge`'s `new Pen(Track,…).Freeze()`
precedent, and the harness's `ApplyGameTiles` on a scenario lacking `fps.frametime` (filtered by
`RebuildSections`' `_store.TryGet` guard, no crash). The `theme-cycle` capture is real evidence: `CaptureHost`
applies each theme to an already-shown window, and frame 4 shows the control-drawn `p99` label re-coloured to
Light's `TextSecondary`, proving `OnThemeChanged`'s `_labelBrush` rebuild.

## Blockers

None.

## Should-fix

- **S1 — stale bars when a render is skipped.** `src/Stats.App/Controls/HistogramBars.cs:132`. The geometry cache
  key is `ReferenceEquals(_cacheBins, bins)` plus size, but the VM alternates exactly **two** `int[12]` buffers
  (`MetricTileViewModel.NextHistogramBinsBuffer`, `:240`), so any even number of missed renders — group collapsed
  then re-expanded, window hidden to tray or minimized while `RefreshAll` keeps ticking
  (`DashboardViewModel.cs:209`, unconditional), a layout-mode switch — lands on the *same* array with new
  contents and the control replays the previous tick's bars until the next tick flips the reference. Fix: add a
  cheap content guard next to the reference check — store `_cacheBinsSum`/`_cacheBinsMax` computed inside
  `RebuildBars` (a 12-element loop, no allocation) and rebuild when either differs.
- **S2 — per-tick threshold scan the index exists to avoid.** `src/Stats.Core/ViewModels/MetricTileViewModel.cs:283`:
  `thresholds?.RuleFor(Definition) ?? ThresholdEvaluator.RuleFor(Definition, _settings)` falls through to the
  settings scan whenever the index simply has *no* rule for the metric, not only when `thresholds` is null
  (`ThresholdIndex.RuleFor` returns null for "no rule" — `ThresholdIndex.cs:43`). Every Histogram tile on a
  rule-less metric (e.g. a clock or memory metric) pays a `List.FirstOrDefault` scan plus a boxed enumerator
  every poll, contradicting the v1.8 §10 "cheap extras" comment two lines above at `:170`. Fix: mirror `:170` —
  `var rule = thresholds is not null ? thresholds.RuleFor(Definition) : ThresholdEvaluator.RuleFor(Definition, _settings);`
- **S3 — README claims a default that decision A explicitly rejected.** `README.md:29`: "**Histogram** (offered on
  every tile, frame time by default)". `Auto` is unchanged, so a frame-time tile still resolves to Sparkline; a
  user who upgrades, reads this and sees a sparkline will file it as a bug. Fix: "offered on every tile, and the
  kind to pick for frame time".
- **S4 — capture 3 does not show what its row claims.** `tools/Stats.UiPreview/captures/baseline.json:81` uses
  `Height: 800`, but in `v14-dashboard-game-tiles-large-dark-amber.png` the second Game row begins at y≈778 and
  only ~12 px of the L Histogram tile's top edge is inside the frame — yet `docs/game-tiles/EVIDENCE.md:83`
  describes both tiles rendering at L. Fix: raise that entry's `Height` to ≥ 1000, re-run the batch, rewrite the
  row from the new PNG (same treatment the doc already gives cases 7 and 8).
- **S5 — capture 1's description misreads the image.** `docs/game-tiles/EVIDENCE.md:81` says "one tall bar near
  the right (the shaped spike data)". In the PNG the single tall bar is **bin 0 at the far left** (~55 of 60
  samples at the 6.9 ms baseline); the tall thin element near the right is the **p99 marker line**, and the five
  spike bins render as ~0.6 px slivers. Fix: correct the row, and use it to record honestly that a heavy-tailed
  frame-time distribution at 12 linear bins leaves 11 of 12 bars near-invisible (owner item 11 below).
- **S6 — the p1 / left-edge marker branch is never captured.** `HistogramBars.cs:182` flips the label right
  (`x + 3`) only when `MarkerFraction <= 0.5`; every capture on the branch is a p99 case at fraction 1.0, so the
  flip and the left-edge marker are covered by a unit test only. Fix: add one `baseline.json` entry putting
  Histogram on `fps.avg` (LowerIsWorse ⇒ p1) — e.g. a `game-tiles-histogram-fps` substate or `gallery` +
  `gallery.inverted` — so both marker sides are visually verified before the PR.

## Nits

- **N1 —** `HistogramBars.cs:171,206`: the label row reserves 12 px (`top = 12`, `available = h - 13`) but
  `FormattedText` at em size 10 Segoe UI is ~13.3 px tall, so the descender of "p" can graze the top of a
  full-height bar directly beneath the label. Fix: reserve 14–15 px, or measure `ft.Height` once.
- **N2 —** `HistogramBars.cs:168,174`: at `MarkerFraction` 0 or 1 the 1 px marker is centred on x = 0 / x = w, so
  half of it falls outside the element and it reads at half intensity — the common case, since nearest-rank p99
  ≡ max for short windows (N8). Fix: `x = Math.Clamp(fraction * w, 0.5, w - 0.5)`.
- **N3 —** `HistogramBars.cs:183`: when `ft.Width > w` the clamp collapses to 0 and the label is drawn past the
  element's right edge (`ClipToBounds` is not set), bleeding into neighbouring chrome. Not reachable at M/L
  today. Fix: `ClipToBounds = true` in the ctor.
- **N4 —** `HistogramBars.cs:209,236`: `_highlightGeometry` is built and frozen on every bins change even when
  `GraphStyle.Effects` is off, where `:162` never draws it. Fix: skip the highlight pass in `RebuildBars` when
  effects are off and add `effects` to the geometry cache key.
- **N5 —** `src/Stats.Core/Metrics/HistogramBinning.cs:30`: `Bin` skips only NaN, so ±Infinity counts as finite and
  makes `range` infinite (every bin index falls to 0). Unreachable from `MetricHistory` (gaps are always NaN).
  Fix if wanted: `if (!float.IsFinite(v)) continue;` in both loops.
- **N6 —** `src/Stats.Core/Settings/TilePref.cs:28`: `Enum.TryParse<TileKind>("99")` succeeds and yields the
  undefined `(TileKind)99` rather than `Auto`. It degrades safely via `TileTemplateSelector`'s `_ => Sparkline`
  and is verbatim `DashboardLayoutModeConverter` behaviour, so it is a shared deliberate trait — noted only so
  the next copy doesn't rediscover it. Optional fix: `&& Enum.IsDefined(kind)`.
- **N7 —** `docs/game-tiles/EVIDENCE.md:12-24` still calls Task 4's files "uncommitted" on top of `51b0746`; they
  are committed as `d98e820` (branch tip). Fix: update the candidate commit.
- **N8 —** Nearest-rank p99 degenerates at short windows: at the default `HistoryWindowMinutes = 2` / 1 s poll
  (120 samples) the marker is the 2nd-largest sample, and at the 30-sample floor it is exactly the maximum,
  duplicating `HistogramMaxText` directly beneath it ("p99 17.6 ms" over "17.6 ms" in captures 1 and 6; p1
  likewise collapses to the minimum). Spec-conformant (decisions 2 and D pin `FrameStatsAggregator`'s rank rule),
  so recorded rather than changed.
- **N9 —** `HistogramBars.cs:217`: a non-empty bin whose count is a small fraction of `maxCount` draws a
  sub-pixel bar (0.56 px in capture 1) that antialiases to a faint smear. A 1 px floor —
  `double barH = c > 0 ? Math.Max(1, …) : 0;` — would make sparse bins readable, but it deviates from the spec's
  literal `Rect` formula, so it needs owner sign-off (item 11).

## Owner checklist additions

On top of the spec's 8 items:

9. Confirm on upgrade that an existing frame-time tile is **not** silently re-templated to Histogram (decision A)
   — and that the corrected README wording (S3) matches what the app actually does.
10. Put the `settings.json` downgrade warning in the **GitHub Release body** at tag time, not only in the README —
    the spec requires it in the release notes and the repo has no CHANGELOG.
11. Judge the 12-linear-bin histogram against a real session: with one dominant bin the other 11 bars are
    sub-pixel (S5/N9). Decide keep-as-spec'd vs. a 1 px floor for non-empty bins, log-scaled heights, or trimming
    the range to the marker percentile.
12. Check the FpsSummary tile at **L** (capture 3): the value row centres in a 192 px tile and leaves a large gap
    between the name and the reading; confirm that reads as intended rather than as a layout bug.

## Fix wave

Applied by the controller on top of `d98e820`: **S1** fixed (bins sum/max in the cache key), **S2** fixed (index-
or-settings rule lookup mirrors the Severity line), **S3** fixed (README wording), **S4** fixed (1180×1000
re-capture), **S5** fixed (evidence row corrected), **S6** fixed (`game-histogram-fps` substate + capture showing
the p1 marker), **N2** fixed (marker clamp). Remaining nits (N1, N3–N9) and owner items 9–12 left as documented.
Gates: build 0 warnings, 836 + 219 tests green.
