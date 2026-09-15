# Fan control backlog — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-fan-backlog-design.md`. Branch `feature/fan-backlog` from `feature/v1.10`,
worktree `C:\claude-projects\Stats-wt\fan-backlog`; PR back to `feature/v1.10`. Gates at every checkpoint:
`dotnet build --nologo` (0 warnings) and `dotnet test --nologo` (green, 0 warnings — both test projects); the
controller re-runs both itself and commits after each task. One Sonnet implementer per task with the listed files
as its only write scope; each task starts from the previous task's committed tip. Rules that bind every task:
CLAUDE.md 1 (backend I/O only in `Tick` phase 2 on the poller thread; UI mutates desired state under the gate),
2 (`FanMode`, `FanChannelStatus` append-only), 3 (settings defaults/sanitation, old files load), 6 (every fan
safety invariant — pump floor, min clamp, master off, failsafe, three failures → Auto — restated in the spec and
pinned by tests), 7/8 (build + tests + harness are the only evidence; no hardware from this shell).

- [ ] **Task 1 — Core: prefs, sanitation, controller, profile file** (`feat(core): zero-RPM stop, per-channel
      hysteresis/slew, fan profile file format`). Owns: `src/Stats.Core/Settings/FanChannelPref.cs`,
      `src/Stats.Core/Settings/SettingsService.cs` (`SanitizeChannelPref` promoted + the new clamps),
      `src/Stats.Core/Settings/FanProfileFile.cs` (new, incl. the lenient `FanModeConverter`),
      `src/Stats.Core/Fans/FanChannelView.cs`, `src/Stats.Core/Fans/FanController.cs`,
      `tests/Stats.Core.Tests/FanControllerTests.cs` (append), `tests/Stats.Core.Tests/SettingsServiceTests.cs`
      (append), `tests/Stats.Core.Tests/FanProfileFileTests.cs` (new). Does: spec Settings + Core (controller)
      + assumed decisions 1–10. Tests: the `FanControllerTests`, `SettingsServiceTests`, `FanProfileFileTests`
      lists in Acceptance; every existing fan test unchanged and green.
- [ ] **Task 2 — View models** (`feat(core): fan channel tuning and profile import/export view-model surface`).
      Owns: `src/Stats.Core/ViewModels/FansViewModel.cs`, `tests/Stats.Core.Tests/FansViewModelTests.cs` (append).
      Does: `FanChannelViewModel` new properties/mirrors/`Apply`/pushes and `StatusText` for `Stopped`;
      `FansViewModel.ExportProfileText`/`ImportProfileText`/`ProfileMessage` (assumed decisions 11–12). Tests: the
      `FansViewModelTests` list in Acceptance.
- [ ] **Task 3 — App** (`feat(app): zero-RPM and Advanced tuning on the channel card, profile import/export`).
      Owns: `src/Stats.App/Views/FansWindow.xaml`, `src/Stats.App/Views/FansWindow.xaml.cs`,
      `src/Stats.App/Controls/FanCurveEditor.cs`, `README.md`. Does: spec App section (card controls with
      eligibility gating, Advanced expander, `ProfileMessage` line, the two menu items with Win32 dialogs owned by
      the window, curve-editor stop band) and README. Every binding path must exist on the view models.
- [ ] **Task 4 — Harness + evidence** (`feat(tools): fan-backlog preview substates and captures` then
      `docs: fan backlog evidence`). Owns: `tools/Stats.UiPreview/PreviewComposition.cs`,
      `tools/Stats.UiPreview/Views/CaptureHost.cs` (the Advanced-expander visual substate),
      `tools/Stats.UiPreview/SubstateCatalog.cs`, `tools/Stats.UiPreview/Fixtures/Scenarios.cs` and
      `ScenarioFixture.cs` (fixture prefs/profile for the substates), `tools/Stats.UiPreview/captures/baseline.json`,
      `tests/Stats.UiPreview.Tests/FanBacklogSubstateTests.cs` (new), `tests/Stats.UiPreview.Tests/CompositionIsolationTests.cs`
      (the hard-coded fans substate list only), `docs/fan-backlog/EVIDENCE.md` (new). Does: the three substates,
      manifest rows, captures (all sidecars `Warnings: []`), harness tests, evidence with the "cannot show" list.
- [ ] **Task 5 — Review**: Opus whole-branch review → `docs/fan-backlog/REVIEW.md` (rule 6 invariants line by
      line against `Tick`, kick/slew ordering, `Stopped` status truthfulness, sanitation completeness, import
      trust boundary, dialog ownership/modality, XAML bindings); fix wave; gates + captures re-run; PR
      `feature/fan-backlog` → `feature/v1.10` with the spec's owner checklist.
