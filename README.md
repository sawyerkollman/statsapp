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
  defaults in one click. Turn on **Game mode** and pick a Gaming/Desktop profile pair; Stats switches
  to Gaming once a foreground game holds ≥10 fps for 5 s, and back to Desktop after 20 s below that —
  no manual swapping between working and playing.
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
- **⚙ Settings** — poll rate, history window (2/5/15/60 min), warn/crit thresholds,
  PPT/TDC/EDC/GPU-power limits, overlay layout/opacity/font scale/click-through/hotkey,
  core matrix toggle, startup task, diagnostics, and About. Applies live. About shows the installed
  version and can check for updates manually; development builds identify themselves and skip checks.
- **Themes and controls** — Dark Amber, Blue, Green, Purple, and Light presets apply live to native
  controls and their popups, including scrollbars, sliders, combo boxes, menus, progress bars,
  check/radio glyphs, and expanders.
- **Diagnostics** — Trace output and crash details are written to
  `%AppData%\Stats\logs\stats-YYYYMMDD.log`; the newest seven daily files are kept, and Settings can
  open the folder.
- **Tiles** — right-click: kind (Sparkline/Gauge/Bar/Value), size (S/M/L), rename, gauge max, remove.
  Drag a tile onto another in the same group to reorder. Group headers collapse; Collapse/Expand all in the header.
  Values turn amber/red past thresholds.
- **Core matrix** — one CPU tile, a cell per core: load heat, clock, temp.
- **▤ Peaks** — separate window: now / min / avg / max for your dashboard metrics (or all); Reset session.
- **▣ Overlay** — always-on-top strip; global hotkey (default **Ctrl+Shift+O**) toggles it; click-through
  mode lets mouse pass through (turn off in Settings to drag it); "Reset overlay position" if it gets lost.
- **Tray** — icon shows CPU temp, tinted by severity; close button hides to tray; left-click reopens;
  right-click: dashboard / overlay / peaks / settings / exit.
- **Updates** — the dashboard banner includes **What's new**, download progress, SHA-256 verification,
  retry on failure, and an explicit **Update now** action; updates never install without a click.

## Test

    dotnet test
