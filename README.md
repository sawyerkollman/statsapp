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
Optional checkbox: start Stats at sign-in (a Scheduled Task, so no UAC prompt each login).
Uninstall from Settings → Apps; PawnIO is left installed because other tools may use it.
- **FPS counter** — select FPS / 1% Low FPS / Frame Time from the *Game* group. Stats runs
  the bundled Intel PresentMon helper only while one of them is selected. Launch Stats from
  the Start menu or desktop shortcut: processes started from the Microsoft Store build of
  PowerShell/Terminal inherit an MSIX identity that Windows blocks from ETW tracing, so FPS
  stays blank there.
- **Fan control** — *Fans* window (toolbar / tray): every LibreHardwareMonitor-controllable fan
  (motherboard headers, GPU fans, supported USB coolers) can be Auto (device/BIOS), Manual %, or
  follow a temperature curve driven by any temperature Stats monitors. Off until you enable it;
  2 °C hysteresis, max 10 %/s change, falls back to device control if the source temperature
  disappears for 10 s, pumps never below 50 %; fans return to device control when you exit Stats.
  Close MSI Center / Fan Control / Afterburner fan curves first. If Stats *crashes* (not exits),
  fans keep their last speed until Stats runs again or you reboot.
  Stats now also reads the motherboard Super-I/O chip and USB fan/AIO controllers through
  LibreHardwareMonitor (new *Motherboard* and *Cooler* metric groups); if another vendor tool owns
  those devices you may see contention — close it or don't enable fan control.

## Run from source

    dotnet build -c Release
    src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe

Requires administrator (UAC prompt) and PawnIO (`winget install namazso.PawnIO`).
For FPS while running from source, run `installer\build.ps1` once (or download
`PresentMon-2.5.1-x64.exe` into `installer\vendor\`); the app finds it there.

## Build the installer

    .\installer\build.ps1 -Version 1.2.3     # -> dist\Stats-Setup-1.2.3.exe

Needs Inno Setup 6.3+ (`winget install -e --id JRSoftware.InnoSetup`). The script publishes a
self-contained single-file build, downloads and SHA-256-verifies the pinned PawnIO setup, and
compiles `installer/Stats.iss`. Releases: `git tag v1.2.3 && git push --tags` — CI builds and
attaches the installer to a GitHub Release.

## Use

- **☰ Metrics** — pick what shows on the dashboard (Dash) and the overlay (Overlay).
  Search box filters; per-group All/None; live value column. Persists to `%AppData%\Stats\settings.json`.
- **⚙ Settings** — poll rate, history window (2/5/15/60 min), warn/crit thresholds, PPT/TDC/EDC/GPU-power
  limits, overlay layout/opacity/font scale/click-through/hotkey, core matrix toggle. Applies live.
- **Tiles** — right-click: kind (Sparkline/Gauge/Bar/Value), size (S/M/L), rename, gauge max, remove.
  Drag a tile onto another in the same group to reorder. Group headers collapse; Collapse/Expand all in the header.
  Values turn amber/red past thresholds.
- **Core matrix** — one CPU tile, a cell per core: load heat, clock, temp.
- **▤ Peaks** — separate window: now / min / avg / max for your dashboard metrics (or all); Reset session.
- **▣ Overlay** — always-on-top strip; global hotkey (default **Ctrl+Shift+O**) toggles it; click-through
  mode lets mouse pass through (turn off in Settings to drag it); "Reset overlay position" if it gets lost.
- **Tray** — icon shows CPU temp, tinted by severity; close button hides to tray; left-click reopens;
  right-click: dashboard / overlay / peaks / settings / exit.

## Test

    dotnet test
