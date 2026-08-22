# Stats fan control — design

**Date:** 2026-08-22 · **Branch:** master → `feature/fan-control` · **Status:** approved in conversation, pending spec review

## Goal

Control every fan Stats can see — motherboard headers, GPU fans, the AIO's pump/radiator — from a
dedicated **Fans** window, using the temperatures Stats already monitors as curve inputs. Auto (leave it
to the device), Manual (fixed %), or Curve (temperature → %). Safe by default: nothing is written until
the user enables fan control, and everything we touched goes back to the device's own control when Stats
exits.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Devices | Motherboard Super-I/O headers (ITE IT8696E ×6), NVIDIA GPU fans (30–100 %), MSI CoreLiquid S360 pump + radiator fans (all via LibreHardwareMonitor `IControl`) |
| Control model | Per channel: **Auto / Manual % / Curve** |
| Curve editing | Draggable points on a temp→% graph with live marker |
| Placement | Separate **Fans** window (like Peaks), opened from dashboard toolbar and tray menu |
| Architecture | **A — in-process controller on the existing poll loop**, writing through the same LHM `Computer` that `LhmSensorReader` owns |
| Master switch | `FanControlEnabled`, default **off** |
| Rejected | Background service (B) — machinery without payoff yet; linking to Fan Control (C) |

## Hardware facts (probe 2026-08-22, Gigabyte B850 GAMING WIFI6)

| LHM hardware | Type | Controls | RPM sensors | Temps |
|---|---|---|---|---|
| ITE IT8696E (sub-hardware of Motherboard) | `SuperIO` | `Control` #0–5 "Fan #1..#6", 0–100 %, mode `Undefined` (= BIOS) | `Fan` #0–5 (same index) | 5 board temps |
| NVIDIA GeForce RTX 5070 Ti | `GpuNvidia` | `Control` #1, #2 "GPU Fan 1/2", **30–100 %** | `Fan` #1, #2 | GPU Core, Memory Junction |
| MSI CoreLiquid S360 | `Cooler` | attached to the `Fan` sensors themselves: Radiator Fan #10, Pump Fan #13, Pump #14, 0–100 % | same sensors | Liquid Temperature |

`LhmSensorReader` today enables only Cpu/Gpu/Memory/Storage/Network, so the Super-I/O chip and the AIO are
not read at all yet.

## Architecture

```
                     poll thread                                           UI thread
SensorPoller ─Read()─▶ CompositeSensorReader ─▶ LhmSensorReader ──┐      FansViewModel ──▶ FanController.SetMode/SetManual/SetCurve…
      │                                     (owns LHM Computer)    │            ▲                      │ (desired state, under lock)
      └─SnapshotAvailable(bg thread)─▶ FanController.Tick(snapshot)─┼──writes──▶ IFanControlBackend (= LhmSensorReader) ─▶ IControl.SetSoftware/SetDefault
                                                                   │
                                        Dispatcher ─▶ store.Apply, VMs refresh (unchanged) ◀──────────┘ FansViewModel.Refresh() shows RPM/%/state
```

All LHM access stays on the poll thread: `Read()` and the fan writes are both invoked from the poller's
background thread (`SnapshotAvailable` fires there before the app marshals to the Dispatcher). UI changes
only mutate desired state; they take effect on the next tick (≤ poll interval, 1 s default).

## Components

### Stats.Core/Sensors (additions)

**`LhmSensorReader`** — also enables `IsMotherboardEnabled` and `IsControllerEnabled`. Implements
**`IFanControlBackend`**:

```csharp
public sealed record FanChannel(string Id, string Name, string Device, string? RpmMetricId,
                                string? PercentMetricId, float MinPercent, float MaxPercent);
public interface IFanControlBackend
{
    IReadOnlyList<FanChannel> Channels { get; }        // discovered once, after Discover()
    void SetPercent(string channelId, float percent);  // poll thread only; clamps to channel range
    void SetAuto(string channelId);                    // poll thread only; IControl.SetDefault()
    float? CurrentPercent(string channelId);           // last value LHM reports for the control sensor
}
```
Channel discovery: every sensor whose `Control` is non-null — for `Control`-type sensors the RPM sensor is
the `Fan` sensor on the same hardware with the same `Index` (ITE, NVIDIA); for `Fan`-type sensors carrying
a control (AIO) the sensor itself is the RPM source. `Id` = LHM `Identifier` string (stable across runs, e.g.
`/lpc/it8696e/0/control/0`). `Name` = sensor name; `Device` = hardware name. Min/Max from
`IControl.MinSoftwareValue/MaxSoftwareValue`.

**`SensorMapper`** — new hardware types: `"Motherboard" or "SuperIO"` → `MetricGroup.Motherboard`,
`"Cooler"` → `MetricGroup.Cooler` (both appended to the enum after `Game`). Display names for these groups
use the plain sensor name (single instance). So board temps, header RPMs, liquid temp, pump RPM become
ordinary metrics (dashboard, overlay, peaks, thresholds). `Control` sensors are mapped too (unit `%`) so
the current PWM is a metric.

**`PerfCounterSensorReader`** — implements `IFanControlBackend` with zero channels (degraded mode: no fan
control). `CompositeSensorReader` forwards `IFanControlBackend` to the first reader that implements it.

### Stats.Core/Fans (new)

**`FanCurve`** — immutable `IReadOnlyList<(float TempC, float Percent)>` sorted by temp, 2–8 points;
`Evaluate(tempC)` = linear interpolation, flat beyond the ends; validation (`TryCreate`) rejects <2 points,
duplicates within 0.5 °C, out-of-range values (temp 0–120, percent 0–100). Default curve for new Curve
channels: `(30,30) (50,45) (70,75) (85,100)`.

**`FanChannelState`** (desired, persisted as `FanChannelPref`):
```csharp
public enum FanMode { Auto, Manual, Curve }
public sealed class FanChannelPref { FanMode Mode = Auto; float ManualPercent = 50; string? SourceMetricId;
                                     List<FanPoint> Points = default curve; string? Name; }
```

**`FanController`** — the loop. `Tick(SensorSnapshot, DateTime nowUtc)` on the poll thread:
1. If `!Enabled` (master switch): if anything is in software mode, `SetAuto` it once; return.
2. For each channel with a pref: compute the **target**:
   - `Auto` → `SetAuto` (only when transitioning; not every tick).
   - `Manual` → `ManualPercent`.
   - `Curve` → source value from the snapshot (`SourceMetricId`); if null or last non-null older than
     **10 s** → **failsafe**: treat as `Auto`, flag `ChannelStatus.SourceUnavailable`. Else
     `curve.Evaluate(temp)` with **hysteresis**: the source temp used for evaluation only moves when it
     differs from the last used temp by ≥ **2 °C**.
3. Apply **floors/ceilings**: clamp to `[MinPercent, MaxPercent]`; channels whose name contains "pump"
   (case-insensitive) have an additional floor of **50 %**.
4. **Slew limit**: move at most **10 percentage points per tick** from the last written value (first write
   after entering Manual/Curve is immediate).
5. Write only when the rounded value changed (avoid hammering the chip). Record `LastWritten`, `LastSource`,
   `Status` per channel for the UI.
`SetMode/SetManualPercent/SetCurve/SetSource(channelId, …)` from the UI thread update prefs under a lock and
mark the channel dirty; the next tick applies. `RestoreAll()` (called from `Dispose`, and when the master
switch turns off) → `SetAuto` on every channel we ever wrote to. Exposes `IReadOnlyList<FanChannelView>`
snapshots for the VM (`Id, Name, Device, Mode, Rpm, Percent, TargetPercent, Status`).

**Failure modes:** a backend exception on write → log (Trace), mark `Status = WriteFailed`, retry next tick;
three consecutive failures → channel to `Auto` + status kept until the user changes the mode. Missing
backend (degraded) → Fans window shows "Fan control unavailable (hardware reader not active)".

### Stats.Core/ViewModels

**`FansViewModel`** — `Enabled` (two-way to settings), `Devices: ObservableCollection<FanDeviceGroupViewModel>`
→ `Channels: ObservableCollection<FanChannelViewModel>` (`Name` editable, `RpmText`, `PercentText`, `Mode`,
`ManualPercent`, `SourceMetricId` + `SourceOptions` (all `°C` metrics: id + friendly name, grouped), `Points`
(observable, for the editor), `LiveTempText`, `TargetText`, `StatusText`). Commands: `ResetCurve`,
`SetAllAuto`. `Refresh()` every tick (from the Dispatcher callback) pulls `FanChannelView`s. Changes → settings
saved via the existing `SaveSettings` callback.

### Stats.App

**`FansWindow`** (like `PeaksWindow`: bounds persisted, `AllowClose` hide-to-tray semantics): header with the
master toggle and warning text ("Writes fan speeds to your hardware. Close other fan software (MSI Center,
Fan Control) first. Speeds return to device control when Stats exits."), then a list grouped by device; each
channel row: name (double-click to rename), RPM, current %, mode radio (Auto / Manual / Curve), Manual
slider, and for Curve: source combo + **`FanCurveEditor`**.

**`FanCurveEditor`** (custom `Control`): X = temp 20–100 °C, Y = 0–100 %; points as draggable thumbs
(mouse down/move/up, clamped, neighbours keep order, right-click removes, double-click on the line adds, min 2
max 8); polyline; horizontal shading for the channel's min floor; a live marker (current source temp → target
%) driven by two DPs; exposes `Points` (two-way `ObservableCollection<FanPoint>`), `MinPercent`,
`LiveTemp`, `LiveTarget`. Theme-aware brushes from `Theme.xaml`.

Tray menu + dashboard toolbar: "Fans…" item → `App.ShowFans()`.

### Settings

```csharp
public bool FanControlEnabled { get; set; }                  // default false
public Dictionary<string, FanChannelPref> FanChannels { get; set; } = new();
public double? FansLeft/Top/Width/Height
```
`SettingsService.Load` clamps `ManualPercent` to 0–100 and drops malformed curves (falls back to default).

### Startup / shutdown sequence

1. Reader discovered → channels known; `FanController` constructed with backend + settings (desired state
   loaded, nothing written yet).
2. Poller starts. **First tick**: controller applies Manual/Curve channels immediately (curves with a
   source that has no value yet wait; failsafe timer starts at first tick, not at launch).
3. `OnExit`: `_fanController.RestoreAll()` **before** `_reader.Dispose()` (LHM `Close()` does not restore
   controls).

### Out of scope (recorded)

Profiles/presets, per-channel hysteresis/slew tuning, "stop fan below X", detecting other fan software, a
background service for crash-survival, BIOS-curve readback, GPU "zero RPM" handling, ARM64.

## Testing

`tests/Stats.Core.Tests`, pure logic:
- `FanCurveTests` — interpolation (exact points, between, beyond ends), validation rejections, default curve.
- `FanControllerTests` with a `FakeBackend` (records writes) and a fixed clock: master switch off → no writes
  and restore-on-disable; Manual writes once then only on change; Curve follows source with hysteresis
  (1.9 °C change → no write; 2 °C → write); slew limit 10 pts/tick (0→100 takes 10 ticks); failsafe after
  10 s of null source → `SetAuto` + status; pump floor 50 %; GPU min 30 % clamp; write failure ×3 → Auto;
  `RestoreAll` resets only touched channels; mode transitions (Curve→Auto emits `SetAuto` once).
- `SensorMapperTests` — `SuperIO`/`Motherboard`/`Cooler` → new groups, Fan/Control units.
- `FansViewModelTests` — source options are exactly the `°C` metrics; edits persist to settings; `Refresh`
  reflects controller views; rename.
- `SettingsServiceTests` — `FanChannels` round-trip; malformed curve → default; `FanControlEnabled` default false.
Manual (user): enable, set one case-fan header to Manual 40 % → RPM changes; Curve on CPU Tctl → ramps
under load; Auto → BIOS curve resumes; exit Stats → fans back to BIOS; GPU 30 % floor; AIO radiator fan
Manual; confirm MSI Center is not running during the test.
