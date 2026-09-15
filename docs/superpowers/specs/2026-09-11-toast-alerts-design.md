# Windows notifications for alerts — design

Date: 2026-09-11. Base: `feature/v1.10` @ `4323b0b` (master `75115aa` v1.9.2 + dashboard layout modes + graph
effects). Branch `feature/toast-alerts`. Touches only the v1.8 alert surface (`App.EvaluateAlerts`, `SetupTray`,
the Settings → Alerts tab) plus the usual settings trio (`AppSettings`/`SettingsViewModel`/the Settings tab of
`DashboardWindow.xaml`), which this branch — like the two base branches — only *appends* to.

Owner ask, paraphrased: when a sustained-critical alert fires (the existing alert that lands in the Peaks window's
Alerts tab, optionally with a sound), also raise a Windows notification via the tray icon so the user sees it when
the dashboard is hidden to the tray or behind a game; clicking the notification opens the dashboard.

## Goal

**What exists today** (v1.8 §1, `docs/superpowers/specs/2026-09-02-v1.8-monitoring-depth-design.md`): every UI-thread
refresh (`App.RunCoalescedRefresh`, `src/Stats.App/App.xaml.cs` lines 536–554) calls `EvaluateAlerts()` (560–583)
while `AlertsEnabled`; `AlertEngine.Tick` raises at most one `AlertEvent` per Crit episode once the hold time is
reached; the App then (a) adds a log row (`_alertLog.Add(evt)` → Peaks Alerts tab), (b) shows a tray balloon
`_tray?.ShowNotification("Stats alert", evt.Message)` in a try/catch, and (c) plays `SystemSounds.Exclamation` when
`AlertSoundEnabled`. Windows 10/11 already render that balloon as a toast, but: the title is a fixed "Stats alert",
the metric is buried in the body, clicking it does nothing, the shell plays its own notification sound even when
the chime is off (`sound` defaults to `true`), it fires whether or not the user is looking at the dashboard, and
nothing rate-limits several metrics going critical together. README line 75 already says "shows a tray balloon".

**This feature is the delta**, not a re-add of the balloon:

1. **Text.** Title `<metric display name> critical`; body `<peak value unit> for <hold> s (crit ≥ <threshold>)`
   (`≤` for lower-is-worse rules), formatted by `ValueFormatter` exactly like the Peaks log row.
2. **Click opens the dashboard.** `TaskbarIcon.TrayBalloonTipClicked` → the existing `ShowDashboard()` (the same
   method the tray left-click uses).
3. **Gating.** Two new settings under Settings → Alerts: "Show Windows notifications" (default on) and "Only when
   the dashboard is not in the foreground" (default on). The log row and the chime are unaffected by either.
4. **Rate limiting.** Once per alert episode (already guaranteed by `AlertEngine`), plus a global 10 s cool-down
   across all metrics and at most one notification per metric per 60 s — decided by a pure, tested Core class.
5. **One sound source.** The toast is requested with `sound: false`; the only alert sound remains the existing
   `SystemSounds.Exclamation` chime behind "Play a sound with the notification".
6. **Warn stays silent.** Unchanged — the engine only raises for Crit.

Everything else — engine, hold time, log, 200-row cap, chime, `AlertsEnabled` master switch, tray icon rendering —
is byte-identical. With "Show Windows notifications" unchecked the alert path behaves exactly as v1.9.2 minus the
balloon.

## Owner decisions (already made — do not re-litigate)

1. **API.** Use the existing `H.NotifyIcon.Wpf` **2.3.2** `TaskbarIcon` notification API; rule 4 forbids bumping the
   package and no new NuGet is allowed (no WinRT `ToastNotificationManager`, no `Microsoft.Toolkit.Uwp.Notifications`).
   Verified from the pinned package metadata (`%USERPROFILE%\.nuget\packages\h.notifyicon.wpf\2.3.2\lib\net8.0-windows7.0\`
   and `h.notifyicon\2.3.2\lib\net8.0\`):
   - `void TaskbarIcon.ShowNotification(string title, string message, NotificationIcon icon = None,
     IntPtr? customIconHandle = null, bool largeIcon = false, bool sound = true, bool respectQuietTime = true,
     bool realtime = false, TimeSpan? timeout = null)`.
   - `enum H.NotifyIcon.Core.NotificationIcon { None = 0, Info = 1, Warning = 2, Error = 3 }`.
   - `void TaskbarIcon.ClearNotifications()` — documented as "clears all notifications (active and deferred) **by
     recreating the tray icon**".
   - Routed events `TrayBalloonTipClicked`, `TrayBalloonTipShown`, `TrayBalloonTipClosed` (`RoutedEventHandler`),
     raised on the UI thread like `TrayLeftMouseUp`.
   - `timeout` is ignored by Windows Vista+ (system range 10–30 s).
2. **Settings and rate limiting.** Settings → Alerts gains "Show Windows notifications" (default **true**) and
   "Only when the dashboard is not in the foreground" (default **true**). A notification fires once per alert
   episode (on entering Crit after the hold period, never per tick), with a global 10 s cool-down across metrics and
   at most one notification per metric per 60 s.
3. **Text and click.** Title `<metric display name> critical`; body `<value unit> for <hold> s (crit ≥ <threshold>)`
   using `ValueFormatter`; the tray click handler that already shows the dashboard (`ShowDashboard()`) is reused
   for the notification click; nothing is shown for warn-level.

## Owner decisions assumed (made here because the brief left them open)

1. **Setting names:** `AppSettings.AlertNotificationsEnabled` and `AppSettings.AlertNotificationsSkipWhenForeground`
   (positive names so the default-`true` JSON reads naturally). No new `SettingsChange` member: both raise the
   existing `SettingsChange.Alerts`, because — like `AlertSoundEnabled` — the App reads them live inside
   `EvaluateAlerts` and needs no push on change.
2. **Icon:** `NotificationIcon.Warning`. Not `Error` (reads as an application failure, not a hardware reading), not a
   `customIconHandle` (`TrayIconRenderer` destroys all but the last two HICONs, so a renderer handle can be freed
   while the toast is on screen; `_appIcon` is disposed in `OnExit`). `largeIcon`, `respectQuietTime`, `realtime`,
   `timeout` keep their defaults (`false`, `true`, `false`, `null`): quiet time is honoured, and `realtime: false`
   lets Focus Assist queue the toast into Action Center instead of discarding it.
3. **Sound:** always `sound: false`. Today, with the chime off, Windows still plays its notification sound; with the
   chime on it plays twice. After this change the chime (`SystemSounds.Exclamation` behind `AlertSoundEnabled`) is
   the one and only alert sound, and it plays for every raised alert regardless of toast gating or cool-down
   (unchanged behaviour).
4. **Suppressed notifications are dropped, not deferred.** A notification refused by the foreground gate or a
   cool-down is simply not shown; the log row and chime still happen. A refused notification does **not** consume
   or extend any cool-down. Order of checks: enabled → foreground gate → cool-down. No coalesced "3 alerts" toast
   (owner decision 3 fixes per-metric text).
5. **"Dashboard in the foreground"** = `_dashboard is { IsVisible: true, IsActive: true }` evaluated on the UI
   thread at the moment the event raises. `Window.IsActive` is false when the window is hidden to the tray,
   minimized, or when any other window — a game, the Peaks window, the overlay — is the active one, so the overlay
   or Peaks being active still notifies (literal reading of the setting). `GameModeSwitcher.IsGaming` is *not*
   consulted (poll-thread state, and Windows Focus Assist already owns the game case).
6. **Never call `ClearNotifications()`** — it recreates the tray icon (flicker, re-runs the shell add, interacts with
   the per-refresh `TrayIconRenderer` icon swap). A toast already on screen stays until Windows dismisses it, also
   when the user unchecks the setting or opens the dashboard.
7. **Fix the pre-existing gap:** `AlertEngine.Reset` documents that it is "called when alerts are switched off", but
   `App.OnSettingsChanged` `case SettingsChange.Alerts` (773–775) only pushes `HoldSeconds`. This branch calls
   `_alertEngine.Reset(DateTime.UtcNow)` (and resets the notification cool-downs) when `AlertsEnabled` flips off, so a
   later re-enable cannot replay a stale hold timer and toast instantly.
8. **Body value and hold:** the body's value is `AlertEvent.PeakValue` (what `Message` and the Peaks log row show)
   and the hold is `AppSettings.AlertHoldSeconds` at the moment of raising (the setting the engine just honoured).
9. **Text helpers live on `AlertEvent`** next to `Message`, which is kept unchanged (its tests remain) even though
   App no longer calls it — removing public Core API is out of scope.
10. **Settings UI:** a second `SettingsHeader` "Windows notifications" inside the existing Alerts tab (mirrors the
    "Graphs" header graph effects added to Appearance), the sub-option indented and disabled while the master is
    off, plus a one-line secondary caption about click-to-open and Focus Assist.
11. **Diagnostics:** one `Trace.WriteLine("[Stats] …")` per outcome (requested / skipped: off / skipped: foreground
    / suppressed by cool-down / failed) and one on `TrayBalloonTipShown`, so the owner can tell "Windows held it
    back" from "Stats never asked" in the rolling trace log. These only run when an alert raises — zero idle cost.
12. **Harness:** one new settings substate `alerts-notify-off`; four `baseline.json` entries (below).

## Settings (rule 3: every field defaulted, sanitized, old files load)

New region in `src/Stats.Core/Settings/AppSettings.cs`, appended after `// ---- graph effects ----`:

```csharp
// ---- toast alerts ----
/// <summary>Raise a Windows notification (H.NotifyIcon tray balloon, rendered by Windows 10/11 as a toast) when an
/// alert raises. Independent of the log row and chime, which <see cref="AlertsEnabled"/> governs.</summary>
public bool AlertNotificationsEnabled { get; set; } = true;
/// <summary>Skip the notification while the dashboard window is visible and active (the user is already looking
/// at it). Hidden to tray, minimized, or behind another app still notifies.</summary>
public bool AlertNotificationsSkipWhenForeground { get; set; } = true;
```

- No `SettingsService.Normalize` change (bools need no sanitation). A pre-1.10 `settings.json` without either field
  loads with both `true`, so an upgraded install keeps getting a notification — now only when the dashboard is not
  the active window. `AlertsEnabled = false` still disables everything (engine never ticks), as today.
- `src/Stats.Core/ViewModels/SettingsViewModel.cs`: two observable bools mirrored in the ctor right after
  `_alertSoundEnabled = settings.AlertSoundEnabled;` (line 81), declared right after `_alertSoundEnabled` (line 165),
  with `partial void OnAlertNotificationsEnabledChanged(bool value)` and
  `partial void OnAlertNotificationsSkipWhenForegroundChanged(bool value)` placed after `OnAlertSoundEnabledChanged`
  (295–315): guarded by `if (!_loaded) return;`, write `_s.AlertNotificationsEnabled` / `_s.AlertNotificationsSkipWhenForeground`,
  then `Raise(SettingsChange.Alerts)` (save, then event). `SettingsChange` is not extended.

## Core (`Stats.Core`, testable, WPF-free)

- New `src/Stats.Core/Alerts/AlertNotificationPolicy.cs`:

  ```csharp
  /// <summary>Decides whether a raised AlertEvent becomes a Windows notification. Pure and clock-injected like
  /// AlertEngine: the App passes the same nowUtc it passed to AlertEngine.Tick. Holds two timestamps and a
  /// per-metric dictionary; nothing runs between alerts.</summary>
  public sealed class AlertNotificationPolicy
  {
      public static readonly TimeSpan GlobalCooldown = TimeSpan.FromSeconds(10);
      public static readonly TimeSpan PerMetricCooldown = TimeSpan.FromSeconds(60);

      /// <summary>The settings/foreground gate, evaluated before any cool-down so a skipped notification never
      /// consumes one: notificationsEnabled && !(skipWhenForeground && dashboardIsForeground).</summary>
      public static bool ShouldNotify(bool notificationsEnabled, bool skipWhenForeground, bool dashboardIsForeground);

      /// <summary>True — and both stamps recorded — when nowUtc is at least GlobalCooldown after the last accepted
      /// notification (any metric) AND at least PerMetricCooldown after the last accepted one for metricId (or
      /// there was none). False records nothing. Boundaries are inclusive (exactly 10 s / 60 s later is accepted).</summary>
      public bool TryAccept(string metricId, DateTime nowUtc);

      /// <summary>Forgets every stamp. Called alongside AlertEngine.Reset when alerts are switched off.</summary>
      public void Reset();
  }
  ```

  Internals: `DateTime? _lastAcceptedUtc` and `Dictionary<string, DateTime> _lastAcceptedByMetricUtc`. A clock
  that steps backwards (negative elapsed) counts as "inside the cool-down" — `DateTime.UtcNow` never does this in
  practice; if it did, every toast would be refused until the clock passed the last accepted stamp + cool-down
  (at most 60 s for that metric, 10 s for the rest), never permanently.

- `src/Stats.Core/Alerts/AlertEvent.cs` gains, next to `Message`:

  ```csharp
  /// <summary>Toast title, e.g. "CPU Package critical".</summary>
  public string NotificationTitle => $"{DisplayName} critical";

  /// <summary>Toast body, e.g. "96 °C for 10 s (crit ≥ 92)" — peak via ValueFormatter with the metric's own
  /// Format (a defaulted trailing `string Format = "F0"` record member, stamped from MetricDefinition.Format by
  /// AlertEngine.Tick and also used by AlertRowViewModel), so an F1 metric reads "15.7 GB" like its tile; the
  /// threshold is a bare "0.#" invariant number; "≤" for lower-is-worse rules.</summary>
  public string NotificationBody(int holdSeconds) =>
      $"{PeakText} for {holdSeconds} s (crit {Symbol} {ThresholdText})";
  ```

  `Message` is refactored onto the same three private helpers (`PeakText`, `Symbol`, `ThresholdText`) and its
  output is unchanged — the two existing `Message_*` tests in `AlertEngineTests` must still pass untouched.
  Worked examples: `("cpu.temp", "CPU Package", "°C", 96f, 92f, false)` → title `CPU Package critical`, body(10)
  `96 °C for 10 s (crit ≥ 92)`; `("fps.avg", "FPS", "fps", 20f, 30f, true)` → `FPS critical` /
  `20 fps for 10 s (crit ≤ 30)`; a `B/s` metric at `1_500_000f` with crit `1_000_000f` →
  `1.5 MB/s for 10 s (crit ≥ 1000000)`; threshold `92.5f` → `(crit ≥ 92.5)`.

- `AlertEngine`, `AlertSample`, `AlertLogViewModel`, `PeaksViewModel`: unchanged.

## App (`Stats.App`)

All of it in `src/Stats.App/App.xaml.cs` and `src/Stats.App/Views/DashboardWindow.xaml`; every `TaskbarIcon` call
stays on the Dispatcher thread inside `RunCoalescedRefresh` → `EvaluateAlerts` (rule 1: nothing here touches LHM or
a `SnapshotAvailable` subscriber). No fan code is touched (rule 6).

- **Field:** `private readonly AlertNotificationPolicy _alertNotifications = new();` next to `_alertEngine` (line 51).
  Add `using H.NotifyIcon.Core;` for `NotificationIcon`.
- **`SetupTray()`** (457–495): immediately after `_tray.TrayLeftMouseUp += (_, _) => ShowDashboard();` add
  `_tray.TrayBalloonTipClicked += (_, _) => ShowDashboard();` and
  `_tray.TrayBalloonTipShown += (_, _) => Trace.WriteLine("[Stats] alert notification shown");`.
  `ShowDashboard()` (588–594) already null-checks `_dashboard`, so a click after `ExitApp()` disposed the tray is
  harmless (and `TrayBalloonTipClicked` no longer fires after `Dispose()` anyway).
- **`EvaluateAlerts()`** (560–583): the loop becomes

  ```csharp
  var nowUtc = DateTime.UtcNow;
  foreach (var evt in _alertEngine.Tick(samples, nowUtc))
  {
      _alertLog.Add(evt);
      ShowAlertNotification(evt, nowUtc);
      if (_settings.AlertSoundEnabled)
          try { System.Media.SystemSounds.Exclamation.Play(); } catch { /* audio device unavailable */ }
  }
  ```

  and the old `_tray?.ShowNotification("Stats alert", evt.Message)` line is deleted. Update the method's XML doc
  ("tray balloon" → "Windows notification, see ShowAlertNotification").
- **New `ShowAlertNotification(AlertEvent evt, DateTime nowUtc)`**, placed right after `EvaluateAlerts`:

  ```csharp
  /// <summary>Owner decisions 2–3 of the toast-alerts spec: gate (setting, foreground) → cool-down → one
  /// H.NotifyIcon balloon (Windows renders it as a toast) with sound off — the Exclamation chime in EvaluateAlerts
  /// is the only alert sound. Every outcome is traced; a shell failure must never take the refresh down.</summary>
  private void ShowAlertNotification(AlertEvent evt, DateTime nowUtc)
  {
      if (_settings is null) return;
      bool dashboardIsForeground = _dashboard is { IsVisible: true, IsActive: true };
      if (!AlertNotificationPolicy.ShouldNotify(_settings.AlertNotificationsEnabled,
              _settings.AlertNotificationsSkipWhenForeground, dashboardIsForeground))
      {
          Trace.WriteLine($"[Stats] alert notification skipped ({(_settings.AlertNotificationsEnabled ? "dashboard in foreground" : "notifications off")}): {evt.NotificationTitle}");
          return;
      }
      if (!_alertNotifications.TryAccept(evt.MetricId, nowUtc))
      {
          Trace.WriteLine("[Stats] alert notification suppressed by cool-down: " + evt.NotificationTitle);
          return;
      }
      try
      {
          _tray?.ShowNotification(evt.NotificationTitle, evt.NotificationBody(_settings.AlertHoldSeconds),
              NotificationIcon.Warning, sound: false);
          Trace.WriteLine("[Stats] alert notification requested: " + evt.NotificationTitle);
      }
      catch (Exception ex) { Trace.WriteLine("[Stats] alert notification failed: " + ex.Message); }
  }
  ```

  `_tray` may be null or never created (`ForceCreate` threw at logon / explorer restart — already swallowed in
  `SetupTray`); the try/catch covers the resulting `ShowNotification` failure exactly as today's balloon call does.
- **`OnSettingsChanged` `case SettingsChange.Alerts`** (773–775) becomes

  ```csharp
  case SettingsChange.Alerts:
      if (_alertEngine is not null)
      {
          _alertEngine.HoldSeconds = _settings.AlertHoldSeconds;
          if (!_settings.AlertsEnabled) _alertEngine.Reset(DateTime.UtcNow); // ends in-flight episodes (finalizes "ongoing" rows) so a re-enable starts clean
      }
      if (!_settings.AlertsEnabled) _alertNotifications.Reset();
      break;
  ```

  `Raise(SettingsChange.Alerts)` also fires for hold/sound/notification toggles while alerts are off; `Reset` on an
  empty engine is a no-op, so that is harmless.
- **`DashboardWindow.xaml` Alerts `TabItem`** (661–679): append inside the `StackPanel`, after the
  `CheckBox Content="Play a sound with the notification"`:

  ```xml
  <TextBlock Text="Windows notifications" Style="{StaticResource SettingsHeader}" Margin="0,14,0,6"/>
  <CheckBox Content="Show Windows notifications" IsChecked="{Binding AlertNotificationsEnabled}"/>
  <CheckBox Content="Only when the dashboard is not in the foreground"
            IsChecked="{Binding AlertNotificationsSkipWhenForeground}"
            IsEnabled="{Binding AlertNotificationsEnabled}" Margin="20,4,0,0"/>
  <TextBlock Text="Click a notification to open the dashboard. Windows Focus Assist / Do not disturb may hold notifications back while a full-screen app is running."
             Foreground="{DynamicResource TextSecondary}" FontSize="11" TextWrapping="Wrap" Margin="0,6,0,0"/>
  ```

  The existing three rows are untouched. The caption uses the same `TextSecondary`/`FontSize="11"`/`TextWrapping`
  recipe as the System tab's captions (e.g. line 753). No new resource keys, converters, or code-behind.
- **`README.md`:** the Alerts paragraph (74–78) describes the Windows notification (title/body, click opens the
  dashboard, the two settings, the 10 s / 60 s cool-downs, Focus Assist caveat, chime as the only sound); the Tray
  paragraph (188–190) adds "alert notifications click through to the dashboard".

## Preview harness

The harness never constructs a `TaskbarIcon` (`docs/ui-polish/PREVIEW_HARNESS.md` lines 16 and 57,
`docs/ui-polish/T1-REPORT.md` line 40) and `CompositionIsolationTests` must keep proving no production service is
resolvable from `PreviewComposition` — this feature adds none (the policy is a plain Core class; the tray wiring
lives in `App.xaml.cs`, which the harness never loads).

- `tools/Stats.UiPreview/PreviewComposition.cs` `ApplySubstate`: new settings-level substate

  ```csharp
  // ---- toast alerts (docs/superpowers/specs/2026-09-11-toast-alerts-design.md) ----
  case "alerts-notify-off":
      c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
      c.SettingsVm.AlertNotificationsEnabled = false; // through the VM so the checkbox binding sees it; write-through records one "settings.save"
      break;
  ```

  (Setting `c.Settings.AlertNotificationsEnabled` directly would not update the already-constructed
  `SettingsViewModel`, unlike the `graphs-plain` pattern, whose settings are read at render time.)
- `tools/Stats.UiPreview/SubstateCatalog.cs`: `["settings"]` gains `"alerts-notify-off"`.
- `tools/Stats.UiPreview/captures/baseline.json`: four entries appended after the graph-effects block, all
  `"Method": "rtb"`, outputs under `artifacts/toast-alerts/` (git-ignored like `artifacts/graph-effects/`):
  1. `settings` / `category-alerts` / Dark Amber / 1180×720 → `v14-settings-alerts-default-dark-amber.png`
  2. `settings` / `category-alerts` / Light / 1180×720 → `v14-settings-alerts-default-light.png`
  3. `settings` / `alerts-notify-off+category-alerts` / Dark Amber / 1180×720 → `v14-settings-alerts-notify-off-dark-amber.png`
     (sub-option greyed)
  4. `alerts` / `ongoing` / Dark Amber / 640×480 → `v14-alerts-ongoing-unchanged.png` (documents that the Peaks
     Alerts tab is pixel-identical to `artifacts/ui-polish/before/v10-alerts-ongoing.png`)
- `docs/toast-alerts/EVIDENCE.md` in the `docs/graph-effects/EVIDENCE.md` format (candidate commit, harness changes,
  captures table with observations, "What the harness could not show", gate outcomes, final handoff).

**What the harness cannot show** (and therefore goes on the owner checklist): the toast itself, its icon and
app-name attribution, the click → `ShowDashboard()` activation, the absence of the shell sound, the real-time
cool-downs (unit-tested instead), `Window.IsActive` foreground detection, `--minimized` launches, Focus Assist /
Do not disturb, Action Center, Windows' per-app notification toggle, and the logon/explorer-restart race.
`--method rtb` also excludes popups and title bars, so only the Settings tab and the Peaks Alerts tab are
capturable.

## Non-goals

No toasts for warn; no Action Center persistence controls; no per-metric notification toggles; no custom sounds;
no notifications from the overlay; no coalesced multi-metric toast; no "Send test notification" button; no
`ClearNotifications()`; no `customIconHandle`; no change to `AlertEngine`, the log, the chime, or the tray icon
renderer; no new `SettingsChange` member; no new NuGet packages.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings (xUnit analyzers: constants in the
  `expected` slot).
- **Tests (Core), new `tests/Stats.Core.Tests/AlertNotificationPolicyTests.cs`:**
  `Cooldowns_AreTenAndSixtySeconds`; `ShouldNotify_NotificationsOff_ReturnsFalse_ForEveryOtherCombination`
  (theory over skip × foreground); `ShouldNotify_SkipWhenForeground_DashboardForeground_ReturnsFalse`;
  `ShouldNotify_SkipWhenForeground_DashboardNotForeground_ReturnsTrue`;
  `ShouldNotify_NotSkipping_DashboardForeground_ReturnsTrue`; `TryAccept_FirstNotification_Accepted`;
  `TryAccept_SecondMetricInsideGlobalCooldown_Refused_ThenAcceptedAtTenSeconds` (t0 cpu → true, t0+9 s gpu →
  false, t0+10 s gpu → true); `TryAccept_SameMetricInsidePerMetricCooldown_Refused_ThenAcceptedAtSixtySeconds`
  (t0 → true, t0+30 s → false, t0+59 s → false, t0+60 s → true); `TryAccept_RefusedCallDoesNotExtendEitherCooldown`
  (the refused t0+5 s gpu attempt leaves gpu accepted at t0+10 s; the refused t0+59 s cpu attempt leaves cpu
  accepted at t0+60 s); `TryAccept_GlobalCooldownRestartsOnEachAcceptedNotification` (t0 cpu, t0+10 gpu, t0+15
  mem → false, t0+20 mem → true); `Reset_ForgetsAllStamps` (t0 cpu, `Reset()`, t0+1 s cpu → true).
- **Tests (Core), new `tests/Stats.Core.Tests/AlertEventTests.cs`:** `NotificationTitle_IsDisplayNameFollowedByCritical`;
  `NotificationBody_HigherIsWorse_FormatsPeakHoldAndThreshold` (`"96 °C for 10 s (crit ≥ 92)"`);
  `NotificationBody_LowerIsWorse_UsesLessOrEqual` (`"20 fps for 10 s (crit ≤ 30)"`);
  `NotificationBody_BytesPerSecond_ScalesLikeTiles` (`"1.5 MB/s for 10 s (crit ≥ 1000000)"`);
  `NotificationBody_FractionalThreshold_KeepsOneDecimal` (`"(crit ≥ 92.5)"`);
  `NotificationBody_UsesTheHoldSecondsArgument` (45 → `"for 45 s"`). The existing `Message_*` tests in
  `AlertEngineTests` stay and still pass.
- **Tests (Core), `SettingsServiceTests`:** `Load_MissingFile_AlertNotificationsDefaultOnAndSkipWhenForegroundOn`;
  `SaveThenLoad_AlertNotificationFields_RoundTrip` (both `false`);
  `Load_PreToastAlertsFile_DefaultsBothNotificationFieldsToTrue` (a JSON file holding only `AlertsEnabled`,
  `AlertHoldSeconds`, `AlertSoundEnabled` loads with both new bools `true`).
- **Tests (Core), `SettingsViewModelTests`:** `Ctor_LoadsAlertNotificationValuesFromSettings`;
  `AlertNotificationsEnabled_WritesThroughAndRaisesAlerts` (changes == `[Alerts]`, exactly one save);
  `AlertNotificationsSkipWhenForeground_WritesThroughAndRaisesAlerts`.
- **Tests (harness), new `tests/Stats.UiPreview.Tests/ToastAlertsSubstateTests.cs`:**
  `AlertsNotifyOffSubstate_TurnsNotificationsOff_KeepsSkipWhenForeground_AndRecordsOneSave`;
  `NoSubstate_DefaultsBothNotificationSettingsOn`; `SubstateCatalog_AcceptsAlertsNotifyOffForSettings_RejectsItForAlertsView`.
  `CompositionIsolationTests` unchanged and green.
- **Captures:** the four `baseline.json` entries above, produced with `--method rtb`, inspected and tabulated in
  `docs/toast-alerts/EVIDENCE.md`; entry 4 byte-compared (or pixel-diffed) against the ui-polish baseline.
- **Owner checklist** (needs the running app; none of it is harness-provable):
  1. Dashboard closed to the tray, a live metric forced Crit (lower its threshold in Settings → Thresholds): after
     the hold time a toast titled `<metric> critical` with the `… for 10 s (crit ≥ …)` body appears under the
     "Stats" app name; the Peaks Alerts tab shows the same row.
  2. Clicking the toast shows and activates the dashboard (not just a taskbar flash) — also from a `--minimized`
     launch where the dashboard has never been shown.
  3. Dashboard visible and active: no toast (default), the row and chime still happen; with "Only when the
     dashboard is not in the foreground" unchecked, the toast appears even then.
  4. "Show Windows notifications" unchecked: no toast, log/chime unchanged, the sub-option is greyed.
  5. Sound: chime off → the toast is silent (no Windows notification sound); chime on → exactly one sound.
  6. Two metrics going Crit in the same second → one toast (the second is traced as "suppressed by cool-down");
     the same metric recovering and re-entering Crit within 60 s → no second toast; the rolling trace log shows
     the requested/shown/suppressed lines.
  7. Behind a borderless-windowed game with Focus Assist off: toast appears; with Focus Assist's "when I'm playing a
     game" / full-screen rule on: the toast lands silently in Action Center (Windows behaviour, documented in README).
  8. Action Center lists the toast under "Stats"; Windows Settings → Notifications lists Stats.
  9. A v1.9.x `settings.json` upgrades with both new checkboxes on and no load error.
  10. Unchecking "Notify on sustained critical readings" during an ongoing episode completes its row; re-checking
      does not toast instantly (the hold restarts).
  11. Explorer restart / logon race: no crash when the tray is not created; the failure is traced.
