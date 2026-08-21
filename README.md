# Stats

Unified PC monitoring dashboard for Windows. One dark-themed window for what
Ryzen Master, Task Manager, Core Temp, and Afterburner each show a slice of:
CPU per-core clocks/temps/loads, package power, PPT, voltages; GPU clocks,
temps, fan, power, VRAM; RAM; per-disk activity; per-adapter network throughput.

## Run

    dotnet build -c Release
    src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe

Requires administrator (UAC prompt) — the LibreHardwareMonitor kernel driver
needs it for CPU temperature/power sensors, same as Core Temp or Ryzen Master.
Without it the app falls back to a degraded mode (loads/usage only).

## Use

- **⚙ Metrics** — choose which sensors show on the dashboard (Dash column)
  and on the overlay (Overlay column). Persists to `%AppData%\Stats\settings.json`.
- **Tray icon** — close button hides to tray; left-click reopens; right-click:
  open / toggle overlay / exit. Tooltip shows CPU/GPU temp.
- **Overlay** — borderless always-on-top strip; drag to move.
- **Limits** — optional: add `"MetricLimits": { "<metric-id>": 150 }` to
  settings.json to render a metric as % of that limit (e.g. PBO PPT watts).
- **Poll rate** — `PollIntervalSeconds` in settings.json, 0.5–5.

## Test

    dotnet test
