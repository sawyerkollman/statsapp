# Windows notifications for alerts — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-toast-alerts-design.md`. Branch `feature/toast-alerts` from
`feature/v1.10` @ `4323b0b` (the controller creates and pushes the branch; implementers never run
checkout/stash/reset/branch). Gates at every checkpoint: `dotnet build --nologo` (0 warnings) and
`dotnet test --nologo` (green, 0 warnings — xUnit analyzers on, constants in the `expected` slot); the controller
re-runs both itself after each task before the next one starts. Commit prefixes per CLAUDE.md. The app cannot be
launched from this shell (CLAUDE.md rules 7/8): build + tests + `tools/Stats.UiPreview --method rtb` captures are
the only automated evidence; everything else is the spec's owner checklist.

Untracked `.codex/`, `AGENTS.md`, `HANDOFF.md`, `Stats-UI-Codex-Handoff/` sit in the tree and must never be
staged — every commit uses `git add <explicit paths>`, never `git add -A` / `git add .`. Each task is one Sonnet
implementer with the file set below as its *only* write scope (disjoint across tasks); each task starts from the
previous task's committed tip.

- [x] **Task 1 — Core policy, text, settings** (`feat(core): alert notification policy, text, and settings`).
      Owns: `src/Stats.Core/Alerts/AlertNotificationPolicy.cs` (new), `src/Stats.Core/Alerts/AlertEvent.cs`,
      `src/Stats.Core/Settings/AppSettings.cs`, `src/Stats.Core/ViewModels/SettingsViewModel.cs`,
      `tests/Stats.Core.Tests/AlertNotificationPolicyTests.cs` (new), `tests/Stats.Core.Tests/AlertEventTests.cs`
      (new), `tests/Stats.Core.Tests/SettingsServiceTests.cs`, `tests/Stats.Core.Tests/SettingsViewModelTests.cs`.
      Does: `AlertNotificationPolicy` (`GlobalCooldown`/`PerMetricCooldown`, static `ShouldNotify`, `TryAccept`,
      `Reset`) exactly as specified; `AlertEvent.NotificationTitle` + `NotificationBody(int holdSeconds)` with
      `Message` refactored onto shared private helpers and byte-identical output; `AppSettings` region
      `// ---- toast alerts ----` with `AlertNotificationsEnabled`/`AlertNotificationsSkipWhenForeground` (both
      default `true`, no `Normalize` change); `SettingsViewModel` observable mirrors + `On…Changed` handlers raising
      `SettingsChange.Alerts` (enum untouched). Tests: every name in the spec's Acceptance "Tests (Core)" bullets;
      the existing `Message_*` and alert settings tests must pass unchanged. Gate: build + tests. Push.
- [x] **Task 2 — App wiring + Settings tab** (`feat(app): Windows notifications for critical alerts, click opens the dashboard`).
      Owns: `src/Stats.App/App.xaml.cs`, `src/Stats.App/Views/DashboardWindow.xaml`. Does: `using H.NotifyIcon.Core;`
      and the `_alertNotifications` field; `SetupTray()` gains `TrayBalloonTipClicked → ShowDashboard()` and the
      `TrayBalloonTipShown` trace; `EvaluateAlerts()` loop → `ShowAlertNotification(evt, nowUtc)` (old
      `"Stats alert"` balloon line deleted, chime untouched, one `nowUtc` shared with `Tick`); new
      `ShowAlertNotification` (gate → cool-down → `ShowNotification(title, body, NotificationIcon.Warning,
      sound: false)` in try/catch, one trace per outcome); `case SettingsChange.Alerts` resets the engine and the
      policy when `AlertsEnabled` flips off; Alerts `TabItem` gains the "Windows notifications" header, two
      checkboxes (sub-option `IsEnabled` bound to the master, `Margin="20,4,0,0"`), and the secondary caption —
      existing rows untouched. No `ClearNotifications`, no `customIconHandle`, no fan/poller/LHM code, nothing
      outside the Dispatcher path. Gate: build + tests (no new automated tests possible for App; the reviewer
      reads the diff against the spec's App section line by line). Push.
- [x] **Task 3 — Harness, captures, docs** (`feat(tools): toast-alerts settings substate and captures` then
      `docs: toast alerts evidence and README`).
      Owns: `tools/Stats.UiPreview/PreviewComposition.cs`, `tools/Stats.UiPreview/SubstateCatalog.cs`,
      `tools/Stats.UiPreview/captures/baseline.json`, `tests/Stats.UiPreview.Tests/ToastAlertsSubstateTests.cs`
      (new), `docs/toast-alerts/EVIDENCE.md` (new), `README.md`. Does: `alerts-notify-off` substate (via
      `c.SettingsVm`, flyout tab 1) and its catalog entry; the four `baseline.json` entries under
      `artifacts/toast-alerts/`; run `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`
      (whole manifest — there is no filter flag), confirm `0 entries failed` and empty sidecar `Warnings`, read the
      four PNGs, pixel-compare entry 4 with `artifacts/ui-polish/before/v10-alerts-ongoing.png`; write
      `EVIDENCE.md` in the `docs/graph-effects/EVIDENCE.md` format including "What the harness could not show";
      README Alerts + Tray paragraphs. Tests: the three `ToastAlertsSubstateTests` names from the spec;
      `CompositionIsolationTests` still green (the substate only records `settings.save`). Gate: build + tests +
      captures. Push.
- [ ] **Task 4 — Review** (Opus, whole branch): diff `feature/toast-alerts` against `feature/v1.10` and check every
      spec claim (owner decisions 1–3, assumed decisions 1–12, rule 1 thread placement, rule 3 old-file loading,
      rule 4 package pinned, rule 6 no fan files touched, "existing rows/paths byte-identical when the feature is
      off", `Message` output unchanged, trace vocabulary, harness isolation). Findings → `docs/toast-alerts/REVIEW.md`;
      fix wave as `fix(core):`/`fix(app):`/`fix(tools):` commits (may touch any branch file); re-run build, tests,
      and the capture batch; update `EVIDENCE.md` "Fix wave" section. Then PR `feature/toast-alerts` →
      `feature/v1.10` with the spec's owner checklist (items 1–11) in the description, ending with the attribution
      footer from the session's system reminder.
