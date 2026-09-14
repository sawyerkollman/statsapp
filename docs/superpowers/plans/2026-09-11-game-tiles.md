# Game-group tile kinds: histogram and FPS summary — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-game-tiles-design.md`. Branch `feature/game-tiles` from `feature/v1.10`
`4323b0b`, worked in its own worktree (e.g. `Stats-game-tiles`). One Sonnet implementer per task, tasks run in
order (each builds on the previous task's committed work), **disjoint file ownership** — a task touches only the
files listed for it; anything else it needs is a finding for the controller, not an edit. Gates at every
checkpoint, re-run by the controller: `dotnet build --nologo` (0 warnings) and `dotnet test --nologo` (green,
0 warnings). Commit prefixes per CLAUDE.md; push after each commit.

- [x] **Task 1 — Core maths, roles, settings** (`feat(core):`): `TileKind` += `Histogram`, `FpsSummary`
      (appended) and `TileKindConverter` (lenient, copy of `DashboardLayoutModeConverter`) applied to
      `TilePref.Kind`; new `HistogramBinning` (`Bin`, `Percentile`, `Fraction`, `DefaultBinCount = 12`); new
      `GameMetricRoles` (`GameMetricRole`, `RoleOf`, `Find`). Tests: `HistogramBinningTests`,
      `GameMetricRolesTests`, `TileKindTests`, the three `SettingsServiceTests` additions — names and assertions per
      the spec's Acceptance list.
      Files: `src/Stats.Core/Settings/TilePref.cs`, `src/Stats.Core/Metrics/HistogramBinning.cs` (new),
      `src/Stats.Core/Frames/GameMetricRoles.cs` (new), `tests/Stats.Core.Tests/HistogramBinningTests.cs` (new),
      `tests/Stats.Core.Tests/GameMetricRolesTests.cs` (new), `tests/Stats.Core.Tests/TileKindTests.cs` (new),
      `tests/Stats.Core.Tests/SettingsServiceTests.cs` (append only).
      Gate: build + tests; the existing `"Kind": "Gauge"` round-trip and every layout-mode/graph-effects test still
      pass untouched.
- [x] **Task 2 — Tile view model** (`feat(core):`): `MetricTileViewModel` optional `MetricStore? store` ctor
      argument, sibling resolution via `GameMetricRoles` in the ctor, `GameRole`, the nine new observable outputs,
      `RefreshHistogram`/`RefreshFpsSummary` gated on `_store != null` and `Kind` (alternating `int[12]` buffers,
      reusable percentile scratch, p1 for `LowerIsWorse` rules), the `FpsSummary` line in `ResolveKind`,
      `AutomationLabel` suffixes, `RaiseSeverityRefresh` also raising `FpsLowSeverity`;
      `DashboardViewModel.RebuildSections` passes `_store`. `OverlayViewModel` untouched. Tests:
      `GameTileViewModelTests` (new) plus the `ViewModelTests` and `DashboardViewModelTests` additions per the spec.
      Files: `src/Stats.Core/ViewModels/MetricTileViewModel.cs`, `src/Stats.Core/ViewModels/DashboardViewModel.cs`,
      `tests/Stats.Core.Tests/GameTileViewModelTests.cs` (new), `tests/Stats.Core.Tests/ViewModelTests.cs` (append
      only), `tests/Stats.Core.Tests/DashboardViewModelTests.cs` (append only).
      Gate: build + tests; `Tile_Kind_AutoRules`, `Tile_Kind_PercentOutsideCpuGpu_StaysSparkline`,
      `Tile_Kind_And_Size_PrefOverride` and every `MetricTileViewModelTests` case pass unmodified (Auto path
      byte-identical).
- [x] **Task 3 — App control, templates, menu** (`feat(app):`): new `HistogramBars` control (DPs `Bins`,
      `MarkerFraction`, `MarkerLabel`, `Stroke`, `Track`; cached geometry/pens; `ThemeManager.Changed`/
      `GraphStyle.Changed` subscription; control-drawn label; effects = gradient + highlight + marker glow; no
      animation); `CurveRenderer.BarBrushFor`; `TileHistogram` and `TileFpsSummary` DataTemplates and the two
      `TileSelector` attributes; `TileTemplateSelector.Histogram/FpsSummary`; `OpenTileMenu` `MenuKinds` +
      `KindHeader` + `GameRole` gating. No `App.xaml.cs`, `Theme.xaml`, overlay, or settings-tab changes.
      Files: `src/Stats.App/Controls/HistogramBars.cs` (new), `src/Stats.App/Controls/CurveRenderer.cs`,
      `src/Stats.App/Views/TileTemplates.xaml`, `src/Stats.App/Views/TileTemplateSelector.cs`,
      `src/Stats.App/Views/DashboardWindow.xaml.cs`.
      Gate: build (0 warnings, XAML included) + tests; the controller additionally runs one smoke capture that
      instantiates both new templates before Task 4's fixtures exist, via the harness's compatibility-settings
      path (`CaptureSpec.Settings` → `PreviewComposition.Build(..., settingsFixturePath)` loads that file in place
      of the scenario's own selection): write a scratch `settings.json` containing
      `"DashboardMetrics": ["fps.avg","fps.low1","fps.frametime"]` and
      `"TilePrefs": { "fps.avg": { "Kind": "FpsSummary" }, "fps.frametime": { "Kind": "Histogram" } }`, then
      `dotnet run --project tools/Stats.UiPreview -- --scenario dense --view dashboard --settings <that file> --theme "Dark Amber" --width 1180 --height 720 --method rtb --output artifacts/game-tiles/smoke-dense.png`
      (`dense` has series for all three Game ids), and check the PNG shows both tiles and the sidecar `Warnings`
      is empty (no binding/resource errors from the new templates).
- [x] **Task 4 — Harness, captures, docs** (`feat(tools):` then `docs:`): `game` scenario (shaped frame-time
      series via `AddExact`, preset prefs) appended to `Scenarios.Names`; dashboard substates `game-tiles`,
      `game-tiles-large`, `game-tiles-small`, `game-summary-orphan` in `PreviewComposition.ApplySubstate` and
      `SubstateCatalog`; the ten `baseline.json` entries under `artifacts/game-tiles/`; `GameTilesSubstateTests`;
      full `baseline.json` batch run (`dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`),
      PNGs inspected; `docs/game-tiles/EVIDENCE.md` (format of `docs/graph-effects/EVIDENCE.md`, including the
      "could not show" list); README "Tiles" paragraph and the new Game-tiles bullet.
      Files: `tools/Stats.UiPreview/Fixtures/Scenarios.cs`, `tools/Stats.UiPreview/PreviewComposition.cs`,
      `tools/Stats.UiPreview/SubstateCatalog.cs`, `tools/Stats.UiPreview/captures/baseline.json`,
      `tests/Stats.UiPreview.Tests/GameTilesSubstateTests.cs` (new), `docs/game-tiles/EVIDENCE.md` (new),
      `README.md`.
      Gate: build + tests (both test projects; `FixtureValidityTests`/`CompositionIsolationTests` now include `game`);
      batch reports 0 failed and every new sidecar `Warnings: []`.
- [x] **Task 5 — Review**: Opus whole-branch review (`docs/game-tiles/REVIEW.md`, findings B/S/N-numbered as in
      `docs/graph-effects/REVIEW.md`) + fix wave in the same file-ownership split; re-run both gates and the full
      capture batch; PR to `feature/v1.10` with the spec's owner checklist (items 1–8) in the description.
