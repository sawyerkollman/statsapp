# Toast alerts — whole-branch review

**Verdict: needs a small fix wave — no blockers, 2 should-fixes.** Everything the spec's owner decisions 1–3 and
assumed decisions 1–12 pin down is implemented as written; CLAUDE.md rules 1, 2, 3, 4 and 6 all hold. Both
should-fixes are text/robustness issues in the toast body and title, not design errors.

Gates re-run in this worktree (clean tree, `feature/v1.10..HEAD` = 3 commits):
`dotnet build --nologo` → **0 warnings, 0 errors**; `dotnet test --nologo` → **824 + 200 = 1024 passed, 0 failed,
0 warnings**. All four captures in `artifacts/toast-alerts/` opened and match `EVIDENCE.md`'s observations;
`v14-alerts-ongoing-unchanged.png` verified byte-identical to `artifacts/ui-polish/before/v10-alerts-ongoing.png`
(sha256 `ba604928…f1988`, both 11,539 B), so the Peaks Alerts tab really is untouched.

## What was checked and is correct

- **Threading (rule 1).** `SetupTray()` is called at `App.xaml.cs:230`, 40 lines before `_poller.Start()` (270), so
  `_tray` exists before any snapshot can arrive. Every `TaskbarIcon` touch is on the Dispatcher:
  `ShowAlertNotification` ← `EvaluateAlerts` ← `RunCoalescedRefresh` (the `Dispatcher.BeginInvoke` target at 253);
  `TrayBalloonTipClicked`/`Shown` are routed events raised on the UI thread. No LHM, poller or fan code touched.
- **`ShowNotification` call (`App.xaml.cs:609-610`)** matches the pinned 2.3.2 signature exactly —
  `ShowNotification(String, String, NotificationIcon, IntPtr?, Boolean, Boolean, Boolean, Boolean, TimeSpan?)`
  (verified in `~/.nuget/packages/h.notifyicon.wpf/2.3.2/lib/net8.0-windows7.0/H.NotifyIcon.Wpf.xml:434`), so the
  positional `NotificationIcon.Warning` lands on `icon` and `sound: false` on `sound` ("If false do not play the
  associated sound"). `largeIcon`/`respectQuietTime`/`realtime`/`timeout` keep their defaults; no
  `customIconHandle`, no `ClearNotifications()`. `Stats.App.csproj:8` still pins 2.3.2 (rule 4).
- **Balloon click after exit.** `ExitApp()` disposes `_tray` (`959-968`) on the UI thread and calls `Shutdown()`
  in the same synchronous block, so no queued `NIN_BALLOONUSERCLICK` can be dispatched in between; and
  `ShowDashboard()` (`620-626`) null-guards `_dashboard` anyway. A toast left in Action Center after exit is inert,
  which is the documented Windows behaviour.
- **`SettingsChange.Alerts` reset branch (`App.xaml.cs:804-811`).** It cannot lose an in-flight episode: both
  `Reset` calls are behind `!_settings.AlertsEnabled`, and while `AlertsEnabled` is false `EvaluateAlerts` is never
  called (`553`), so no episode can accumulate. The switch-off itself runs `Reset` once (`_settings.AlertsEnabled`
  is already `false` when `Raise` fires, since `SettingsViewModel` writes through before raising); every later
  Alerts-tab toggle while off hits an empty `_episodes` and is a genuine no-op. Owner checklist item 10 is
  satisfied by construction — `AlertEngine.Reset` → `EndEpisode` → `EpisodeEnded` → `_alertLog.Complete`
  (`App.xaml.cs:155-158`) finalizes the "ongoing" row.
- **Policy semantics (`AlertNotificationPolicy.cs:22-30`).** Boundaries are inclusive (`< Cooldown` refuses, so
  exactly 10 s / 60 s is accepted); a refusal returns before either assignment, so it stamps nothing and extends
  nothing; a backwards clock yields a negative `TimeSpan` which is `< Cooldown` → refused, as specified. Calls come
  only from `EvaluateAlerts` and `OnSettingsChanged`, both UI-thread (`_settingsVm`'s `_save` is `SaveSettings`,
  not the marshalling `RequestSaveSettings`, and nothing mutates the VM off-thread), so the unsynchronized
  `Dictionary` is safe today.
- **`AlertEvent.Message` is byte-identical.** `Message` (`AlertEvent.cs:21`) composes the same three fragments in
  the same order as the pre-refactor body; `AlertEngineTests.Message_NormalDirection_…` / `Message_InvertedDirection_…`
  still assert the exact strings and still pass untouched.
- **Settings compat (rule 3).** Both new bools default `true` in `AppSettings.cs:133-140`, need no `Normalize`
  change, and `SettingsServiceTests.Load_PreToastAlertsFile_DefaultsBothNotificationFieldsToTrue` proves a v1.9.x
  file loads with both on. XAML binding names match the VM properties one-for-one, proven live by the notify-off
  capture (unchecked master + greyed sub-option).
- **Harness isolation.** The new substate only reaches `c.SettingsVm`, records exactly one `settings.save`, and adds
  no production service; `CompositionIsolationTests` is unchanged and still meaningful (it sweeps the fan substates
  and the allowed-verb vocabulary, neither of which this branch widens). README's Alerts and Tray paragraphs match
  the shipped behaviour; no stale "tray balloon" wording survives.

## Blockers

None.

## Should-fix

**S1 — `AlertEvent.cs:32-39`: the toast body rounds the peak to whole numbers for F1 metrics, so the body can
contradict itself.** `PeakText` rebuilds a synthetic `MetricDefinition(MetricId, DisplayName, default, "", Unit)`
which drops the real definition's `Format` and falls back to `"F0"`, while `ThresholdText` (`:43`) prints `"0.#"`.
Failure scenario: a *Memory Used* alert (`PerfCounterSensorReader.cs:46`, unit `GB`, format `F1`) peaking at
15.7 GB against a 15.5 GB crit renders as `16 GB for 10 s (crit ≥ 15.5)` — a peak the tile shows as 15.7, and a
line that reads as if 16 ≥ 15.5 were news; *Frame Time* (`FrameMetrics.cs:18`, `ms`, `F1`) has the same problem.
The old `Message`/Peaks row shared the F0 rounding but also F0'd the threshold, so this within-line mismatch is new
to the toast. Fix: append `string Format = "F0"` to the `AlertEvent` record (defaulted, so every existing call site
and test is unaffected), stamp `sample.Definition.Format` onto `Episode` in `AlertEngine.Tick`, and use it in
`PeakText` (and, for consistency, `AlertRowViewModel`'s identical synthetic def at `AlertLogViewModel.cs:19`).
This touches `AlertEngine`, which the spec lists under Non-goals, so it needs an owner call; the doc-only fallback
is to correct the spec/EVIDENCE claim that `"96.4"` reads `"96"` "everywhere" and add owner-checklist item 12 below.

**S2 — `App.xaml.cs:609`: a long metric display name can silently swallow the toast.** `NotificationTitle` is
`"<DisplayName> critical"` and goes straight into `NOTIFYICONDATAW.szInfoTitle`, which is 64 chars *including* the
terminator (body `szInfo` is 256). Failure scenario: a GPU sensor whose mapped name is
`NVIDIA GeForce RTX 4090 · GPU Memory Junction Temperature` (56 chars) produces a 65-char title; depending on how
H.NotifyIcon 2.3.2 copies into the fixed buffer it either truncates or throws, and the throw path is swallowed by
the `catch` at `:613` — the branch's headline feature silently does nothing for exactly the users with verbose
hardware names. The project already assumes names get this long (the `peaks`/`long-names` fixture uses a 64-char
name). Fix: clamp before the call, e.g. `static string Clamp(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";`
then `Clamp(evt.NotificationTitle, 63)` and `Clamp(body, 255)`.

## Nits

**N1 — `App.xaml.cs:609-611`: the `_tray?.` null-conditional can trace a lie.** If `_tray` were null the call
no-ops but still traces `"alert notification requested"`, and the cool-down has already been consumed at `:602`.
Unreachable in practice (see the threading note above), but it also means the `ForceCreate`-failed case — owner
checklist item 11, "the failure is traced" — depends on `ShowNotification` throwing rather than no-opping, which
cannot be verified from this shell. Fix: `if (_tray is null) { Trace.WriteLine("[Stats] alert notification failed: no tray icon: " + evt.NotificationTitle); return; }`
placed *before* `TryAccept`, so a dead tray neither lies nor burns a cool-down.

**N2 — `AlertNotificationPolicy.cs:3-6`: the class doc calls itself "Pure" and omits the threading contract.**
It is stateful, and unlike `AlertEngine` ("No threads: call `Tick` synchronously from the UI-thread refresh") it
says nothing, while now having two call sites. Fix: replace "Pure and" with "Stateful but clock-injected", and add
"UI-thread only, like `AlertEngine`" so a future poll-thread caller does not race the dictionary.

**N3 — spec `…-toast-alerts-design.md` "Core" bullet: the backwards-clock rationale is wrong.** "The worst case is
one dropped toast" understates it — because a refusal never re-stamps, a clock stepping back N seconds blocks
*every* toast for N seconds and that metric for up to N + 60 s. The code matches the specified contract; only the
sentence needs correcting.

**N4 — `docs/toast-alerts/EVIDENCE.md` "Candidate": stale.** It names HEAD `572a7f7` "+ this task's uncommitted
harness/docs changes"; those are now committed as `7fa88f2`. Also the plan promised Task 3 as two commits
(`feat(tools): …` then `docs: …`) and the branch has one combined `feat(tools):` commit — worth a line in the fix
wave's EVIDENCE update.

**N5 — `DashboardWindow.xaml:676-682`: the "Windows notifications" block stays enabled when "Notify on sustained
critical readings" is off**, so both checkboxes look live while nothing can ever raise. This copies the existing
house pattern (the hold slider and the chime checkbox are not gated on `AlertsEnabled` either), so it is consistent
rather than wrong — but if it is ever fixed it should be fixed for all five rows at once, not just the new two.

**N6 — `AlertEvent.ToString()` changed.** A record's synthesized `PrintMembers` includes every public readable
property, so the new `NotificationTitle` now appears in `ToString()` output (`NotificationBody` does not — it is a
method). Nothing in `src/`, `tests/` or `tools/` consumes it; noted only so it is not a surprise in a future
assertion message.

## Owner checklist additions

Beyond the spec's items 1–11:

12. **F1-format metrics.** Force *Memory Used* (GB) or *Frame Time* (ms) to Crit and read the toast body: confirm
    whether the rounded peak against an unrounded threshold (S1) is acceptable, or whether the `Format` fix lands.
13. **Long metric names.** Force Crit on the metric with the longest display name the machine produces (a full GPU
    sensor name or a `<NIC> · Download` row) and confirm a toast actually appears (S2).
14. **Dead-tray trace.** After an explorer restart that kills the tray icon, force an alert and confirm the rolling
    trace shows a `failed`/`no tray icon` line rather than `requested` (N1) — item 11 as written cannot tell them
    apart.
15. **Master switch interaction.** With "Notify on sustained critical readings" unchecked, confirm the two Windows-
    notification checkboxes remaining clickable-but-inert is acceptable (N5).


## Fix wave

Applied by the controller on top of `7fa88f2`: **S1** fixed (`AlertEvent.Format`, stamped by `AlertEngine`, used
by the toast body, `Message`, and `AlertRowViewModel`; two new tests), **S2** fixed (`ClampForShell` 63/255),
**N1** fixed (null-tray trace before `TryAccept`), **N2/N3** fixed (docs), **N4** fixed (EVIDENCE candidate +
this section), **N5/N6** skipped (consistency with existing rows; unconsumed `ToString`). Gates: build 0 warnings,
826 + 200 tests green.
