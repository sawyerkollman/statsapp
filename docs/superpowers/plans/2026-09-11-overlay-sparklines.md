# Overlay sparklines and status line — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-overlay-sparklines-design.md`. Branch `feature/overlay-sparklines` from
`feature/v1.10` @ `4323b0b`; PR back to `feature/v1.10`. Gates at every checkpoint: `dotnet build --nologo`
(0 warnings) and `dotnet test --nologo` (green, 0 warnings — runs both `tests/Stats.Core.Tests` and
`tests/Stats.UiPreview.Tests`); the controller re-runs both itself after each task before the next one starts.
Commit prefixes per CLAUDE.md. One Sonnet implementer per task; file ownership is disjoint, and each task builds on
the previous task's committed work. Rules that bind every task: CLAUDE.md 1 (LHM/poller thread — the overlay reads
published state only), 2 (`MetricGroup` untouched), 3 (settings defaults/compat), 4 (H.NotifyIcon 2.3.2, no new
packages), 6 (fan safety — read-only use of `FanController`), 7/8 (no app launch/PresentMon from the shell; build +
tests + `tools/Stats.UiPreview --method rtb` are the only automated evidence).

- [ ] **Task 1 — Core + settings** (`feat(core):`). `OverlayGraphs` enum + `OverlayGraphsConverter` (lenient,
      fallback `Sparkline`), `AppSettings.OverlayGraphs`/`OverlayStatusLine` in a `// ---- overlay sparklines ----`
      block, `SettingsViewModel.OverlaySparklines`/`OverlayStatusLine` (write-through, `Raise(SettingsChange.Overlay)`,
      seeded before `_loaded`), `OverlayStatus` record + `OverlayStatusComposer.Compose(...)`,
      `OverlayViewModel.ShowSparklines`/`ShowStatusLine`/`StatusText`/`StatusIsWarning`/`HasStatus`/`SetStatus(...)`
      with `ApplyLayout()` re-reading both flags. Tests exactly as the spec's Acceptance "Core tests" list (new
      `OverlayStatusComposerTests`; additions to `ViewModelTests`, `SettingsViewModelTests`, `SettingsServiceTests`).
      No `SettingsService` change (nothing to clamp); no new `SettingsChange` member.
      May touch only: `src/Stats.Core/Settings/OverlayGraphs.cs` (new), `src/Stats.Core/Settings/AppSettings.cs`,
      `src/Stats.Core/ViewModels/SettingsViewModel.cs`, `src/Stats.Core/ViewModels/OverlayViewModel.cs`,
      `src/Stats.Core/ViewModels/OverlayStatusComposer.cs` (new), `tests/Stats.Core.Tests/OverlayStatusComposerTests.cs`
      (new), `tests/Stats.Core.Tests/ViewModelTests.cs`, `tests/Stats.Core.Tests/SettingsViewModelTests.cs`,
      `tests/Stats.Core.Tests/SettingsServiceTests.cs`.
      Gate: build 0 warnings; tests green 0 warnings; `Stats.Core` still references no WPF type.
- [ ] **Task 2 — Overlay view + composition root** (`feat(app):`). `OverlayWindow.xaml`: `xmlns:controls`, tiles
      host named `TilesHost`, value row `StackPanel` bound through the existing `OverlayOrientation` converter,
      `controls:Sparkline` (`Width=56`, `Height` bound to `ValueText.ActualHeight`, `Values`/`Capacity`/`Stroke`
      bindings, `ShowGuides=False`, `IsHitTestVisible=False`, `Visibility` from `ShowSparklines`, orientation
      `DataTrigger` for margin/alignment), and the status strip `Grid` (`MaxWidth` bound to `TilesHost.ActualWidth`,
      `Visibility` from `HasStatus`, `Icon.Warn` glyph + `OverlayTextSecondary`/`OverlayWarn` text, 10 px, wrapping)
      — root `Grid`, `Border`, `LayoutTransform`, `ItemsPanel` and the move-mode `Rectangle` byte-for-byte as today.
      `DashboardWindow.xaml`: the two CheckBoxes after Click-through in the Overlay tab. `App.xaml.cs`:
      `PushOverlayStatus()` (read-only inputs: `FanController.Enabled` getter/`ActiveProfile`/`Views()`,
      `_settings.GameModeEnabled ? _gameMode.StatusText : null`, `FrameStatus()`) called from `RunCoalescedRefresh`
      (inside the existing `_overlay.IsVisible` branch), the `_overlay.IsVisibleChanged` catch-up handler, and
      `OnSettingsChanged` `case SettingsChange.Overlay` after `ApplyLayout()`.
      May touch only: `src/Stats.App/Views/OverlayWindow.xaml`, `src/Stats.App/Views/DashboardWindow.xaml` (Overlay
      `TabItem` only), `src/Stats.App/App.xaml.cs`. Must not touch `Sparkline.cs`, `CurveRenderer.cs`,
      `GraphStyle.cs`, `OverlayWindow.xaml.cs`, `Theme.xaml`, `Icons.xaml`, converters, or anything in `Stats.Core`.
      Gate: build 0 warnings; tests green; smoke capture
      `dotnet run --project tools/Stats.UiPreview -- --scenario normal --view overlay --method rtb --output artifacts/overlay-sparklines/smoke.png`
      (default settings → sparklines on) succeeds with an empty sidecar `Warnings` array and a visible sparkline
      beside each value; the same with `--substate vertical` shows it below each value.
- [ ] **Task 3 — Harness + docs** (`feat(tools):` for the harness commit, then `docs:` for README/evidence).
      `SubstateCatalog.ByView["overlay"]` += `sparklines`, `sparklines-off`, `sparklines-warmup`, `status-line`,
      `status-line-warn`; `PreviewComposition`: the `Build()` warm-up trim condition extended with
      `sparklines-warmup`, and a `// ---- overlay sparklines ----` block in `ApplySubstate` per the spec's table
      (status injected through `OverlayStatusComposer.Compose` with the spec's fixture inputs); the eleven
      `captures/baseline.json` entries under `artifacts/overlay-sparklines/`; run them (batch or individually) and
      confirm every sidecar has empty `Warnings`; `tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs`
      per the spec's "Harness tests"; `docs/overlay-sparklines/EVIDENCE.md` (one row per capture: launch command,
      what it demonstrates, sidecar warnings = none; note the harness blind spots verbatim from the spec); README
      "▣ Overlay" bullet extended.
      May touch only: `tools/Stats.UiPreview/SubstateCatalog.cs`, `tools/Stats.UiPreview/PreviewComposition.cs`,
      `tools/Stats.UiPreview/captures/baseline.json`, `tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs`
      (new), `docs/overlay-sparklines/EVIDENCE.md` (new), `README.md`. (`artifacts/` is git-ignored; captures stay
      local.)
      Gate: build 0 warnings; tests green 0 warnings; all eleven captures rendered with no sidecar warnings.
- [ ] **Task 4 — Review**: Opus whole-branch review against the spec (rule 1/6 read-only fan use, rule 3 compat,
      off-path identity, no theme-replaced brush on the panel, no continuous animation, XAML binding paths) + fix
      wave (`fix(core):`/`fix(app):`/`fix(tools):`), findings in `docs/overlay-sparklines/REVIEW.md`; the reviewer
      captures the plain `--view overlay` (400×200, rtb, Dark Amber) from a `feature/v1.10` worktree and confirms
      `v13-overlay-sparklines-off-dark-amber.png` is pixel-identical; re-run build + tests + the eleven captures;
      push; PR to `feature/v1.10` carrying the spec's owner checklist verbatim.
