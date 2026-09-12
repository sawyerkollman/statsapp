# Toast alerts — implementation evidence

Task 3 (harness + captures + docs) of `docs/superpowers/plans/2026-09-11-toast-alerts.md`, per
`docs/superpowers/specs/2026-09-11-toast-alerts-design.md`'s "Preview harness" and "Acceptance → Tests (harness) /
Captures" sections. Adapted from `docs/graph-effects/EVIDENCE.md`'s format — this feature has no separate
routing/task ledger, so only the sections that apply are kept.

## Candidate

- Date/time: 2026-09-12 (Windows 11 Pro, this session's worktree)
- Repo/branch: `feature/toast-alerts` (worktree `C:\claude-projects\Stats-wt\toast-alerts`)
- Candidate commit: the branch tip — Tasks 1–3 (`b7de6df`, `572a7f7`, `7fa88f2`) plus the review fix wave commit
  that follows `7fa88f2` (see "Fix wave" below). The captures in this report were taken at `572a7f7` + Task 3's
  files; the fix wave touched no harness file and no capturable surface (Settings tab XAML unchanged), so they
  stand.

## Fix wave (after `docs/toast-alerts/REVIEW.md`)

- S1 — `AlertEvent` gained a defaulted trailing `Format` member stamped from `MetricDefinition.Format` by
  `AlertEngine.Tick`; `PeakText` (toast body, `Message`) and `AlertRowViewModel` format the peak with it, so an F1
  metric reads "15.7 GB" everywhere. Tests: `Raise_StampsTheDefinitionFormat_SoToastAndLogRoundLikeTheTile`,
  `NotificationBody_UsesTheEventFormat_SoAnF1MetricReadsLikeItsTile`. Existing `Message_*` tests unchanged.
- S2 — `App.ShowAlertNotification` clamps the title to 63 and the body to 255 characters with an ellipsis
  (`ClampForShell`) before `ShowNotification`.
- N1 — a null `_tray` is traced as `failed: no tray icon` before the cool-down is consumed.
- N2 / N3 — policy doc comment states the UI-thread contract; spec's backwards-clock note corrected.
- N4 — this section. Task 3 shipped as one commit (`feat(tools)`), not the two the plan named.
- N5 / N6 — not changed (consistency with the existing Alerts rows; `ToString()` is unconsumed).
- Gates after the fix wave: `dotnet build --nologo` 0 warnings; `dotnet test --nologo` 826 + 200 green.
- .NET SDK: 9.0.316 (targeting `net8.0-windows`); Windows 11 Pro 10.0.26220.
- Overall status: implemented, captured, visually inspected. Build/tests/captures all pass — see "Gate outcomes".

## What changed in the harness (Task 3 scope)

- `tools/Stats.UiPreview/PreviewComposition.cs` `ApplySubstate`: new settings-level substate `alerts-notify-off`,
  added right after the existing `settings-update-error` case (end of the "settings" block), exactly as the spec's
  code shows — opens the flyout to the Settings tab (`FlyoutTabIndex = 1`) and sets
  `c.SettingsVm.AlertNotificationsEnabled = false` through the view model (not `c.Settings` directly), so the
  checkbox binding sees it and the write-through records one `settings.save`. No other substate touched; the
  harness still never constructs a `TaskbarIcon` — this feature adds no new production service for
  `CompositionIsolationTests` to guard against (the notification policy is a plain Core class, and the tray wiring
  lives in `App.xaml.cs`, which the harness never loads).
- `tools/Stats.UiPreview/SubstateCatalog.cs`: `alerts-notify-off` added to the `["settings"]` allow-list (it is not
  valid for any other view, including `alerts`).
- `tools/Stats.UiPreview/captures/baseline.json`: 4 new entries appended after the graph-effects block (below).
- `tests/Stats.UiPreview.Tests/ToastAlertsSubstateTests.cs` (new, 3 tests): `AlertsNotifyOffSubstate_TurnsNotificationsOff_KeepsSkipWhenForeground_AndRecordsOneSave`,
  `NoSubstate_DefaultsBothNotificationSettingsOn`, `SubstateCatalog_AcceptsAlertsNotifyOffForSettings_RejectsItForAlertsView`
  — same composition-only, no-WPF-window style every other settings-level substate test in this project uses (see
  `DashboardLayoutSubstateTests`, `GraphEffectsSubstateTests`). `CompositionIsolationTests` is unchanged and still
  green; the new substate only records one `settings.save`, already inside its allowed-command-verb list.

## Captures

Command: `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`
(whole manifest — there is no `--filter`/subset flag on the batch command, so per the task instructions the whole
manifest was run rather than a hand-picked subset). `"Method": "rtb"` on every entry. DPI 96×96 (logical ==
physical at `UiScale 1.0`), Invariant culture, fixed fixture time `2026-09-06T14:30:00Z`, seed `1234`.
`SourceCommit` in every sidecar is `572a7f7...` (HEAD); every sidecar's `Warnings` array is empty; `SimulatedServices`
lists only the fake reader/backend/marker/startup/update/settings entries (no production service resolved). Batch
reported **68 manifest entries → 73 capture(s) written, 0 entries failed** (one entry, `v6-theme-cycle`, fans out
into 6 themed captures). All 4 new entries land under `artifacts/toast-alerts/` (git-ignored, matching the existing
`artifacts/ui-polish/`/`artifacts/graph-effects/` convention), one PNG + one JSON sidecar per entry.

| Case | Scenario/view/substate | Theme | Size | File | Observation |
| --- | --- | --- | --- | --- | --- |
| Alerts tab, default | `normal` / `settings` / `category-alerts` | Dark Amber | 1180×720 | `v14-settings-alerts-default-dark-amber.png` | Alerts category selected; existing rows (Notify on sustained critical readings, Hold time slider at 10 s, Play a sound with the notification unchecked) untouched above a new "Windows notifications" header; both new checkboxes (Show Windows notifications / Only when the dashboard is not in the foreground) show checked with orange checkmarks — the shipped defaults — and the click-to-open/Focus Assist caption renders below them. |
| Alerts tab, default (Light) | `normal` / `settings` / `category-alerts` | Light | 1180×720 | `v14-settings-alerts-default-light.png` | Same content on the Light theme preset; both new checkboxes render as checked orange checkmarks against the light background, caption text readable in the secondary-text colour. |
| Alerts tab, notify off | `normal` / `settings` / `alerts-notify-off+category-alerts` | Dark Amber | 1180×720 | `v14-settings-alerts-notify-off-dark-amber.png` | "Show Windows notifications" is unchecked (empty box); the sub-option "Only when the dashboard is not in the foreground" still shows its checkmark but renders visibly dimmed/greyed (its own text is a duller grey than the header/other rows) — confirms `IsEnabled="{Binding AlertNotificationsEnabled}"` on the sub-checkbox actually disables it in a real render, not just in XAML markup. |
| Peaks Alerts tab, unchanged | `normal` / `alerts` / `ongoing` | Dark Amber | 640×480 | `v14-alerts-ongoing-unchanged.png` | One alert row: `Tctl/Tdie`, peak `96 °C`, threshold `≥ 92 °C`, duration `ongoing` — identical content/layout to the pre-existing `v10-alerts-ongoing.png` baseline entry (same Scenario/View/Substate/Theme/Size). Byte comparison below confirms this task made no visual change to the Peaks Alerts tab. |

### Pixel/byte comparison: entry 4 vs. the ui-polish baseline

`artifacts/ui-polish/before/v10-alerts-ongoing.png` **does exist** — it was regenerated in this same batch run
(baseline.json's own pre-existing entry for it, captured alongside the 4 new toast-alerts entries in one batch
invocation, same Scenario/View/Substate/Theme/Size inputs). SHA-256 of both files:

```
ba604928d63c9ad60f571bc727611f3e9b7cfe6016ecc0c9ee273d9c5d2f1988  artifacts/ui-polish/before/v10-alerts-ongoing.png
ba604928d63c9ad60f571bc727611f3e9b7cfe6016ecc0c9ee273d9c5d2f1988  artifacts/toast-alerts/v14-alerts-ongoing-unchanged.png
```

Byte-identical (`cmp` reports no difference; both files are also identical in size, 11,539 bytes). This is exactly
what the spec's entry 4 is meant to document: the Peaks Alerts tab (log row rendering, "ongoing" duration, colour
of the peak value) is untouched by this feature — the toast/notification path is additive and never reaches the
log-row rendering code.

## What the harness could not show

Copied verbatim from the spec's "Preview harness" section:

The toast itself, its icon and app-name attribution, the click → `ShowDashboard()` activation, the absence of the
shell sound, the real-time cool-downs (unit-tested instead), `Window.IsActive` foreground detection,
`--minimized` launches, Focus Assist / Do not disturb, Action Center, Windows' per-app notification toggle, and
the logon/explorer-restart race. `--method rtb` also excludes popups and title bars, so only the Settings tab and
the Peaks Alerts tab are capturable. All of this remains on the spec's owner checklist (items 1–11), which needs
the running app.

## Gate outcomes

| Gate | Result | Evidence |
| --- | --- | --- |
| Build | Pass | `dotnet build --nologo` — 0 warnings, 0 errors. |
| Tests | Pass | `dotnet test --nologo` — Stats.Core.Tests 824/824, Stats.UiPreview.Tests 200/200 (197 pre-existing + 3 new in `ToastAlertsSubstateTests.cs`), 0 failures, 0 warnings. |
| Captures | Pass | Full `baseline.json` batch: 68 manifest entries → 73 capture(s) written, 0 failed, every sidecar `Warnings: []`. |

## Final handoff

- Concrete change: the preview harness gains one new settings substate (`alerts-notify-off`) and 4 new
  `baseline.json` entries covering the Alerts settings category (default, on both themes, and notify-off) plus a
  no-op-content confirmation capture of the Peaks Alerts tab.
- Design adjustment: none to the App/Core code from Tasks 1–2 — this task only touched the harness
  (`tools/Stats.UiPreview`), its tests, `README.md` (Alerts + Tray paragraphs), and this evidence doc.
- Simulation-only evidence: all 4 new captures above (fake sensors/fan backend/settings persistence, no hardware,
  no real `TaskbarIcon`).
- Real Windows/hardware checks performed: none in this task — build/tests ran directly in this environment; no
  `dotnet run --project src/Stats.App` smoke test (elevation prompt can't be satisfied from this shell, per
  `CLAUDE.md` rules 7/8).
- Remaining checks: the spec's owner checklist (items 1–11 — the real toast appearing/clicking, sound behaviour,
  cool-down behaviour observed live, Focus Assist/Action Center, the logon/explorer-restart race, and the
  v1.9.x settings upgrade path) needs the real running app and is unverified here.
