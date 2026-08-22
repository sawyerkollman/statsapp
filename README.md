# Stats

Unified PC monitoring dashboard for Windows. One dark-themed window for what
Ryzen Master, Task Manager, Core Temp, and Afterburner each show a slice of:
CPU per-core clocks/temps/loads, package power, PPT, voltages; GPU clocks,
temps, fan, power, VRAM; RAM; per-disk activity; per-adapter network throughput.

## Run

    dotnet build -c Release
    src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe

Requires administrator (UAC prompt) and the **PawnIO** kernel driver
(`winget install namazso.PawnIO` or https://pawnio.eu) — LibreHardwareMonitor 0.9.6
reads CPU temperature/clock/power through PawnIO only, same as Core Temp or Ryzen Master.
Without it the app shows a degraded-mode banner (loads/usage only).

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
