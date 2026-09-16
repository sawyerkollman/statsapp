# Stats

Unified PC monitoring dashboard for Windows. One dark-themed window for what
Ryzen Master, Task Manager, Core Temp, and Afterburner each show a slice of:
CPU per-core clocks/temps/loads, package power, PPT, voltages; GPU clocks,
temps, fan, power, VRAM; RAM; per-disk activity; per-adapter network throughput.

## Install

Grab `Stats-Setup-<version>.exe` from the
[latest release](https://github.com/sawyerkollman/statsapp/releases/latest) and run it.
It is not code-signed, so SmartScreen will warn: **More info → Run anyway**. The installer
needs administrator rights and 64-bit Windows 10 1809 or later, puts Stats in `C:\Program Files\Stats` and the Start menu, and
installs the **PawnIO** kernel driver (https://pawnio.eu) if it is not already present —
LibreHardwareMonitor reads CPU temperature/clock/power through PawnIO only, same as Core Temp
or Ryzen Master. Without it the app shows a degraded-mode banner (loads/usage only).
Optional checkbox: start Stats at sign-in (a Scheduled Task, so no UAC prompt each login);
sign-in launches Stats minimized to the tray instead of opening the dashboard.
Settings → Startup can create or remove the same task later and always reflects its actual state.
Uninstall from Settings → Apps; PawnIO is left installed because other tools may use it.
- **FPS counter** — select FPS / 1% Low FPS / Frame Time from the *Game* group. Stats runs the bundled
  Intel PresentMon helper only while one of them is selected or Game mode is on (*Fans* window). Launch
  Stats from the Start menu or desktop shortcut: processes started from the Microsoft Store build of
  PowerShell/Terminal inherit an MSIX identity that Windows blocks from ETW tracing, so FPS
  stays blank there.
- **Game tiles** — two extra tile kinds for the *Game* group: **FPS summary** on the FPS tile shows the
  average large, 1% low and frame time small, and a compact bar for 1% low ÷ average (e.g. "64% of avg"),
  coloured by the 1% low's own severity so it turns amber/red on stutter even while the average looks
  fine; a missing sibling reads "—". **Histogram** (offered on every tile; pick it from the tile menu — nothing is re-templated automatically) bins the
  metric's history window into 12 bars with a p99 marker (p1 for lower-is-worse metrics like FPS) — the
  bars are per-poll averages, not individual frames, so they show the shape of a session, not a per-frame
  trace. A settings file with either kind opened by an older (1.9.x) Stats build resets all settings to
  defaults on load; back up `%AppData%\Stats\settings.json` before downgrading.
- **Fan control** — *Fans* window (toolbar / tray): the header states whether fan control is off
  or on. Every LibreHardwareMonitor-controllable fan (motherboard headers, GPU fans, supported USB
  coolers) has a segmented **Auto / Manual / Curve** control, driven by one or more temperatures
  Stats monitors when on Curve (the curve follows the hottest selected source); the controls wrap
  onto their own line on narrow windows. Off until you enable it;
  2 °C hysteresis, max 10 %/s change, falls back to device control if the source temperature
  disappears for 10 s, pumps never below 50 %; fans return to device control when you exit Stats.
  Close MSI Center / Fan Control / Afterburner fan curves first. On a fatal error Stats makes a
  best-effort attempt to return every fan to device control before terminating; the next-launch
  recovery marker remains the fallback if that cleanup cannot complete.
  Stats now also reads the motherboard Super-I/O chip and USB fan/AIO controllers through
  LibreHardwareMonitor (new *Motherboard* and *Cooler* metric groups); if another vendor tool owns
  those devices you may see contention — close it or don't enable fan control. Game mode keeps the
  FPS tracer (PresentMon) running while enabled.
- **Fan profiles & game mode** — the profile row shows the active profile, a **Modified** badge and
  **Reload** button when you've edited a channel without saving, **Save as…**, and a **⋯** profile
  menu holding **Delete** and **Create default profiles** (generates **Silent**, **Balanced**, and
  **Gaming** in one click). The dropdown follows whichever profile is actually active — editing a
  channel blanks the selection and shows **Modified**/**Reload** until you save or reload. **Game
  mode** is its own section: turn it on and pick a Gaming/Desktop profile pair; Stats switches
  to Gaming once a foreground game holds ≥10 fps for 5 s, and back to Desktop after 20 s below that —
  no manual swapping between working and playing.
- **Identify a fan** — each channel in the *Fans* window has an **Identify** button that pulses it to
  max for 2 seconds so you can tell which physical fan it is, then restores whatever it was doing
  before; disabled while fan control is off. The always-on safety banner can be collapsed to one line
  with **Got it**.
- **Competing fan software warning** — the *Fans* window flags other tools that write to the same
  fans (MSI Center, Fan Control, Argus Monitor, MSI Afterburner, Corsair iCUE, NZXT CAM, ASUS Armoury
  Crate, SpeedFan, Gigabyte Control Center, EVGA Precision, Corsair Link) so you can close them first.
  This is a warning, not a block — some of those tools sit idle unless you open their UI. GPU-only tools
  (MSI Afterburner, EVGA Precision) get advisory wording — they cannot fight motherboard fans, and on
  their Auto setting there is no conflict at all. **Don't warn again** silences the banner for the tools
  currently listed (persisted); a different tool showing up later still warns.
- **Crash recovery** — if Stats didn't shut down cleanly last time while a fan was under software
  control, every fan is returned to Auto on the next launch and the *Fans* window shows a dismissible
  notice explaining why (and says so if a fan could *not* be handed back — usually other fan software
  holding the device).
- **Sensor health warning** — after three consecutive failed sensor reads, the dashboard identifies
  the failing backend and when the failure episode started. Healthy backends continue updating, and
  the warning clears automatically on the next fully healthy read.
- **Hardware setting** — Settings → Hardware has a **Read motherboard fan headers and USB coolers**
  checkbox (on by default); **uncheck** it if another tool needs exclusive access to those devices —
  fan control needs it on. Takes effect after restarting Stats; **Restart now** relaunches through
  Stats' clean shutdown path so fans are released first.
- **Inverted FPS thresholds** — FPS gets its own warn/crit pair in Settings where *lower* is worse
  (defaults: warn 60 fps, crit 30 fps), instead of the "higher is worse" rule used for temperatures
  and load. *1% Low FPS* starts on its own lower scale (warn 30, crit 15) so it isn't permanently
  amber; per-tile overrides for an inverted metric take the warn value first (e.g. `60/30`).
- **Layout profiles** — **View → Layout profiles** saves named dashboard/overlay metric selections,
  tile kinds/sizes/positions and layout modes. Save, revert, rename or delete without changing fan settings.
  Optional Gaming/Desktop layout picks in **Fans → Game mode** switch on the existing game-state transition.
- **Lock and undo** — **View → Lock layout** prevents accidental moves, resizing and layout-mode changes;
  **Undo layout edit** restores the preceding move, resize, reorder or reset. Lock persists across restarts.
- **Recordings** — **View → Recordings…** explicitly starts/stops timestamped recording of the selected
  dashboard/overlay metrics. Reopen recordings for min/average/max summaries and export the full session to
  CSV. Files live in `%AppData%\Stats\recordings`; interrupted recordings keep their valid samples.
  Charts use the newest 3,600 samples; exports include all samples. Missing readings remain gaps, not zeroes.
- **Comparison** — **View → Compare metrics…** plots up to four metrics on a shared UTC timeline with
  separate scales, a linked cursor and Freeze/Resume. Recorded sessions and alert context use the same view.
- **Top apps** — **Peaks → Processes** shows CPU and working-set memory (private bytes in the tooltip), plus GPU usage when Windows
  exposes GPU-engine counters. Sort by any available column or copy the list. Sampling pauses while Peaks
  is hidden and can be disabled in **Settings → Monitoring**; it never controls or terminates processes.
- **Alerts** — when a monitored metric holds Crit for a hold time (default 10 s, Settings → Alerts,
  1–120 s), Stats shows a tray balloon and can optionally play a chime (off by default). The *Peaks*
  window's **Alerts** tab logs each alert's time, metric, peak value, threshold, and duration (live
  "ongoing" until the metric recovers), newest first, capped at 200 persisted rows. **View context** opens
  up to 120 samples before and 120 after the alert; unfinished events become **interrupted** after restart.
  Alerts evaluate even while the dashboard and overlay are hidden. History stays local in
  `%AppData%\Stats\alerts.json`; transitions save promptly, ongoing context at five-second intervals,
  with a final flush on clean shutdown.

## Beta updates

In **Settings → System → Updates**, enable **Receive beta updates** to include beta builds in automatic
and manual update checks. It is off by default. Updates still require clicking **Update now**; beta builds
may be unstable. Turning it off waits for a stable version at least as new as the installed beta's target
version, rather than downgrading. About displays the full beta version.

Maintain development on `beta`; publish test releases with tags such as `v1.11.0-beta.1`, incrementing the
beta number for every build. The release workflow marks suffix tags as prereleases and never makes them
the latest stable release. After testing, merge `beta` into `master` and tag `v1.11.0` for stable promotion.
Branch pushes alone do not publish installers. Existing stable installations need a stable release containing
the opt-in control first; alternatively testers can install the first beta manually.

Before the first beta release, reconcile the newer `master` notification changes with `beta` and rerun
validation. This change configures the channel but does not publish a release or merge `master`.

## Run from source

    dotnet build -c Release
    src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe

Requires administrator (UAC prompt) and PawnIO (`winget install namazso.PawnIO`).
For FPS while running from source, run `installer\build.ps1` once (or download
`PresentMon-2.5.1-x64.exe` into `installer\vendor\`); the app finds it there.
Pass `--minimized` to create the dashboard and monitoring services without showing the
dashboard until the first tray click.

## Build the installer

    .\installer\build.ps1 -Version 1.2.3     # -> dist\Stats-Setup-1.2.3.exe

Needs Inno Setup 6.3+ (`winget install -e --id JRSoftware.InnoSetup`). The script publishes a
self-contained single-file build, downloads and SHA-256-verifies the pinned PawnIO setup, and
compiles `installer/Stats.iss`. Releases: `git tag v1.2.3 && git push --tags` — CI builds and
attaches the installer to a GitHub Release with a machine-readable SHA-256. Stats verifies that
hash after downloading an update (older releases without a published hash retain size verification).

## Use

- **Header** — actions are grouped (Overlay · Fans · Peaks / Metrics · Settings) with icons; a
  **View** menu holds Collapse all / Expand all; the **Overlay** button shows whether the overlay
  is on. The window has a minimum size of 860×600, and the action groups wrap onto a second line
  on narrow windows.
- **Empty dashboard** — if nothing is selected, the dashboard shows an empty state with an
  **Open Metrics** button instead of a blank window.
- **☰ Metrics** and **⚙ Settings** open a shared flyout with a title and a close button; **Esc**
  closes it and returns focus to the button that opened it.
  - **☰ Metrics** — pick what shows on the dashboard (Dash) and the overlay (Overlay).
    Search box has a placeholder and a clear button and filters as you type; a "No sensors match"
    state with **Clear search** shows when nothing matches; per-group All/None; live value column.
    Persists to `%AppData%\Stats\settings.json`.
  - **⚙ Settings** — grouped into five tabs: **Appearance** (theme, accent, UI scale, core matrix),
    **Monitoring** (polling, history window, thresholds with Warn/Crit columns, limits, tray metric),
    **Alerts**, **Overlay**, and **System** (hardware, startup, updates, diagnostics, About) — same
    settings as before, just organized by category. Applies live. About shows the installed
    version and can check for updates manually; development builds identify themselves and skip checks.
  - **Thresholds** — one editable warn/crit row per (group, unit) pair Stats has discovered
    (CPU and GPU load get their own rows, separate from CPU/GPU temperature), plus a Motherboard
    temperature default (warn 80 °C, crit 95 °C). **Add rule…** offers any discovered (group, unit)
    pair that doesn't have one yet — e.g. Memory, Storage, Network, Cooler — seeded at `0/0` until
    you fill it in; rules are never removed from the grid. Bad input shows an inline error and isn't
    applied.
  - **Tray icon shows** — pick **Auto** (the CPU-temperature heuristic) or any discovered °C/%
    metric for the tray icon to render and tint by severity.
- **Themes and controls** — Dark Amber, Blue, Green, Purple, and Light presets apply live to native
  controls and their popups, including scrollbars, sliders, combo boxes, menus, progress bars,
  check/radio glyphs, and expanders. Light's accent and crit colours are slightly darker than
  before for contrast (no new presets).
- **Graphs** — Settings → Appearance → **Smooth lines** draws sparklines and the detail chart as a
  monotone curve through the samples instead of a jagged polyline (never overshoots past a spike or
  flat run); **Glow and motion effects** adds a line/gauge glow, a stronger fill gradient, a brief
  pulse ring on a new sample, and eased bar/gauge fills — off restores the plain, static look. Both
  default on. Sparklines and the detail chart also lay samples out on a fixed time axis (constant
  spacing anchored to the right edge), so during warm-up the line grows in from the right at a
  steady scale instead of stretching to fill the width every tick.
- **Diagnostics** — Trace output and crash details are written to
  `%AppData%\Stats\logs\stats-YYYYMMDD.log`; the newest seven daily files are kept, and Settings can
  open the folder.
- **Tiles** — right-click, the hover **⋯** button, or Shift+F10/the Menu key on a focused tile:
  kind (Sparkline/Gauge/Bar/Value/Histogram, plus FPS summary on the FPS tile), size (S/M/L —
  160×80/224×144/460×192, with **Larger**/**Smaller**
  items next to the size menu — **Ctrl+Plus** / **Ctrl+Minus** on a focused tile do the same), rename,
  gauge max, thresholds, Details…, remove. The value and its unit are shown separately with stable digit
  widths so readings don't jitter, and the options button sits in a reserved corner so it never
  covers the reading. Empty rows (no limit set, no history yet) are omitted rather than shown
  blank, and a long footer shows a tooltip with the full text. Gauge tiles show the reading beside
  a compact arc rather than inside it.
  Drag a tile onto another in the same group to reorder — an insertion line shows where it'll land,
  and dragging across groups shows a no-drop cursor. Group headers collapse (Collapse all / Expand
  all live in the header's **View** menu). Values turn amber/red past thresholds, with a non-colour
  ▲ (warn) / ‼ (crit) glyph next to the value and a screen-reader name ("metric, value, severity")
  for accessibility.
  - **Thresholds…** opens a dialog with separate Warn/Crit fields for the tile's unit, a note when
    the governing rule is inverted (lower is worse), a **Lower is worse** checkbox when no group rule
    covers it yet, inline validation, and **Clear** to remove the override.
  - **Details…** (or double-click a tile) opens a time-axis chart with y-axis labels, warn/crit guide
    lines, a hover crosshair showing value and time, and current/min/avg/max. If a sensor stops
    reporting, the chart shows a visible gap instead of splicing the surrounding points together.
- **Core matrix** — one CPU tile, a cell per core: load heat, clock, temp.
- **Dashboard layout** — the header's **View** menu offers three arrangements:
  - **Auto arrange** (default) — today's collapsible groups, tiles flowing in a WrapPanel per
    group, same-group drag to reorder.
  - **Free** — the dashboard becomes one scrollable canvas (group headers disappear); drag any
    tile, or the core matrix block, anywhere. With a tile or the core matrix block focused, arrow
    keys nudge it by one step (**Shift**+arrow moves 4 steps at once).
  - **Snap to grid** — the same free canvas, but a dropped or nudged position rounds to the
    nearest 16-pixel grid line; tiles are allowed to overlap.

  **Resize**: in Free or Snap, drag the grip in a tile's bottom-right corner; the outline shows
  which size (S, M, L) it will snap to. Ctrl+Plus / Ctrl+Minus resize the focused tile in any
  layout.

  **Reset tile positions…** (also in the View menu, enabled outside Auto arrange) clears every
  saved position and re-seeds a fresh packed layout. Positions are saved per tile — and for the
  core matrix block — in `settings.json`, so a Free or Snap layout you build is exactly where you
  left it the next time Stats starts. Switching back to Auto never discards your Free/Snap
  positions, and switching away from Auto never discards your group order — each mode remembers
  its own arrangement independently. Positions are kept per tile even for tiles removed from the
  dashboard — re-adding one returns it to the same spot rather than re-seeding it.
- **▤ Peaks** — separate window: now / min / avg / max for your dashboard metrics (or all), with the
  time the session min/max occurred shown as subtext/tooltip; numeric columns are aligned; **Copy**
  copies the table as TSV; Reset session. At the window's minimum width the table scrolls
  horizontally instead of crushing the Metric column. An **Alerts** tab logs sustained-crit events
  for the session (see Alerts above); both tabs show an empty-state message when there's nothing to
  list yet.
- **▣ Overlay** — always-on-top strip; global hotkey (default **Ctrl+Shift+O**) toggles it; click-through
  mode lets mouse pass through (turn off in Settings to drag it); "Reset overlay position" if it gets lost;
  the tray's **Move overlay** item enters a move mode (click-through off, dashed outline, drag it into
  place) without changing your saved click-through setting — exit via the same menu item (now "Done
  moving overlay"), **Esc**, or toggling the overlay. Settings → Overlay → **Sparklines beside each value**
  (on by default) draws a compact per-metric sparkline beside the value horizontally or below it vertically,
  from the same history window and Smooth lines / Glow and motion effects as the dashboard. **Status line**
  (off by default) adds a strip at the overlay's bottom edge showing the active fan profile (with
  write-failed / source-unavailable faults), game mode, and why FPS is unavailable — it only appears when
  one of those has something to say.
- **Tray** — icon shows CPU temp by default (or the metric you pick in Settings), tinted by severity;
  close button hides to tray; left-click reopens; right-click: dashboard / overlay / peaks / settings /
  move overlay / exit.
- **FPS hint** — when FPS metrics are available but none is on the dashboard or overlay, a dismissible
  "Gaming? Add FPS, 1% lows and frame time from ☰ Metrics" banner shows until you add one or click
  Got it (remembered across restarts); the *Game* group
  in the picker carries a caption noting it uses the bundled PresentMon and only runs while selected.
- **Updates** — the dashboard banner includes **What's new**, download progress, SHA-256 verification,
  retry on failure, and an explicit **Update now** action; updates never install without a click.
- **Notices** — the update, degraded-mode, sensor-health-warning, and FPS-hint banners share one
  layout: an icon with text and actions.

## Test

    dotnet test

## Development

`tools/Stats.UiPreview` renders the real windows with simulated data, for screenshots:

    dotnet run --project tools/Stats.UiPreview -- --scenario normal --view dashboard --theme "Dark Amber" --width 1180 --height 720 --ui-scale 1.0 --output out.png

It never touches hardware or the user's settings, and isn't part of the installer.
