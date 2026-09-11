# Graph smoothing, fixed time axis, and effects — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-graph-effects-design.md`. Branch `feature/graph-effects` from master
`75115aa`, worked in the `Stats-timescale` worktree. Gates at every checkpoint: `dotnet build --nologo`
(0 warnings) and `dotnet test --nologo` (green, 0 warnings). Commit prefixes per CLAUDE.md.

- [x] **Task 1 — Core math + settings** (`feat(core):`): `CurveSmoothing`, `SampleAxis`,
      `AppSettings.SmoothLines/GraphEffects`, `SettingsViewModel` toggles + `SettingsChange.Graphs`,
      `MetricTileViewModel.HistoryCapacity` (+ detail VM). Tests per the spec's acceptance list.
- [ ] **Task 2 — Controls** (`feat(app):`): `GraphStyle`, `Sparkline`/`HistoryChart` fixed axis + curve + glow +
      pulse, `LevelBar`/`ArcGauge` gradient/glow + eased fraction, `Capacity` bindings, Settings tab "Graphs"
      header, `App.xaml.cs` wiring.
- [ ] **Task 3 — Harness + docs** (`feat(tools):`/`docs:`): substates, warm-up fixture, `baseline.json`,
      captures in `artifacts/graph-effects/`, README paragraph.
- [ ] **Task 4 — Review**: Opus whole-branch review + fix wave; PR to master with owner checklist.
