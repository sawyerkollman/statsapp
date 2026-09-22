# Monitoring workflows

User authorization: implement all six suggestions from the 2026-09-15 conversation.
Base: `c42264b`; integration branch: `feature/monitoring-workflows`.

## Contract

1. Named layouts follow `2026-09-11-layout-profiles-design.md`, including manual switching,
   modified/save/revert state and the existing game-mode signal. Preserve shared metric names,
   limits, thresholds, fan settings, and old settings compatibility.
2. Session recording is explicit Start/Stop. Capture the selected dashboard/overlay metrics with
   UTC timestamps and missing values. Save locally, reopen a completed recording, show per-metric
   min/average/max and export CSV. Poll samples are not individual frames; never derive a purported
   session 1% low from averages. Recording failure is visible and cannot affect sensor/fan control.
3. A comparison window shows up to four selected metrics in aligned charts with a common timeline,
   independent units/scales and a shared cursor. Freeze affects this view only; live monitoring,
   recording and safety continue. It can display live data and recorded/alert context.
4. Top apps follows `2026-09-11-process-monitor-design.md`: optional, grouped CPU/GPU/memory rows,
   bounded refresh cost, graceful unavailable states, no process termination or reprioritization.
5. View > Lock layout persists (default off) and blocks user drag/reorder/nudge/resize/reset actions,
   including keyboard and context menu paths. Explicit profile switches remain usable. View > Undo
   layout edit restores the last move/reorder/resize/reset only, once; it does not undo sensor,
   threshold, fan or profile changes. Clear obsolete undo state on layout/profile/selection changes.
6. Alerts retain up to 200 recent entries across restarts in separate local storage, keeping the
   existing hold/re-arm rules. Capture bounded pre-event history when raised and update context
   while ongoing, with a clear end/interrupted state. Opening an alert shows its captured graph,
   timestamp, duration and peak even if that sensor is no longer discovered. Clear removes stored
   history too. Missing/corrupt history fails gracefully without resetting application settings.

## Integration and safety

Production services are constructed in `App.xaml.cs`, never in view-model constructors. Preview
composition uses deterministic samples and explicit temporary paths; it cannot enumerate real
processes, start ETW/hardware, or access production history/settings. Reuse the native WPF controls,
theme dictionaries and existing chart renderer. No new package dependencies.

Keep hardware reader ownership, shutdown ordering, fan defaults/recovery and theme propagation.
At most two source editors, disjoint ownership. Parent owns project files and composition root.
No release or merge is authorized. Untracked owner files are preserved.

## Acceptance

- Zero-warning build and full existing/new behavior tests at frozen checkpoints.
- Focused checks for profile compatibility, lock/undo routes, session round-trip/CSV/failures,
  timestamp alignment/freeze, process lifecycle/unavailability, alert persistence/context.
- Actual simulated WPF captures for new views in dark/light and narrow sizes, inspected by an
  independent reviewer/validator. Record native hardware, tray, popup and DPI limits separately.

## Shared capture contract (frozen before packets C/D)

Use `Stats.Core.Recording.MetricSeries(MetricDefinition Definition, DateTime[] TimesUtc,
float?[] Values)` as the interchange between live comparison, recorded sessions and alert context.
Arrays are owned snapshots, equally sized, ordered oldest-first; missing/non-finite values become
null. `MetricHistory.CopySeries(MetricDefinition)` copies timestamp/value rings together; Add,
Resize and ResetSession keep them aligned. Existing CopyTo/ToArray and chart behavior remain.

Record at the UI coalesced-refresh boundary immediately after Store.Apply, using that snapshot's
UTC timestamp (not a second clock). Freeze selected metric identity/name/unit/order at Start.
Use a local `.stats-session.jsonl` file: versioned header, sample records, explicit end record.
A bounded channel sends copied DTOs to a background writer; queue-full/failure ends recording
visibly, never silently drops data. Stop drains writes before opening/exporting the file. A
partial file after interruption can be read as incomplete; malformed interior rows fail clearly.
Loading computes summaries by streaming all samples and retains the newest 3600 samples per
metric for chart display; label this limit. CSV export streams the whole recording with UTC,
invariant numeric values, quoted headers and empty missing values. Do not invent per-frame data.

`SessionRecorder(string directory)` exposes `Start(definitions, startedUtc)`, `Record(snapshot)`,
`StopAsync()`, `IsRecording`, `FilePath`, `Error`. `SessionFile.Load(path)` returns `SessionData`
(StartedUtc, EndedUtc, IsComplete, SampleCount, Series, Summaries); `SessionFile.ExportCsv(source,
destination)` exports all rows. Constructors never start work; the supplied directory is explicit.
Use the smallest implementation that handles validation, I/O errors, interruption and shutdown.

Alert records have stable identity, raised UTC, original event data, duration, ongoing/completed/
interrupted state, and optional MetricSeries context. Preserve the existing public Add/Complete
methods for callers/tests, extending overloads for timestamps/context. Loaded ongoing rows become
interrupted. Capture the most recent 120 pre-event samples and retain up to 120 later samples;
no duplicate raised sample. Peak updates respect LowerIsWorse. Expose Changed and
OpenContextRequested events; persistence is explicit through an injected store in App, not the
VM constructor. Save on raise/complete/clear and periodically while ongoing; contain I/O errors
and show a history status message. Never alter the AlertEngine hold/re-arm rules.

Comparison uses optional timestamp/shared-start/shared-end/cursor properties on HistoryChart,
retaining its existing sample-index path when not supplied. Up to four rows share horizontal
bounds and cursor time, with independently labeled units/scales. Freeze keeps the captured arrays
and axis fixed until resumed. Recorded/alert contexts are read-only and explicitly dated; live
capture uses one common latest timestamp. Empty/missing data stays visibly missing.
