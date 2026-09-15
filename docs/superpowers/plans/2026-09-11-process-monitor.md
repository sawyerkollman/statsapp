# Per-process top consumers — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-process-monitor-design.md` (its Core section names every new type; its
Acceptance section names every test). Branch `feature/process-monitor` from `feature/v1.10`, worktree
`C:\claude-projects\Stats-wt\process-monitor`; PR back to `feature/v1.10`. Gates at every checkpoint:
`dotnet build --nologo` (0 warnings) and `dotnet test --nologo` (green, 0 warnings — both test projects); the
controller re-runs both itself and commits after each task. One Sonnet implementer per task with the listed files
as its only write scope; each task starts from the previous task's committed tip. Rules that bind every task:
CLAUDE.md 1 (the sampler has its own thread and never touches LibreHardwareMonitor or the poller), 3 (settings
defaults/compat), 7/8 (build + tests + `tools/Stats.UiPreview --method rtb` are the only automated evidence; the
real `SystemProcessSource`/`GpuEngineCounters` tests run against this machine like `PerfCounterSensorReaderTests`).
No new NuGet packages.

- [ ] **Task 1 — Pure core + settings** (`feat(core): process sample model, CPU tracker, grouping, formatting,
      sampling setting`). Owns: `src/Stats.Core/Processes/ProcessSample.cs` (new; the records),
      `src/Stats.Core/Processes/ProcessSortColumn.cs` (new), `src/Stats.Core/Processes/IProcessSource.cs` (new),
      `src/Stats.Core/Processes/ProcessCpuTracker.cs` (new), `src/Stats.Core/Processes/ProcessGrouping.cs` (new),
      `src/Stats.Core/Processes/SlowSampleGate.cs` (new), `src/Stats.Core/Processes/ProcessFormat.cs` (new),
      `src/Stats.Core/Settings/AppSettings.cs`, `src/Stats.Core/ViewModels/SettingsViewModel.cs` (`ProcessSamplingEnabled`
      + `SettingsChange.Processes` appended last), `tests/Stats.Core.Tests/ProcessCpuTrackerTests.cs` (new),
      `ProcessGroupingTests.cs` (new), `SlowSampleGateTests.cs` (new), `ProcessFormatTests.cs` (new),
      `SettingsServiceTests.cs` (append), `SettingsViewModelTests.cs` (append). Tests: the matching Acceptance lists.
- [ ] **Task 2 — Sampler and real sources** (`feat(core): background process sampler, GPU engine counters, system
      process source`). Owns: `src/Stats.Core/Processes/ProcessSampler.cs` (new; modelled on `SensorPoller`),
      `src/Stats.Core/Processes/GpuEngineCounters.cs` (new), `src/Stats.Core/Processes/SystemProcessSource.cs` (new),
      `tests/Stats.Core.Tests/ProcessSamplerTests.cs` (new, with its nested `FakeSource`),
      `GpuEngineCountersTests.cs` (new), `SystemProcessSourceTests.cs` (new). Tests: the matching Acceptance lists
      (the real-machine tests must be robust to a machine without the GPU Engine category).
- [ ] **Task 3 — View models** (`feat(core): process list view model and Peaks integration`). Owns:
      `src/Stats.Core/ViewModels/ProcessListViewModel.cs` (new; `ProcessListViewModel` + `ProcessRowViewModel`),
      `src/Stats.Core/ViewModels/PeaksViewModel.cs`, `tests/Stats.Core.Tests/ProcessListViewModelTests.cs` (new),
      `tests/Stats.Core.Tests/PeaksViewModelTests.cs` (append). Tests: the matching Acceptance lists (in-place row
      updates, sort columns, GPU-unavailable fallback, unavailable/off/sampling states, TSV).
- [ ] **Task 4 — App** (`feat(app): Processes tab in the Peaks window, sampler lifetime, Monitoring setting`).
      Owns: `src/Stats.App/Views/PeaksWindow.xaml`, `src/Stats.App/Views/PeaksWindow.xaml.cs`,
      `src/Stats.App/App.xaml.cs`, `src/Stats.App/Views/DashboardWindow.xaml` (Monitoring tab only), `README.md`.
      Does: spec "App" in full — the Processes tab (sortable headers, GPU column visibility, status/summary text,
      Copy as TSV with the copy-error pattern), the sampler constructed in the composition root with
      `SystemProcessSource`, paused while the Peaks window is closed and resumed on show, `SettingsChange.Processes`
      handling, the "Sample processes" checkbox, README section. Gate adds one smoke capture of the Peaks window's
      Processes tab through the harness only if Task 5's fixture is not needed for it — otherwise build + tests.
- [ ] **Task 5 — Harness + evidence** (`feat(tools): process substates, fake process source, captures` then
      `docs: process monitor evidence`). Owns: everything under `tools/Stats.UiPreview/` the spec's "Preview harness"
      section names (the `FakeProcessSource` fixture, process ticks in the `normal`/`dense` fixtures, the `processes`
      view and its eight `proc-*` substates, `SubstateCatalog`, `baseline.json`, the isolation metadata),
      `tests/Stats.UiPreview.Tests/ProcessSubstateTests.cs` (new), `tests/Stats.UiPreview.Tests/CompositionIsolationTests.cs`
      and `IsolationMetadataTests.cs` (the additions the spec lists), `docs/process-monitor/EVIDENCE.md` (new).
      Does: the substates, manifest rows and captures (every sidecar `Warnings: []`), the harness tests, the
      evidence report with the "cannot show" list (real process data, GPU counters on this machine, pause/resume
      timing, CPU cost of sampling).
- [ ] **Task 6 — Review**: Opus whole-branch review → `docs/process-monitor/REVIEW.md` (rule 1 thread isolation,
      sampler lifetime and shutdown join, per-sample allocation and cost, elevated-process access failures, GPU
      counter category absence/slowness, settings compat, XAML bindings, TSV/clipboard error path); fix wave; gates
      + captures re-run; PR `feature/process-monitor` → `feature/v1.10` with the spec's owner checklist.
