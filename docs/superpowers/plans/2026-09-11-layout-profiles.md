# Dashboard and overlay layout profiles — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-layout-profiles-design.md` (its Acceptance section already tags every
test with the task that owns it — T1/T2/T4). Branch `feature/layout-profiles` from `feature/v1.10`, worktree
`C:\claude-projects\Stats-wt\layout-profiles`; PR back to `feature/v1.10`. Gates at every checkpoint:
`dotnet build --nologo` (0 warnings) and `dotnet test --nologo` (green, 0 warnings — both test projects); the
controller re-runs both itself and commits after each task. One Sonnet implementer per task with the listed files
as its only write scope; each task starts from the previous task's committed tip. Rules that bind every task:
CLAUDE.md 1 (the game-mode signal is raised on the poll thread — the App marshals; nothing here touches LHM),
3 (settings defaults/null-guards/sanitation), 6 (a layout profile never writes fan or hardware state — read-only
use of `FanController`/`GameModeSwitcher`), 7/8 (build + tests + `tools/Stats.UiPreview --method rtb` are the only
automated evidence).

- [ ] **Task 1 — Settings model, game-mode signal, Fans picks** (`feat(core): layout profile model, game-mode
      transition signal, fans-window layout picks`). Owns: `src/Stats.Core/Settings/LayoutProfile.cs` (new),
      `src/Stats.Core/Settings/AppSettings.cs`, `src/Stats.Core/Settings/SettingsService.cs`,
      `src/Stats.Core/Fans/GameModeSwitcher.cs`, `src/Stats.Core/ViewModels/FansViewModel.cs`,
      `tests/Stats.Core.Tests/LayoutProfileTests.cs` (new), `tests/Stats.Core.Tests/SettingsServiceTests.cs`
      (append), `tests/Stats.Core.Tests/GameModeSwitcherTests.cs` (append), `tests/Stats.Core.Tests/FansViewModelTests.cs`
      (append). Does: `LayoutProfile` (`CloneArrangement`, `Capture`, `Restore` exactly as the spec's Settings
      section), the five new `AppSettings` fields, the `SettingsService.Normalize` block mirroring the `FanProfiles`
      one, the `GameModeSwitcher.GamingChanged` transition signal (spec "Core → GameModeSwitcher"), and the
      `FansViewModel` layout-profile pickers (spec "Core → FansViewModel"). Tests: every T1 name in Acceptance.
- [ ] **Task 2 — Dashboard profiles partial** (`feat(core): layout profile save/apply/revert/rename/delete and
      game-mode application`). Owns: `src/Stats.Core/ViewModels/DashboardViewModel.Profiles.cs` (new),
      `src/Stats.Core/ViewModels/DashboardViewModel.cs` and `DashboardViewModel.Layout.cs` (only the
      mark-modified hooks and the resync entry points the spec names), `src/Stats.Core/ViewModels/SettingsViewModel.cs`
      (`SyncShowCoreMatrix` quiet mirror), `tests/Stats.Core.Tests/LayoutProfileViewModelTests.cs` (new),
      `tests/Stats.Core.Tests/SettingsViewModelTests.cs` (append). Does: spec "Core → DashboardViewModel" in full —
      `LayoutProfileNames`, active/modified state, `SaveLayoutProfile`/`SaveActiveLayoutProfile`/`ApplyLayoutProfile`
      /`RevertLayoutProfile`/`RenameLayoutProfile`/`DeleteLayoutProfile` and their commands, `MarkLayoutModified`
      on arrangement edits only, `ApplyGameModeLayout`, one rebuild + one save per apply, fan state never touched.
      Tests: every T2 name in Acceptance.
- [ ] **Task 3 — App: View menu, Fans window picks, composition root** (`feat(app): layout profiles menu, game-mode
      layout picks, profile switching on the game-mode signal`). Owns: `src/Stats.App/App.xaml.cs`,
      `src/Stats.App/Views/DashboardWindow.xaml`, `src/Stats.App/Views/DashboardWindow.xaml.cs`,
      `src/Stats.App/Views/FansWindow.xaml`, `README.md`. Does: spec "App" in full — the View ▸ Layout profiles
      submenu (checkable names, Save, Save as…, Rename…, Delete… via `InputDialog`, enablement per state), the two
      Game-mode layout pickers on the Fans window, `App.xaml.cs` subscribing to `GamingChanged` and marshalling
      `ApplyGameModeLayout` to the Dispatcher, overlay rebuild on apply, README section. No Core changes.
- [ ] **Task 4 — Harness + evidence** (`feat(tools): layout profile preview substates and captures` then
      `docs: layout profiles evidence`). Owns: `tools/Stats.UiPreview/PreviewComposition.cs`,
      `tools/Stats.UiPreview/SubstateCatalog.cs`, `tools/Stats.UiPreview/captures/baseline.json`,
      `tests/Stats.UiPreview.Tests/LayoutProfileSubstateTests.cs` (new), `docs/layout-profiles/EVIDENCE.md` (new).
      Does: spec "Preview harness" — the substates and the seven manifest rows, the `dense` Auto re-capture proving
      `layout-profile-desk` is pixel-identical, the T4 tests, the evidence report with the harness blind spots.
- [ ] **Task 5 — Review**: Opus whole-branch review → `docs/layout-profiles/REVIEW.md` (rule 1 marshalling, rule 3
      compat incl. profiles inside old files, rule 6 no fan writes, one-rebuild/one-save per apply, menu state,
      XAML bindings); fix wave; gates + captures re-run; PR `feature/layout-profiles` → `feature/v1.10` with the
      spec's owner checklist.
