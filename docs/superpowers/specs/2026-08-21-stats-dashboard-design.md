# Stats — Unified PC Monitoring Dashboard: Design Spec

**Date:** 2026-08-21
**Status:** Approved by user (design conversation, this date)
**Target machine (primary):** AMD Ryzen 7 9800X3D (PBO Advanced, EXPO), NVIDIA RTX 5070 Ti, 32 GB DDR5, 5 disks (2× SATA HDD, 3× NVMe SSD), 2 Ethernet adapters, Windows 11 Pro.

## 1. Purpose

One native Windows application replacing the need to keep AMD Ryzen Master, Task Manager (Performance tab), Core Temp, and MSI Afterburner open simultaneously. It consolidates their key live metrics into a single dark-themed dashboard, and lets the user select exactly which metrics are visible.

Explicitly a local desktop application. Not a web app, no server, no cloud.

## 2. Scope

### In scope (v1)

- **CPU deep stats:** per-core clocks, per-core temps, per-core loads, package temp (Tdie), total CPU load, CPU package power, PPT / TDC / EDC as % of limit, core voltage (VID), SOC power, SOC voltage.
- **GPU stats:** core clock, memory clock, core voltage (mV), GPU temp, hotspot temp, fan speed (% and RPM where available), board power draw, VRAM used/total, GPU load.
- **Memory:** used / total / % used.
- **Storage:** per-disk activity %, per-disk used/total capacity, read/write throughput where sensors exist. All physical disks discovered at runtime (target machine has 5).
- **Network:** per-adapter upload/download throughput.
- **History graphs:** rolling sparkline per metric tile (120 samples ≈ 2 min at default 1 s poll).
- **Session min / max / avg** per metric (Core Temp style).
- **Metric picker:** checkbox tree of every discovered sensor; unchecked tiles hidden; layout reflows.
- **System tray:** NotifyIcon, tooltip with CPU/GPU temp, context menu (open dashboard / toggle overlay / exit). Window close minimizes to tray.
- **Compact overlay:** borderless, always-on-top, draggable, semi-transparent strip showing a separately selected metric subset.
- **Settings persistence:** JSON at `%AppData%\Stats\settings.json`.

### Out of scope (v1)

- CSV logging (user declined), alerts/notifications, fan control, overclocking/tuning controls, per-process stats, remote monitoring, multi-machine support.

## 3. Architecture

**Stack:** .NET 8, WPF, MVVM. Sensor engine: **LibreHardwareMonitorLib** (NuGet) — single engine covering CPU, GPU, memory, storage, network.

**Elevation:** app manifest sets `requireAdministrator`. LibreHardwareMonitor's kernel driver (ring-0 MSR/SMU access) needs it — same requirement as Core Temp / Ryzen Master.

**Approach decision:** single process, sensor engine in-proc (Approach A). A split collector-service + UI (Approach B) was considered and rejected for v1 as unneeded complexity; the `ISensorReader` interface boundary keeps that door open without rework. Building on an existing tool (Rainmeter plugin, Approach C) rejected — goal is a standalone unified app.

### Components

| Component | Responsibility | Depends on |
|---|---|---|
| `SensorService` | Owns LHM `Computer` (CPU, GPU, memory, storage, network enabled). Background timer polls every 1 s (configurable 0.5–5 s). Maps raw LHM sensors to stable metric IDs. Publishes one immutable `SensorSnapshot` per tick. | LibreHardwareMonitorLib, behind `ISensorReader` |
| `MetricStore` | Per-metric ring buffer (120 samples). Current value, history for sparkline, session min/max/avg. | `SensorService` snapshots |
| Dashboard window | Tile grid grouped CPU / GPU / Memory / Storage / Network. Tile = name, large current value + unit, sparkline, min/max footer. Limit-based metrics (PPT/TDC/EDC) rendered as "% of limit" (Ryzen Master style). Settings flyout hosts the metric picker. | `MetricStore` via ViewModels |
| Overlay window | Borderless, topmost, draggable, semi-transparent. Binds to its own selected-metric list. Toggled from tray or dashboard. | `MetricStore` via ViewModels |
| Tray integration | NotifyIcon, tooltip, context menu. Close-to-tray behavior. | Dashboard/Overlay windows |
| `SettingsService` | Load/save JSON settings: selected dashboard metrics, overlay metrics, poll interval, window positions/opacity. | filesystem |

### Metric identity

Stable string IDs decouple UI/settings from LHM's runtime sensor tree, e.g.:
`cpu.package.temp`, `cpu.core0.clock`, `cpu.core0.load`, `cpu.ppt`, `cpu.tdc`, `cpu.edc`, `cpu.core.voltage`, `cpu.soc.power`, `gpu.core.clock`, `gpu.hotspot`, `gpu.vram.used`, `disk.C.activity`, `net.eth4.down`.

Each maps to a `MetricDefinition` (id, display name, group, unit, format string, optional limit value for %-of-limit rendering).

### Data flow

Timer tick → LHM `Update()` → `SensorService` builds snapshot → `MetricStore` appends to ring buffers → ViewModels raise change notifications → WPF bindings update tiles and sparklines. UI never touches LHM types directly.

### Sparklines

Custom lightweight `Polyline` rendering inside each tile. 120 points per tile; no third-party charting dependency.

## 4. Error handling

- **Sensor absent on this machine:** metric never appears in picker; no dead tiles.
- **Kernel driver init failure:** non-blocking warning banner; app degrades to WMI/`PerformanceCounter` fallback providing CPU load, RAM, disk activity, network throughput. Temps, power, clocks, voltages unavailable in degraded mode and marked so in picker.
- **GPU asleep/removed mid-session:** affected tiles show "—", recover automatically when sensor returns.
- **Settings file corrupt/missing:** fall back to defaults (sensible starter metric set), rewrite file on next save.

## 5. Testing

- **Unit tests** (sensor layer mocked behind `ISensorReader`): sensor→metric mapping, ring buffer behavior (wrap, min/max/avg), settings round-trip and corrupt-file fallback.
- **Live smoke test** on target machine: verify sensor discovery matches expectations (per-core temps, PPT/TDC/EDC, GPU hotspot, all 5 disks, both Ethernet adapters), verify admin elevation prompt, tray behavior, overlay topmost over a game/fullscreen window.

## 6. Success criteria

1. One window shows, live, the headline numbers currently gathered from four apps: CPU temp/clocks/power/PPT-TDC-EDC/voltages, GPU clock/voltage/temp/fan/power, RAM, per-disk, per-adapter network.
2. User can hide/show any metric via picker; choice survives restart.
3. Overlay stays on top and readable while another app has focus.
4. Idle overhead noticeably below running the four source apps together (single 1 s poll loop).
