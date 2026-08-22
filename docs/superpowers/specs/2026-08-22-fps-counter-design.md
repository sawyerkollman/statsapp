# Stats FPS counter — design

**Date:** 2026-08-22 · **Branch:** feature/v1.1-ui · **Status:** approved in conversation, pending spec review

## Goal

Show the frame rate of the game I'm playing as ordinary Stats metrics — on the dashboard and in the
click-through overlay — with no injection into the game and no extra UI to manage. Pick the
metrics like any other (metric picker), threshold them like any other, and pay nothing when none
are selected.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Data source | Intel **PresentMon** console app (MIT), ETW-based, passive; bundled in the installer |
| Integration | PresentMon CLI child process, CSV on stdout, merged into the existing reader pipeline (approach A) |
| Target process | **Foreground window's process**, automatically, every poll tick |
| Metrics | `fps.avg` (FPS), `fps.low1` (1% low FPS), `fps.frametime` (ms) — individually selectable |
| Lifecycle | Tracing runs **only while at least one `fps.*` metric is selected** (dashboard or overlay) |
| Defaults | None selected by default; no default thresholds (see Thresholds) |
| PresentMon version | 2.5.1 console exe (`PresentMon-2.5.1-x64.exe`, 956,768 bytes), SHA-256 `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191` |
| Rejected | PresentMon service/API2 (service install + undocumented ABI); raw ETW via TraceEvent (weeks); DLL-injection overlays (anti-cheat) |

## Architecture

```
SensorPoller ──Read()──▶ CompositeSensorReader ──▶ LhmSensorReader | PerfCounterSensorReader  (unchanged)
                                  │
                                  └──▶ FrameRateReader ──▶ FrameStatsAggregator ◀── PresentMonProcess (stdout CSV)
                                              ▲
                                   ForegroundProcess.CurrentPid()
```

All new code lives in `Stats.Core/Frames/`. `App.xaml.cs` composes
`new CompositeSensorReader(baseReader, frameReader)` and hands it to `SensorPoller` exactly as today;
`MetricStore`, tiles, overlay, history, thresholds and settings are untouched except where noted.

### Components

**`PresentMonLocator`** (static) — finds the binary: `{AppContext.BaseDirectory}\PresentMon.exe`, else
walks up from the base directory looking for `installer\vendor\PresentMon-*.exe` (run-from-source),
else `null`. Name-agnostic on purpose: build.ps1 copies the pinned exe into the publish dir as
`PresentMon.exe`.

**`PresentMonProcess`** — owns one child process. `Start()` launches

```
PresentMon.exe --output_stdout --no_console_stats --stop_existing_session --session_name StatsFps
               --no_track_gpu --no_track_input --exclude Stats.App.exe
```

with redirected stdout/stderr, `UseShellExecute=false`, `CreateNoWindow=true`. A background thread reads
stdout line by line and raises `LineReceived(string)`. `Stop()` kills the process tree (`Kill(true)`)
and waits ≤ 2 s. If the child exits on its own, `Exited(int exitCode, string stderrTail)` fires.
Restart policy (owned by `FrameRateReader`): backoff 1 s → 5 s → 30 s, then stay down until the next
lifecycle toggle. Stderr is captured (last ~20 lines) for the log.

Rationale for the flags: capturing *all* processes and filtering in-app means alt-tabbing between games
never restarts the ETW session; `--no_track_gpu/--no_track_input` drop work we don't need; a unique
session name plus `--stop_existing_session` recovers from a crashed previous Stats; `--exclude` keeps the
overlay from ever measuring itself.

**`PresentMonCsvParser`** — header-driven. The first line is the header; it locates `ProcessID` and the
frame-interval column by name, accepting either `MsBetweenPresents` (1.x naming) or `FrameTime` (2.x
naming), and a `CPUStartTime` column as a fallback from which per-process deltas are derived if neither
interval column exists. Data lines yield `FrameSample(int Pid, double FrameTimeMs)`; malformed lines
(wrong field count, unparsable numbers, negative/zero/NaN intervals) are skipped and counted. Throws
`PresentMonFormatException` only if the header lacks `ProcessID` or any usable timing column — that
is surfaced as a log line and the reader reports `null` for all three metrics.

**`FrameStatsAggregator`** — thread-safe store of recent frames per PID. `Add(FrameSample, DateTime
nowUtc)` appends `(timestamp, frameTimeMs)` to a ring buffer capped at **5000 frames** per PID (enough to
cover the longest poll window, 5 s, even at 1000 fps); PIDs with no frame for **10 s** are pruned on each
`Snapshot`.
`Snapshot(int pid, DateTime nowUtc, TimeSpan window)` returns:

- `Fps` — frames with timestamp in `(now − window, now]` ÷ `window.TotalSeconds`; `null` if < **10**
  frames in the window (not rendering / just started).
- `FrameTimeMs` — mean frame time of the frames in the window; `null` under the same rule.
- `OnePercentLowFps` — 1000 ÷ (99th-percentile frame time over the newest **1000** frames in the ring
  buffer); `null` until the buffer holds ≥ **100** frames. Percentile: sort ascending, take element at
  `ceil(0.99·n) − 1`.

`window` is the poll interval (from settings). Timestamps are assigned on receipt; PresentMon's own
CPUStartTime is not used for windowing so clock domains never matter.

**`ForegroundProcess`** (static, P/Invoke `GetForegroundWindow` + `GetWindowThreadProcessId`) — returns
the PID of the foreground window, or `null`. Stats' own PID is mapped to `null`.

**`FrameRateReader : ISensorReader`** — `Name = "PresentMon"`, `IsDegraded = false`.
- `Discover()`: returns the three definitions below if `PresentMonLocator` finds a binary, else an empty
  list. Never starts the process.
- `Read()`: if tracing is active, `pid = ForegroundProcess.CurrentPid()`; values are the aggregator
  snapshot for that PID (all `null` when `pid` is null or has no frames). If tracing is inactive, all
  three are `null` (tiles show their existing no-data state).
- `SetActive(bool)`: starts/stops `PresentMonProcess`; idempotent; called from the UI thread on startup
  and on every settings change. Active ⇔ any selected metric id (dashboard ∪ overlay) starts with `fps.`.
- `Dispose()`: stops the child.

**`CompositeSensorReader : ISensorReader`** — `Discover()` concatenates; `Read()` merges dictionaries
(later readers win on id collision — none expected); `IsDegraded` = base reader's; `Name` = base
reader's; `Dispose()` disposes all. One reader throwing in `Read()` does not lose the others' values
(try/catch per reader; that reader's ids are simply absent that tick).

### Metric definitions

New enum member `MetricGroup.Game` (append at the end so existing serialized ordinals are unaffected —
`MetricGroup` is serialized inside `ThresholdRule`; verify whether System.Text.Json writes it as number
or string and keep appends safe either way).

| Id | DisplayName | Group | HardwareName | Unit | Format |
|---|---|---|---|---|---|
| `fps.avg` | FPS | Game | Foreground app | fps | F0 |
| `fps.low1` | 1% Low FPS | Game | Foreground app | fps | F0 |
| `fps.frametime` | Frame Time | Game | Foreground app | ms | F1 |

Required touch points for the new group (found by grep): `DashboardViewModel` group-order list gets
`MetricGroup.Game` last; `DefaultSelector` unchanged (nothing selected by default);
`MetricTileViewModel` "load percent" styling is unit/group-gated and needs no change; `SettingsViewModel`
threshold rows are hand-listed (CPU/GPU temp, load) and gain nothing. If any `switch` over `MetricGroup`
lacks a default arm, add the `Game` arm.

### Thresholds

`ThresholdEvaluator` only knows "higher is worse" (`v ≥ Warn`). That is correct for `fps.frametime`
(a rule `Group=Game, Unit=ms` works today) but wrong for FPS. **This design adds no default rules and
no inverted-direction support**; FPS tiles stay `Normal` unless the user adds an override (which would
fire backwards, so the Settings UI should not advertise it). A follow-up can add a `LowerIsWorse`
flag to `ThresholdRule`. Recorded as out of scope, not forgotten.

### Settings

No new settings. The lifecycle derives from existing `DashboardMetrics` / `OverlayMetrics`. Poll
interval is reused as the FPS window.

## Installer / build

- `build.ps1`: second pinned download, same pattern as PawnIO —
  `https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe` →
  `installer/vendor/`, verify the SHA-256 above, abort on mismatch, skip if cached-and-verified. After
  publish, copy it to `installer/publish/PresentMon.exe` so the existing `[Files]` glob ships it to
  `{app}`. (Step order: publish → PawnIO → PresentMon copy → ISCC.)
- `Stats.iss`: no change needed (publish glob covers it). `--exclude Stats.App.exe` matches `AppExe`.
- `THIRD-PARTY.txt`: add the PresentMon MIT notice and source URL (`https://github.com/GameTechDev/PresentMon`), noting it is run as a helper process only while FPS metrics are selected.
- `.gitignore` already ignores `installer/vendor/` and `installer/publish/`.

## Error handling

| Condition | Behaviour |
|---|---|
| `PresentMon.exe` missing | FPS group not discovered; nothing to select; one log line |
| ETW access denied (exit 6, stderr "failed to start trace session") | Do **not** retry-loop: log stderr tail once, mark reader `Unavailable`, all `fps.*` → null until next toggle |
| Child crashes mid-run | Restart with 1/5/30 s backoff; metrics null while down |
| CSV header unrecognised | `PresentMonFormatException` → log, stop child, metrics null |
| Foreground app not presenting (desktop, browser idle) | `< 10` frames in window → null (tile shows no data) |
| Stats exiting | `Dispose` kills child; `--stop_existing_session` covers a crash that leaves a session behind |

**Known constraint (discovered during the spike):** processes with an MSIX *package identity* are denied
ETW trace sessions even when elevated. The Store build of PowerShell has package identity and children
inherit it — so Stats launched from such a terminal (including this Claude Code shell, and a `dotnet
run` from it) **cannot** trace. Launched from the Start menu, the desktop shortcut, the autostart task,
or a non-Store terminal it works. The access-denied row above is exactly this case; its log line names
the cause. Development testing must launch Stats from Explorer/Start menu or Windows Terminal's
non-Store PowerShell / cmd.

## Testing

`tests/Stats.Core.Tests` (xunit), pure logic only:

- `PresentMonCsvParserTests` — header with `MsBetweenPresents`; header with `FrameTime`; `CPUStartTime`
  fallback (deltas per PID, first frame per PID yields nothing); malformed lines skipped and counted;
  missing `ProcessID` → `PresentMonFormatException`. Fixture: a real ~200-line PresentMon 2.5.1 capture
  checked in under `tests/Stats.Core.Tests/Fixtures/presentmon-2.5.1-sample.csv` (captured from a
  non-packaged elevated terminal during implementation; the plan's first task produces it).
- `FrameStatsAggregatorTests` — FPS/frametime over window; `null` under 10 frames; 1% low null under
  100 frames; percentile index on exact boundaries (n=100, n=1000); ring-buffer cap at 1000; stale PID
  pruning at 10 s; per-PID isolation; thread-safety smoke (concurrent Add + Snapshot, no exception).
- `CompositeSensorReaderTests` — Discover concatenation; Read merge; one reader throwing doesn't drop
  the other's values; Dispose fans out.
- `FrameRateReaderTests` — with a fake process/aggregator: `SetActive` idempotence; `Read()` null when
  inactive; `Discover()` empty when locator returns null; activation predicate from selections
  (`fps.` prefix, dashboard ∪ overlay).

No unit tests for `PresentMonProcess` or `ForegroundProcess` (thin OS shells). Manual verification:
select FPS on overlay from the Start-menu-launched app, run a game, confirm numbers roughly match the
game's own counter; alt-tab to desktop → tiles blank; deselect → `PresentMon.exe` gone from Task Manager.

## Out of scope

Inverted thresholds ("lower is worse"), per-game history/logging, GPU-time/latency metrics PresentMon
could provide, user-selectable target process, showing which app is being measured, ARM64.
