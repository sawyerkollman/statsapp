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
- **Fan control** — *Fans* window (toolbar / tray): every LibreHardwareMonitor-controllable fan
  (motherboard headers, GPU fans, supported USB coolers) can be Auto (device/BIOS), Manual %, or follow
  a temperature curve driven by one or more temperatures Stats monitors (the curve follows the hottest
  selected source). Off until you enable it;
  2 °C hysteresis, max 10 %/s change, falls back to device control if the source temperature
  disappears for 10 s, pumps never below 50 %; fans return to device control when you exit Stats.
  Close MSI Center / Fan Control / Afterburner fan curves first. On a fatal error Stats makes a
  best-effort attempt to return every fan to device control before terminating; the next-launch
  recovery marker remains the fallback if that cleanup cannot complete.
  Stats now also reads the motherboard Super-I/O chip and USB fan/AIO controllers through
  LibreHardwareMonitor (new *Motherboard* and *Cooler* metric groups); if another vendor tool owns
  those devices you may see contention — close it or don't enable fan control. Game mode keeps the
  FPS tracer (PresentMon) running while enabled.
- **Fan profiles & game mode** — save the current per-channel fan setup as a named profile from the
  *Fans* window (pick a profile from the dropdown to load it; Save as… / Delete / Create defaults), or
  generate **Silent**, **Balanced**, and **Gaming**
  defaults in one click. The dropdown follows whichever profile is actually active — editing a channel
  blanks the selection and shows a **Reload** button that snaps back to the last-loaded profile without
  having to re-pick it. Turn on **Game mode** and pick a Gaming/Desktop profile pair; Stats switches
  to Gaming once a foreground game holds ≥10 fps for 5 s, and back to Desktop after 20 s below that —
  no manual swapping between working and playing.
- **Identify a fan** — each channel in the *Fans* window has an **Identify** button that pulses it to
  max for 2 seconds so you can tell which physical fan it is, then restores whatever it was doing
  before; disabled while fan control is off. The always-on safety banner can be collapsed to one line
  with **Got it**.
- **Competing fan software warning** — the *Fans* window flags other tools that write to the same
  fans (MSI Center, Fan Control, Argus Monitor, MSI Afterburner, Corsair iCUE, NZXT CAM, ASUS Armoury
  Crate, SpeedFan, Gigabyte Control Center, EVGA Precision, Corsair Link) so you can close them first.
  This is a warning, not a block — some of those tools sit idle unless you open their UI.
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
- **Alerts** — when a monitored metric holds Crit for a hold time (default 10 s, Settings → Alerts,
  1–120 s), Stats shows a tray balloon and can optionally play a chime (off by default). The *Peaks*
  window's **Alerts** tab logs each alert's time, metric, peak value, threshold, and duration (live
  "ongoing" until the metric recovers) for the current session, newest first, capped at 200 rows.
  Alerts evaluate even while the dashboard and overlay are hidden.

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

- **☰ Metrics** — pick what shows on the dashboard (Dash) and the overlay (Overlay).
  Search box filters; per-group All/None; live value column. Persists to `%AppData%\Stats\settings.json`.
- **⚙ Settings** — poll rate, dashboard UI scale (0.9×–1.3×), history window (2/5/15/60 min),
  a rule-driven threshold grid, PPT/TDC/EDC/GPU-power limits, overlay layout/opacity/font
  scale/click-through/hotkey, core matrix toggle, which metric the tray icon shows, alerts,
  theme, startup task, diagnostics, and About. Applies live. About shows the installed
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
  check/radio glyphs, and expanders.
- **Diagnostics** — Trace output and crash details are written to
  `%AppData%\Stats\logs\stats-YYYYMMDD.log`; the newest seven daily files are kept, and Settings can
  open the folder.
- **Tiles** — right-click, the hover **⋯** button, or Shift+F10/the Menu key on a focused tile:
  kind (Sparkline/Gauge/Bar/Value), size (S/M/L), rename, gauge max, thresholds, Details…, remove.
  Drag a tile onto another in the same group to reorder — an insertion line shows where it'll land,
  and dragging across groups shows a no-drop cursor. Group headers collapse; Collapse/Expand all in
  the header. Values turn amber/red past thresholds, with a non-colour ▲ (warn) / ‼ (crit) glyph next
  to the value and a screen-reader name ("metric, value, severity") for accessibility.
  - **Thresholds…** opens a dialog with separate Warn/Crit fields for the tile's unit, a note when
    the governing rule is inverted (lower is worse), a **Lower is worse** checkbox when no group rule
    covers it yet, inline validation, and **Clear** to remove the override.
  - **Details…** (or double-click a tile) opens a time-axis chart with y-axis labels, warn/crit guide
    lines, a hover crosshair showing value and time, and current/min/avg/max. If a sensor stops
    reporting, the chart shows a visible gap instead of splicing the surrounding points together.
- **Core matrix** — one CPU tile, a cell per core: load heat, clock, temp.
- **▤ Peaks** — separate window: now / min / avg / max for your dashboard metrics (or all), with the
  time the session min/max occurred shown as subtext/tooltip; **Copy** copies the table as TSV;
  Reset session. An **Alerts** tab logs sustained-crit events for the session (see Alerts above).
- **▣ Overlay** — always-on-top strip; global hotkey (default **Ctrl+Shift+O**) toggles it; click-through
  mode lets mouse pass through (turn off in Settings to drag it); "Reset overlay position" if it gets lost;
  the tray's **Move overlay** item enters a move mode (click-through off, dashed outline, drag it into
  place) without changing your saved click-through setting — exit via the same menu item (now "Done
  moving overlay"), **Esc**, or toggling the overlay.
- **Tray** — icon shows CPU temp by default (or the metric you pick in Settings), tinted by severity;
  close button hides to tray; left-click reopens; right-click: dashboard / overlay / peaks / settings /
  move overlay / exit.
- **FPS hint** — when FPS metrics are available but none is on the dashboard or overlay, a dismissible
  "Gaming? Add FPS, 1% lows and frame time from ☰ Metrics" banner shows until you add one or click
  Got it (remembered across restarts); the *Game* group
  in the picker carries a caption noting it uses the bundled PresentMon and only runs while selected.
- **Updates** — the dashboard banner includes **What's new**, download progress, SHA-256 verification,
  retry on failure, and an explicit **Update now** action; updates never install without a click.

## Test

    dotnet test
