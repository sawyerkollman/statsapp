# Fan Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "Fans" window that puts every LibreHardwareMonitor-controllable fan (motherboard headers, GPU fans, AIO pump/radiator) under Auto / Manual % / temperature-curve control, using any temperature Stats already monitors as the curve source, with safe defaults and restore-on-exit.

**Architecture:** `LhmSensorReader` additionally enables motherboard + controller hardware and implements `IFanControlBackend` (channels discovered from `ISensor.Control`). A `FanController` in `Stats.Core/Fans/` runs on the poller's background thread (`SensorPoller.SnapshotAvailable`) — same thread as LHM reads — computing targets (hysteresis, slew, floors, failsafe) and writing through the backend. A `FansViewModel` + `FansWindow` (cloned from Peaks) edit desired state; a custom `FanCurveEditor` control provides draggable curve points.

**Tech Stack:** .NET 8 / C# 12, WPF, CommunityToolkit.Mvvm 8.4, LibreHardwareMonitorLib 0.9.6 (`IControl`), xunit 2.9. No new packages.

**Spec:** `docs/superpowers/specs/2026-08-22-fan-control-design.md`

## Global Constraints

- Target `net8.0-windows`, Nullable + ImplicitUsings on, **no new NuGet packages**.
- All LHM access (reads and control writes) happens on the poller thread; UI only mutates desired state. `FanController.RestoreAll()` is called only after the poller is stopped.
- Constants (verbatim): hysteresis **2 °C**; slew **10 percentage points per tick**; source stale after **10 s**; pump floor **50 %** for channels whose name contains "pump" (case-insensitive); write failures **3** consecutive → channel to Auto; master switch `FanControlEnabled` default **false**; curve points **2–8**, temp **0–120 °C**, percent **0–100**, duplicate temps within **0.5 °C** rejected; default curve `(30,30) (50,45) (70,75) (85,100)`; default `ManualPercent` **50**.
- New enum members appended in this order: `MetricGroup.Motherboard`, `MetricGroup.Cooler` (after `Game`).
- `SensorMapper`: `"Motherboard"` or `"SuperIO"` → `Motherboard`; `"Cooler"` → `Cooler`; both single-instance display (plain sensor name).
- LHM API (verified by reflection on 0.9.6): `ISensor.Control : IControl?`; `IControl { ControlMode ControlMode; float SoftwareValue, MinSoftwareValue, MaxSoftwareValue; void SetSoftware(float); void SetDefault(); }`; `ControlMode { Undefined, Software, Default }`; `ISensor.Identifier.ToString()` e.g. `/lpc/it8696e/0/control/0`; `ISensor.Index`.
- Tests flat in `tests/Stats.Core.Tests/`, namespace `Stats.Core.Tests`; `dotnet test` green with **zero warnings** after every task (xUnit analyzers: constants go in the `expected` slot).
- Commit messages `feat(core): …` / `feat(app): …` / `test(core): …` / `docs: …`, body ending with:
  ```
  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01XaNQkVXyDTEeBUyRZz22GE
  ```
- **Workers never write to hardware.** No task runs the app with fan control enabled; all hardware verification is the user's (Task 10).

---

### Task 1: Settings model — `FanMode`, `FanPoint`, `FanChannelPref`, `AppSettings` additions, load sanitation

**Files:**
- Create: `src/Stats.Core/Settings/FanChannelPref.cs`
- Modify: `src/Stats.Core/Settings/AppSettings.cs` (append after `PeaksHeight`)
- Modify: `src/Stats.Core/Settings/SettingsService.cs:44-46` (`Load`, after the `ThresholdRules` seed)
- Create: `src/Stats.Core/Fans/FanCurve.cs` (needed by sanitation — created here, tested in Task 2)
- Test: `tests/Stats.Core.Tests/SettingsServiceTests.cs` (append)

**Interfaces:**
- Produces: `enum FanMode { Auto, Manual, Curve }`; `sealed record FanPoint(float TempC, float Percent)`; `sealed class FanChannelPref { FanMode Mode; float ManualPercent = 50; string? SourceMetricId; List<FanPoint> Points = FanCurve.DefaultPoints.ToList(); string? Name; }`; `AppSettings.FanControlEnabled`, `.FanChannels: Dictionary<string, FanChannelPref>`, `.FansLeft/FansTop/FansWidth/FansHeight: double?`; `FanCurve` (full API in Task 2 — created here with the code from Task 2 Step 3 so the solution compiles).

- [ ] **Step 1: Write the failing tests** (append to `SettingsServiceTests`)

```csharp
    [Fact]
    public void Load_FanControl_DefaultsOffAndEmpty()
    {
        var s = new SettingsService(_dir).Load();
        Assert.False(s.FanControlEnabled);
        Assert.Empty(s.FanChannels);
    }

    [Fact]
    public void SaveThenLoad_FanChannels_RoundTrip()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings { FanControlEnabled = true };
        s.FanChannels["/lpc/it8696e/0/control/0"] = new FanChannelPref
        {
            Mode = FanMode.Curve, ManualPercent = 35, SourceMetricId = "cpu.x.temperature.tctl", Name = "Front intake",
            Points = new() { new(30, 20), new(60, 50), new(80, 100) },
        };
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.True(loaded.FanControlEnabled);
        var p = loaded.FanChannels["/lpc/it8696e/0/control/0"];
        Assert.Equal(FanMode.Curve, p.Mode);
        Assert.Equal(35f, p.ManualPercent);
        Assert.Equal("cpu.x.temperature.tctl", p.SourceMetricId);
        Assert.Equal("Front intake", p.Name);
        Assert.Equal(new[] { new FanPoint(30, 20), new FanPoint(60, 50), new FanPoint(80, 100) }, p.Points);
    }

    [Fact]
    public void Load_MalformedCurve_FallsBackToDefault_AndClampsManual()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings();
        s.FanChannels["a"] = new FanChannelPref { ManualPercent = 140, Points = new() { new(50, 50) } }; // 1 point = invalid
        s.FanChannels["b"] = new FanChannelPref { ManualPercent = -5, Points = new() { new(50, 50), new(50.2f, 60) } }; // dup temps
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(100f, loaded.FanChannels["a"].ManualPercent);
        Assert.Equal(FanCurve.DefaultPoints, loaded.FanChannels["a"].Points);
        Assert.Equal(0f, loaded.FanChannels["b"].ManualPercent);
        Assert.Equal(FanCurve.DefaultPoints, loaded.FanChannels["b"].Points);
    }
```
Add `using Stats.Core.Fans;` at the top of the test file.

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~SettingsServiceTests" --nologo` → build error (types missing).

- [ ] **Step 3: Implement**

`src/Stats.Core/Settings/FanChannelPref.cs`:
```csharp
using Stats.Core.Fans;

namespace Stats.Core.Settings;

public enum FanMode { Auto, Manual, Curve }

/// <summary>One curve vertex: at TempC the fan runs at Percent.</summary>
public sealed record FanPoint(float TempC, float Percent);

/// <summary>Desired state for one controllable fan channel, keyed by the LHM control identifier.</summary>
public sealed class FanChannelPref
{
    public FanMode Mode { get; set; } = FanMode.Auto;
    public float ManualPercent { get; set; } = 50f;
    /// <summary>Metric id (unit °C) that drives the curve; null = no source chosen yet.</summary>
    public string? SourceMetricId { get; set; }
    public List<FanPoint> Points { get; set; } = FanCurve.DefaultPoints.ToList();
    /// <summary>Friendly display-name override; null/blank = hardware name.</summary>
    public string? Name { get; set; }
}
```

`src/Stats.Core/Fans/FanCurve.cs` — paste the full implementation from **Task 2 Step 3** (it has no dependencies beyond `FanPoint`).

`AppSettings.cs` — append after `PeaksHeight`:
```csharp
    // ---- v1.3 fan control ----
    /// <summary>Master switch: nothing is written to fan controls while false.</summary>
    public bool FanControlEnabled { get; set; }
    /// <summary>Per-channel desired state keyed by LHM control identifier (e.g. "/lpc/it8696e/0/control/0").</summary>
    public Dictionary<string, FanChannelPref> FanChannels { get; set; } = new();
    public double? FansLeft { get; set; }
    public double? FansTop { get; set; }
    public double? FansWidth { get; set; }
    public double? FansHeight { get; set; }
```

`SettingsService.Load` — after `settings.OverlayHotkey ??= "";` add:
```csharp
        foreach (var pref in settings.FanChannels.Values)
        {
            pref.ManualPercent = Math.Clamp(pref.ManualPercent, 0f, 100f);
            if (!FanCurve.TryCreate(pref.Points, out _))
                pref.Points = FanCurve.DefaultPoints.ToList();
        }
```
and `using Stats.Core.Fans;` at the top.

- [ ] **Step 4: Run** `dotnet test --nologo` → all PASS, 0 warnings.
- [ ] **Step 5: Commit**
```bash
git add src/Stats.Core/Settings/FanChannelPref.cs src/Stats.Core/Settings/AppSettings.cs src/Stats.Core/Settings/SettingsService.cs src/Stats.Core/Fans/FanCurve.cs tests/Stats.Core.Tests/SettingsServiceTests.cs
git commit -m "feat(core): fan control settings model (FanMode, FanPoint, FanChannelPref) with load sanitation"
```

---

### Task 2: `FanCurve` tests (implementation landed in Task 1)

**Files:**
- Test: `tests/Stats.Core.Tests/FanCurveTests.cs`
- (Already created) `src/Stats.Core/Fans/FanCurve.cs`

**Interfaces:**
- Produces: `sealed class FanCurve { static IReadOnlyList<FanPoint> DefaultPoints; static FanCurve Default; IReadOnlyList<FanPoint> Points; static bool TryCreate(IEnumerable<FanPoint>? points, out FanCurve? curve); float Evaluate(float tempC); const int MinPoints=2, MaxPoints=8; }`

- [ ] **Step 1: Write the tests**

```csharp
using Stats.Core.Fans;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class FanCurveTests
{
    private static FanCurve Make(params (float T, float P)[] pts)
    {
        Assert.True(FanCurve.TryCreate(pts.Select(p => new FanPoint(p.T, p.P)), out var c));
        return c!;
    }

    [Fact]
    public void Default_IsValid_AndMatchesSpec()
    {
        Assert.Equal(new[] { new FanPoint(30, 30), new FanPoint(50, 45), new FanPoint(70, 75), new FanPoint(85, 100) }, FanCurve.DefaultPoints);
        Assert.Equal(FanCurve.DefaultPoints, FanCurve.Default.Points);
    }

    [Theory]
    [InlineData(30f, 30f)]   // exact first point
    [InlineData(40f, 37.5f)] // halfway 30→50 : 30→45
    [InlineData(70f, 75f)]   // exact middle point
    [InlineData(80f, 91.666664f)] // 70→85 : 75→100, 2/3 of the way
    [InlineData(85f, 100f)]
    public void Evaluate_InterpolatesLinearly(float temp, float expected) =>
        Assert.Equal(expected, FanCurve.Default.Evaluate(temp), 3);

    [Fact]
    public void Evaluate_FlatBeyondEnds()
    {
        Assert.Equal(30f, FanCurve.Default.Evaluate(-10f));
        Assert.Equal(100f, FanCurve.Default.Evaluate(150f));
    }

    [Fact]
    public void TryCreate_SortsByTemperature()
    {
        var c = Make((70, 75), (30, 30));
        Assert.Equal(30f, c.Points[0].TempC);
        Assert.Equal(52.5f, c.Evaluate(50f), 3);
    }

    [Fact]
    public void TryCreate_RejectsTooFewTooManyNull()
    {
        Assert.False(FanCurve.TryCreate(null, out _));
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(50, 50) }, out _));
        Assert.False(FanCurve.TryCreate(Enumerable.Range(0, 9).Select(i => new FanPoint(10 + i * 10, 50)), out _));
        Assert.True(FanCurve.TryCreate(Enumerable.Range(0, 8).Select(i => new FanPoint(10 + i * 10, 50)), out _));
    }

    [Theory]
    [InlineData(-1f, 50f)] [InlineData(121f, 50f)] [InlineData(50f, -1f)] [InlineData(50f, 101f)] [InlineData(float.NaN, 50f)]
    public void TryCreate_RejectsOutOfRange(float t, float p) =>
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(20, 20), new FanPoint(t, p) }, out _));

    [Fact]
    public void TryCreate_RejectsDuplicateTemperaturesWithinHalfDegree()
    {
        Assert.False(FanCurve.TryCreate(new[] { new FanPoint(50, 20), new FanPoint(50.4f, 60) }, out _));
        Assert.True(FanCurve.TryCreate(new[] { new FanPoint(50, 20), new FanPoint(50.6f, 60) }, out _));
    }
}
```

- [ ] **Step 2: Run** — if Task 1 pasted the implementation correctly these pass immediately; if anything fails, fix `FanCurve.cs` to match.

- [ ] **Step 3: Implementation (reference — this is what Task 1 pasted)**

`src/Stats.Core/Fans/FanCurve.cs`:
```csharp
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>Immutable, validated temperature→percent curve. Linear between points, flat beyond the ends.</summary>
public sealed class FanCurve
{
    public const int MinPoints = 2;
    public const int MaxPoints = 8;
    public const float MinTemp = 0f, MaxTemp = 120f;
    private const float DuplicateTempTolerance = 0.5f;

    public static IReadOnlyList<FanPoint> DefaultPoints { get; } =
        new[] { new FanPoint(30, 30), new FanPoint(50, 45), new FanPoint(70, 75), new FanPoint(85, 100) };
    public static FanCurve Default { get; } = new(DefaultPoints);

    private FanCurve(IReadOnlyList<FanPoint> sortedPoints) => Points = sortedPoints;

    public IReadOnlyList<FanPoint> Points { get; }

    public static bool TryCreate(IEnumerable<FanPoint>? points, out FanCurve? curve)
    {
        curve = null;
        if (points is null) return false;
        var list = points.ToList();
        if (list.Count < MinPoints || list.Count > MaxPoints) return false;
        foreach (var p in list)
        {
            if (float.IsNaN(p.TempC) || float.IsNaN(p.Percent)) return false;
            if (p.TempC < MinTemp || p.TempC > MaxTemp || p.Percent < 0f || p.Percent > 100f) return false;
        }
        list.Sort((a, b) => a.TempC.CompareTo(b.TempC));
        for (int i = 1; i < list.Count; i++)
            if (list[i].TempC - list[i - 1].TempC < DuplicateTempTolerance) return false;
        curve = new FanCurve(list);
        return true;
    }

    public float Evaluate(float tempC)
    {
        var pts = Points;
        if (tempC <= pts[0].TempC) return pts[0].Percent;
        if (tempC >= pts[^1].TempC) return pts[^1].Percent;
        for (int i = 1; i < pts.Count; i++)
        {
            if (tempC <= pts[i].TempC)
            {
                var a = pts[i - 1]; var b = pts[i];
                float f = (tempC - a.TempC) / (b.TempC - a.TempC);
                return a.Percent + f * (b.Percent - a.Percent);
            }
        }
        return pts[^1].Percent;
    }
}
```

- [ ] **Step 4: Run** `dotnet test --nologo` → PASS, 0 warnings.
- [ ] **Step 5: Commit**
```bash
git add tests/Stats.Core.Tests/FanCurveTests.cs src/Stats.Core/Fans/FanCurve.cs
git commit -m "test(core): FanCurve interpolation and validation"
```

---

### Task 3: `MetricGroup.Motherboard` / `Cooler` + `SensorMapper`

**Files:**
- Modify: `src/Stats.Core/Metrics/MetricGroup.cs`
- Modify: `src/Stats.Core/Sensors/SensorMapper.cs:27-35`
- Modify: `src/Stats.Core/ViewModels/DashboardViewModel.cs:12-13` (GroupOrder)
- Modify: `tests/Stats.Core.Tests/SensorMapperTests.cs:30-33` (replace `Map_MotherboardSensor_ReturnsNull`)
- Modify: `tests/Stats.Core.Tests/FrameMetricsTests.cs` (`Game_IsLastEnumMember…` → ordinal-stability test)

- [ ] **Step 1: Tests**

Replace `Map_MotherboardSensor_ReturnsNull` with:
```csharp
    [Theory]
    [InlineData("Motherboard", "Gigabyte B850 GAMING WIFI6", "Temperature", "System", MetricGroup.Motherboard, "°C")]
    [InlineData("SuperIO", "ITE IT8696E", "Fan", "Fan #1", MetricGroup.Motherboard, "RPM")]
    [InlineData("SuperIO", "ITE IT8696E", "Control", "Fan #1", MetricGroup.Motherboard, "%")]
    [InlineData("Cooler", "MSI CoreLiquid S360", "Temperature", "Liquid Temperature", MetricGroup.Cooler, "°C")]
    [InlineData("Cooler", "MSI CoreLiquid S360", "Fan", "Pump", MetricGroup.Cooler, "RPM")]
    public void Map_MotherboardAndCooler_MapToNewGroups(string hwType, string hwName, string sType, string sName, MetricGroup group, string unit)
    {
        var def = SensorMapper.Map(new RawSensor(hwType, hwName, sType, sName));
        Assert.NotNull(def);
        Assert.Equal(group, def!.Group);
        Assert.Equal(unit, def.Unit);
        Assert.Equal(sName, def.DisplayName); // single-instance: no hardware prefix
    }

    [Fact]
    public void Map_SuperIoFanAndControl_HaveDistinctIds()
    {
        var fan = SensorMapper.Map(new RawSensor("SuperIO", "ITE IT8696E", "Fan", "Fan #1"))!;
        var ctl = SensorMapper.Map(new RawSensor("SuperIO", "ITE IT8696E", "Control", "Fan #1"))!;
        Assert.NotEqual(fan.Id, ctl.Id);
        Assert.Equal("motherboard.ite-it8696e.fan.fan-1", fan.Id);
        Assert.Equal("motherboard.ite-it8696e.control.fan-1", ctl.Id);
    }
```
In `FrameMetricsTests`, replace `Game_IsLastEnumMember_SoSerializedRulesStayStable` with:
```csharp
    [Fact]
    public void MetricGroup_ExistingOrdinalsAreStable()
    {
        // Append-only enum: these values are what older settings files (if ever written numerically) mean.
        Assert.Equal(0, (int)MetricGroup.Cpu);
        Assert.Equal(4, (int)MetricGroup.Network);
        Assert.Equal(5, (int)MetricGroup.Game);
        Assert.Equal(6, (int)MetricGroup.Motherboard);
        Assert.Equal(7, (int)MetricGroup.Cooler);
    }
```
Also in `FrameMetricsTests.DashboardViewModel_PlacesGameSectionLast` keep as is (Game is still after Network; the Motherboard/Cooler sections come after Game — the test only uses Cpu/Network/Game).

- [ ] **Step 2: Run** → fails (enum members missing).
- [ ] **Step 3: Implement**

`MetricGroup.cs`: `public enum MetricGroup { Cpu, Gpu, Memory, Storage, Network, Game, Motherboard, Cooler }`

`SensorMapper.Map` hardware switch:
```csharp
        MetricGroup? group = raw.HardwareType switch
        {
            "Cpu" => MetricGroup.Cpu,
            "GpuNvidia" or "GpuAmd" or "GpuIntel" => MetricGroup.Gpu,
            "Memory" => MetricGroup.Memory,
            "Storage" => MetricGroup.Storage,
            "Network" => MetricGroup.Network,
            "Motherboard" or "SuperIO" => MetricGroup.Motherboard,
            "Cooler" => MetricGroup.Cooler,
            _ => null,
        };
```
(`multiInstance` stays `Gpu or Storage or Network`.)

`DashboardViewModel.GroupOrder`:
```csharp
    private static readonly MetricGroup[] GroupOrder =
        { MetricGroup.Cpu, MetricGroup.Gpu, MetricGroup.Memory, MetricGroup.Storage, MetricGroup.Network, MetricGroup.Game, MetricGroup.Motherboard, MetricGroup.Cooler };
```

- [ ] **Step 4: Run** `dotnet test --nologo` → PASS, 0 warnings.
- [ ] **Step 5: Commit**
```bash
git add src/Stats.Core/Metrics/MetricGroup.cs src/Stats.Core/Sensors/SensorMapper.cs src/Stats.Core/ViewModels/DashboardViewModel.cs tests/Stats.Core.Tests/SensorMapperTests.cs tests/Stats.Core.Tests/FrameMetricsTests.cs
git commit -m "feat(core): Motherboard and Cooler metric groups; map SuperIO/Cooler sensors"
```

---

### Task 4: `IFanControlBackend` — LHM implementation, composite forwarding, perf-counter stub

**Files:**
- Create: `src/Stats.Core/Fans/IFanControlBackend.cs`
- Modify: `src/Stats.Core/Sensors/LhmSensorReader.cs` (whole file shown below)
- Modify: `src/Stats.Core/Sensors/CompositeSensorReader.cs`
- Modify: `src/Stats.Core/Sensors/PerfCounterSensorReader.cs` (implement with zero channels)
- Test: `tests/Stats.Core.Tests/CompositeSensorReaderTests.cs` (append)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record FanChannel(string Id, string Name, string Device, string? RpmMetricId, string? PercentMetricId, float MinPercent, float MaxPercent);
  public interface IFanControlBackend {
      IReadOnlyList<FanChannel> Channels { get; }
      void SetPercent(string channelId, float percent);  // poll thread only; throws if unknown id
      void SetAuto(string channelId);                    // poll thread only; no-op if unknown id
  }
  ```
  `LhmSensorReader`, `PerfCounterSensorReader`, `CompositeSensorReader` all implement it.

- [ ] **Step 1: Tests** (append to `CompositeSensorReaderTests`; make `Fake` implement `IFanControlBackend` with configurable channels and recorded calls)

Add to the `Fake` class:
```csharp
        public List<FanChannel> FanChannels = new();
        public List<(string Id, float? Pct)> FanWrites = new();   // Pct null = SetAuto
        public IReadOnlyList<FanChannel> Channels => FanChannels;
        public void SetPercent(string channelId, float percent) => FanWrites.Add((channelId, percent));
        public void SetAuto(string channelId) => FanWrites.Add((channelId, null));
```
and `: ISensorReader, IFanControlBackend` on its declaration, plus `using Stats.Core.Fans;`. New tests:
```csharp
    [Fact]
    public void FanBackend_ForwardsToFirstReaderWithChannels()
    {
        var a = new Fake("A", false);
        var b = new Fake("B", false) { FanChannels = { new FanChannel("/x/control/0", "Fan #1", "ITE", null, null, 0, 100) } };
        var c = new CompositeSensorReader(a, b);
        Assert.Single(c.Channels);
        c.SetPercent("/x/control/0", 40);
        c.SetAuto("/x/control/0");
        Assert.Equal(new (string, float?)[] { ("/x/control/0", 40f), ("/x/control/0", null) }, b.FanWrites);
        Assert.Empty(a.FanWrites);
    }

    [Fact]
    public void FanBackend_NoReaderHasChannels_EmptyAndWritesAreNoOps()
    {
        var c = new CompositeSensorReader(new Fake("A", false));
        Assert.Empty(c.Channels);
        c.SetPercent("nope", 50); // must not throw
        c.SetAuto("nope");
    }
```

- [ ] **Step 2: Run** → build error.
- [ ] **Step 3: Implement**

`src/Stats.Core/Fans/IFanControlBackend.cs`:
```csharp
namespace Stats.Core.Fans;

/// <summary>One controllable fan output. Id is the LHM control identifier (stable across runs).</summary>
public sealed record FanChannel(
    string Id, string Name, string Device,
    string? RpmMetricId, string? PercentMetricId,
    float MinPercent, float MaxPercent);

/// <summary>Writes fan speeds. All members are poll-thread only (LHM is not thread-safe).</summary>
public interface IFanControlBackend
{
    IReadOnlyList<FanChannel> Channels { get; }
    /// <exception cref="KeyNotFoundException">Unknown channel id.</exception>
    void SetPercent(string channelId, float percent);
    void SetAuto(string channelId);
}
```

`src/Stats.Core/Sensors/LhmSensorReader.cs` (complete file):
```csharp
using LibreHardwareMonitor.Hardware;
using Stats.Core.Fans;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Wraps LibreHardwareMonitor. CPU temps/clocks/power need the PawnIO kernel driver (installed separately) plus admin.
/// Also the fan-control backend: every sensor exposing an IControl becomes a FanChannel.</summary>
public sealed class LhmSensorReader : ISensorReader, IFanControlBackend
{
    private readonly Computer _computer;
    private readonly List<(ISensor Sensor, string MetricId)> _map = new();
    private List<MetricDefinition> _definitions = new();
    private readonly Dictionary<string, IControl> _controls = new();
    private List<FanChannel> _channels = new();

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsMotherboardEnabled = true,   // Super-I/O: board temps, fan headers + their PWM controls
            IsControllerEnabled = true,    // USB/HID fan & AIO controllers (e.g. MSI CoreLiquid)
        };
        _computer.Open();
    }

    public string Name => "LibreHardwareMonitor";
    // LHM 0.9.6 reads CPU MSR/SMN only through PawnIO; without it the sensors exist but read 0.
    public bool IsDegraded => !PawnIo.IsInstalled;

    public IReadOnlyList<FanChannel> Channels => _channels;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        UpdateAll();

        var sensors = new List<ISensor>();
        foreach (var hardware in _computer.Hardware)
            Collect(hardware, sensors);

        var raws = sensors
            .Select(s => new RawSensor(
                s.Hardware.HardwareType.ToString(),
                s.Hardware.Name,
                s.SensorType.ToString(),
                s.Name))
            .ToList();

        var defs = SensorMapper.MapAll(raws);

        _map.Clear();
        _definitions = new List<MetricDefinition>();
        var idOf = new Dictionary<ISensor, string>();
        for (int i = 0; i < sensors.Count; i++)
        {
            if (defs[i] is not MetricDefinition def) continue;
            _map.Add((sensors[i], def.Id));
            _definitions.Add(def);
            idOf[sensors[i]] = def.Id;
        }

        DiscoverChannels(sensors, idOf);
        return _definitions;
    }

    /// <summary>Control-type sensors (ITE, NVIDIA) pair with the Fan sensor of the same hardware+index;
    /// Fan-type sensors that carry a control themselves (USB coolers) are their own RPM source.</summary>
    private void DiscoverChannels(List<ISensor> sensors, Dictionary<ISensor, string> idOf)
    {
        _controls.Clear();
        var channels = new List<FanChannel>();
        foreach (var s in sensors)
        {
            if (s.Control is not IControl ctl) continue;
            string id = s.Identifier.ToString();
            if (_controls.ContainsKey(id)) continue;
            ISensor? rpmSensor = s.SensorType == SensorType.Fan
                ? s
                : s.Hardware.Sensors.FirstOrDefault(o => o.SensorType == SensorType.Fan && o.Index == s.Index);
            ISensor? pctSensor = s.SensorType == SensorType.Control ? s : null;
            _controls[id] = ctl;
            channels.Add(new FanChannel(
                Id: id,
                Name: s.Name,
                Device: s.Hardware.Name,
                RpmMetricId: rpmSensor is not null && idOf.TryGetValue(rpmSensor, out var rid) ? rid : null,
                PercentMetricId: pctSensor is not null && idOf.TryGetValue(pctSensor, out var pid) ? pid : null,
                MinPercent: ctl.MinSoftwareValue,
                MaxPercent: ctl.MaxSoftwareValue));
        }
        _channels = channels;
    }

    public SensorSnapshot Read()
    {
        UpdateAll();
        var values = new Dictionary<string, float?>(_map.Count);
        foreach (var (sensor, id) in _map)
            values[id] = sensor.Value;
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    public void SetPercent(string channelId, float percent)
    {
        var ctl = _controls[channelId];
        ctl.SetSoftware(Math.Clamp(percent, ctl.MinSoftwareValue, ctl.MaxSoftwareValue));
    }

    public void SetAuto(string channelId)
    {
        if (_controls.TryGetValue(channelId, out var ctl)) ctl.SetDefault();
    }

    private void UpdateAll()
    {
        foreach (var hardware in _computer.Hardware)
            UpdateRecursive(hardware);
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private static void Collect(IHardware hardware, List<ISensor> acc)
    {
        acc.AddRange(hardware.Sensors);
        foreach (var sub in hardware.SubHardware)
            Collect(sub, acc);
    }

    public void Dispose() => _computer.Close();
}
```
(Check the existing file's top for a `PawnIo` helper reference and keep any lines this listing doesn't show — e.g. if `PawnIo` lives in another file, nothing changes; if the existing `using`s differ, keep what compiles.)

`PerfCounterSensorReader`: add `IFanControlBackend` to the class declaration, `using Stats.Core.Fans;`, and
```csharp
    public IReadOnlyList<FanChannel> Channels => Array.Empty<FanChannel>();
    public void SetPercent(string channelId, float percent) => throw new KeyNotFoundException(channelId);
    public void SetAuto(string channelId) { }
```

`CompositeSensorReader`: add `IFanControlBackend` to the declaration, `using Stats.Core.Fans;`, and
```csharp
    private IFanControlBackend? FanBackend => _readers.OfType<IFanControlBackend>().FirstOrDefault(b => b.Channels.Count > 0);
    public IReadOnlyList<FanChannel> Channels => FanBackend?.Channels ?? Array.Empty<FanChannel>();
    public void SetPercent(string channelId, float percent) => FanBackend?.SetPercent(channelId, percent);
    public void SetAuto(string channelId) => FanBackend?.SetAuto(channelId);
```

- [ ] **Step 4: Run** `dotnet build --nologo && dotnet test --nologo` → PASS, 0 warnings.
- [ ] **Step 5: Commit**
```bash
git add src/Stats.Core/Fans/IFanControlBackend.cs src/Stats.Core/Sensors/LhmSensorReader.cs src/Stats.Core/Sensors/CompositeSensorReader.cs src/Stats.Core/Sensors/PerfCounterSensorReader.cs tests/Stats.Core.Tests/CompositeSensorReaderTests.cs
git commit -m "feat(core): IFanControlBackend — LHM control channels (motherboard/controller enabled), composite forwarding"
```

---

### Task 5: `FanController`

**Files:**
- Create: `src/Stats.Core/Fans/FanChannelView.cs`
- Create: `src/Stats.Core/Fans/FanController.cs`
- Test: `tests/Stats.Core.Tests/FanControllerTests.cs`

**Interfaces:**
- Consumes: `IFanControlBackend`, `FanChannel`, `FanCurve`, `FanChannelPref`, `FanMode`, `FanPoint`, `AppSettings`, `SensorSnapshot`.
- Produces:
  ```csharp
  public enum FanChannelStatus { Idle, Active, WaitingForSource, SourceUnavailable, WriteFailed }
  public sealed record FanChannelView(string Id, string Name, string Device, FanMode Mode, float? Rpm, float? Percent,
      float? TargetPercent, float? SourceTemp, FanChannelStatus Status, float MinPercent, float MaxPercent,
      string? SourceMetricId, float ManualPercent, IReadOnlyList<FanPoint> Points);
  public sealed class FanController {
      FanController(IFanControlBackend backend, AppSettings settings, Action saveSettings);
      const float HysteresisC = 2f, MaxStepPerTick = 10f, PumpFloorPercent = 50f; static readonly TimeSpan SourceStaleAfter = 10 s; const int MaxWriteFailures = 3;
      bool Enabled { get; set; }                      // ↔ settings.FanControlEnabled; saves
      IReadOnlyList<FanChannel> Channels { get; }
      void SetMode(string id, FanMode m); void SetManualPercent(string id, float p); void SetSource(string id, string? metricId);
      bool TrySetPoints(string id, IEnumerable<FanPoint> points); void SetName(string id, string? name); void ResetCurve(string id);
      void Tick(SensorSnapshot snapshot, DateTime nowUtc);   // poll thread
      void RestoreAll();                                      // after poller stopped
      IReadOnlyList<FanChannelView> Views();
  }
  ```

- [ ] **Step 1: Tests**

`tests/Stats.Core.Tests/FanControllerTests.cs`:
```csharp
using Stats.Core.Fans;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class FanControllerTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new();
        public List<(string Id, float? Pct)> Writes = new();
        public Func<string, bool>? FailWrite;
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p) { if (FailWrite?.Invoke(id) == true) throw new InvalidOperationException("io"); Writes.Add((id, p)); }
        public void SetAuto(string id) => Writes.Add((id, null));
    }

    private const string Case = "/lpc/it8696e/0/control/0";
    private const string Gpu = "/gpu-nvidia/0/control/1";
    private const string Pump = "/usbhid/0/fan/14";
    private const string Cpu = "cpu.amd.temperature.tctl";
    private const string CaseRpm = "motherboard.ite.fan.fan-1";
    private static readonly DateTime T0 = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    private sealed class H
    {
        public FakeBackend B = new();
        public AppSettings S = new() { FanControlEnabled = true };
        public int Saves;
        public FanController C;
        public H()
        {
            B.Chans.Add(new FanChannel(Case, "Fan #1", "ITE IT8696E", CaseRpm, null, 0, 100));
            B.Chans.Add(new FanChannel(Gpu, "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
            B.Chans.Add(new FanChannel(Pump, "Pump", "MSI CoreLiquid S360", null, null, 0, 100));
            C = new FanController(B, S, () => Saves++);
        }
        public SensorSnapshot Snap(float? cpu, float? rpm = 1200) => new(new Dictionary<string, float?> { [Cpu] = cpu, [CaseRpm] = rpm }, T0);
        public void Tick(float? cpu, int secondsFromT0 = 0) => C.Tick(Snap(cpu), T0.AddSeconds(secondsFromT0));
        public IEnumerable<(string, float?)> WritesFor(string id) => B.Writes.Where(w => w.Id == id).Select(w => (w.Id, w.Pct));
    }

    // 1 %/°C from (30,0) to (90,60): easy arithmetic.
    private static readonly FanPoint[] Linear = { new(30, 0), new(90, 60) };

    [Fact]
    public void Disabled_NeverWrites_AndRestoresWhatWasInSoftware()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));
        h.C.Enabled = false;
        Assert.False(h.S.FanControlEnabled);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
        h.Tick(50);
        Assert.Equal(2, h.WritesFor(Case).Count()); // nothing more while disabled
    }

    [Fact]
    public void Auto_NoWrites_WhenNeverInSoftware()
    {
        var h = new H();
        h.Tick(50); h.Tick(60);
        Assert.Empty(h.B.Writes);
    }

    [Fact]
    public void Manual_WritesOnce_ThenOnlyOnChange()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));
        h.C.SetManualPercent(Case, 45);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, 45f) }, h.WritesFor(Case));
    }

    [Fact]
    public void Curve_FollowsSource_WithTwoDegreeHysteresis()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); Assert.True(h.C.TrySetPoints(Case, Linear));
        h.Tick(50);            // 20 %
        h.Tick(51.9f);         // < 2 °C change → no write
        Assert.Equal(new (string, float?)[] { (Case, 20f) }, h.WritesFor(Case));
        h.Tick(52f);           // 2 °C → 22 %
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, 22f) }, h.WritesFor(Case));
        h.Tick(50.1f);         // 1.9 below 52 → no write
        Assert.Equal(2, h.WritesFor(Case).Count());
    }

    [Fact]
    public void SlewLimit_TenPointsPerTick()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 0);
        h.Tick(50);                                   // first write immediate: 0
        h.C.SetManualPercent(Case, 100);
        for (int i = 0; i < 12; i++) h.Tick(50);
        var w = h.WritesFor(Case).Select(x => x.Item2).ToList();
        Assert.Equal(new float?[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }, w);
    }

    [Fact]
    public void Curve_NoSourceValueYet_WaitsThenFailsSafeToAutoAfterTenSeconds()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(null, 0); h.Tick(null, 5);
        Assert.Empty(h.B.Writes);
        Assert.Equal(FanChannelStatus.WaitingForSource, h.C.Views().Single(v => v.Id == Case).Status);
        h.Tick(null, 11);
        Assert.Empty(h.B.Writes);                     // never in software → nothing to restore
        Assert.Equal(FanChannelStatus.SourceUnavailable, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void Curve_SourceGoesStale_RevertsToAuto_ThenRecovers()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(50, 0);                                // 20 %
        h.Tick(null, 5);                              // stale < 10 s: hold
        h.Tick(null, 9);
        Assert.Equal(new (string, float?)[] { (Case, 20f) }, h.WritesFor(Case));
        h.Tick(null, 11);                             // > 10 s → SetAuto
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, null) }, h.WritesFor(Case));
        Assert.Equal(FanChannelStatus.SourceUnavailable, h.C.Views().Single(v => v.Id == Case).Status);
        h.Tick(70, 12);                               // source back → 40 %, immediate (lastWritten cleared on SetAuto)
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
        Assert.Equal(FanChannelStatus.Active, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void Floors_GpuMin30_PumpMin50()
    {
        var h = new H();
        h.C.SetMode(Gpu, FanMode.Manual); h.C.SetManualPercent(Gpu, 10);
        h.C.SetMode(Pump, FanMode.Manual); h.C.SetManualPercent(Pump, 10);
        h.Tick(50);
        Assert.Equal((Gpu, 30f), h.WritesFor(Gpu).Single());
        Assert.Equal((Pump, 50f), h.WritesFor(Pump).Single());
    }

    [Fact]
    public void ModeChange_CurveToAuto_EmitsSetAutoOnce()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(50);
        h.C.SetMode(Case, FanMode.Auto);
        h.Tick(50); h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, null) }, h.WritesFor(Case));
    }

    [Fact]
    public void WriteFailures_ThreeInARow_ChannelGoesAuto()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);
        h.Tick(50);
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status); // status kept for the user
        h.Tick(50);
        Assert.Empty(h.WritesFor(Case)); // all attempts threw; SetAuto not needed (never in software)
    }

    [Fact]
    public void RestoreAll_OnlyTouchedChannels()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        h.C.RestoreAll();
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
        Assert.Empty(h.WritesFor(Gpu));
        h.Tick(50); // next tick after restore re-applies Manual (still enabled)
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
    }

    [Fact]
    public void Views_ReflectRpmPercentTargetAndSettings()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear); h.C.SetName(Case, "Front");
        h.Tick(50);
        var v = h.C.Views().Single(x => x.Id == Case);
        Assert.Equal("Front", v.Name);
        Assert.Equal(1200f, v.Rpm);
        Assert.Equal(20f, v.TargetPercent);
        Assert.Equal(20f, v.Percent);           // no PercentMetricId → last written
        Assert.Equal(50f, v.SourceTemp);
        Assert.Equal(FanMode.Curve, v.Mode);
        Assert.Equal(Cpu, v.SourceMetricId);
        Assert.Equal(Linear, v.Points);
        var g = h.C.Views().Single(x => x.Id == Gpu);
        Assert.Equal("GPU Fan 1", g.Name);       // no pref → hardware name, Auto, Idle
        Assert.Equal(FanMode.Auto, g.Mode);
        Assert.Equal(FanChannelStatus.Idle, g.Status);
    }

    [Fact]
    public void Setters_PersistAndSave_InvalidPointsRejected()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual);
        h.C.SetManualPercent(Case, 33);
        h.C.SetSource(Case, Cpu);
        Assert.False(h.C.TrySetPoints(Case, new[] { new FanPoint(50, 50) }));
        Assert.True(h.C.TrySetPoints(Case, Linear));
        h.C.ResetCurve(Case);
        var p = h.S.FanChannels[Case];
        Assert.Equal(FanMode.Manual, p.Mode);
        Assert.Equal(33f, p.ManualPercent);
        Assert.Equal(Cpu, p.SourceMetricId);
        Assert.Equal(FanCurve.DefaultPoints, p.Points);
        Assert.Equal(5, h.Saves); // SetMode, SetManualPercent, SetSource, TrySetPoints(valid), ResetCurve
    }
}
```

- [ ] **Step 2: Run** → build error.
- [ ] **Step 3: Implement**

`src/Stats.Core/Fans/FanChannelView.cs`:
```csharp
using Stats.Core.Settings;

namespace Stats.Core.Fans;

public enum FanChannelStatus { Idle, Active, WaitingForSource, SourceUnavailable, WriteFailed }

/// <summary>Read-only snapshot of one channel for the UI.</summary>
public sealed record FanChannelView(
    string Id, string Name, string Device, FanMode Mode,
    float? Rpm, float? Percent, float? TargetPercent, float? SourceTemp,
    FanChannelStatus Status, float MinPercent, float MaxPercent,
    string? SourceMetricId, float ManualPercent, IReadOnlyList<FanPoint> Points);
```

`src/Stats.Core/Fans/FanController.cs`:
```csharp
using System.Diagnostics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>
/// The fan control loop. Tick() runs on the poller thread after each snapshot (the only thread that may touch
/// the backend). UI-thread setters only change desired state (settings) under _gate; the next Tick applies it.
/// </summary>
public sealed class FanController
{
    public const float HysteresisC = 2f;
    public const float MaxStepPerTick = 10f;
    public const float PumpFloorPercent = 50f;
    public const int MaxWriteFailures = 3;
    public static readonly TimeSpan SourceStaleAfter = TimeSpan.FromSeconds(10);

    private sealed class Runtime
    {
        public bool InSoftware;
        public float? LastWritten;
        public float? LastSourceUsed;
        public DateTime? LastSourceSeen;
        public int Failures;
        public FanChannelStatus Status = FanChannelStatus.Idle;
        public float? Rpm, Percent, Target, SourceTemp;
    }

    private readonly IFanControlBackend _backend;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly object _gate = new();
    private readonly Dictionary<string, Runtime> _rt = new();
    private DateTime? _firstTick;

    public FanController(IFanControlBackend backend, AppSettings settings, Action saveSettings)
    {
        _backend = backend;
        _settings = settings;
        _save = saveSettings;
    }

    public IReadOnlyList<FanChannel> Channels => _backend.Channels;

    public bool Enabled
    {
        get { lock (_gate) return _settings.FanControlEnabled; }
        set { lock (_gate) { if (_settings.FanControlEnabled == value) return; _settings.FanControlEnabled = value; } _save(); }
    }

    // ---- desired-state setters (any thread) ----

    public void SetMode(string id, FanMode mode) => Mutate(id, p => p.Mode = mode);
    public void SetManualPercent(string id, float percent) => Mutate(id, p => p.ManualPercent = Math.Clamp(percent, 0f, 100f));
    public void SetSource(string id, string? metricId) => Mutate(id, p => p.SourceMetricId = string.IsNullOrWhiteSpace(metricId) ? null : metricId);
    public void SetName(string id, string? name) => Mutate(id, p => p.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
    public void ResetCurve(string id) => Mutate(id, p => p.Points = FanCurve.DefaultPoints.ToList());

    public bool TrySetPoints(string id, IEnumerable<FanPoint> points)
    {
        if (!FanCurve.TryCreate(points, out var curve)) return false;
        Mutate(id, p => p.Points = curve!.Points.ToList());
        return true;
    }

    private void Mutate(string id, Action<FanChannelPref> change)
    {
        lock (_gate)
        {
            if (!_settings.FanChannels.TryGetValue(id, out var pref))
                _settings.FanChannels[id] = pref = new FanChannelPref();
            change(pref);
            Rt(id).LastSourceUsed = null; // re-evaluate immediately on next tick
        }
        _save();
    }

    // ---- loop (poll thread) ----

    public void Tick(SensorSnapshot snapshot, DateTime nowUtc)
    {
        lock (_gate)
        {
            _firstTick ??= nowUtc;
            foreach (var ch in _backend.Channels)
            {
                var rt = Rt(ch.Id);
                rt.Rpm = Value(snapshot, ch.RpmMetricId);
                float? reported = Value(snapshot, ch.PercentMetricId);

                if (!_settings.FanControlEnabled)
                {
                    ReleaseLocked(ch, rt, FanChannelStatus.Idle);
                    rt.Percent = reported; rt.Target = null; rt.SourceTemp = null;
                    continue;
                }

                _settings.FanChannels.TryGetValue(ch.Id, out var pref);
                var mode = pref?.Mode ?? FanMode.Auto;
                float? target = null;
                rt.SourceTemp = null;

                switch (mode)
                {
                    case FanMode.Auto:
                        ReleaseLocked(ch, rt, FanChannelStatus.Idle);
                        break;

                    case FanMode.Manual:
                        target = pref!.ManualPercent;
                        break;

                    case FanMode.Curve:
                        float? src = Value(snapshot, pref!.SourceMetricId);
                        rt.SourceTemp = src;
                        if (src is float t)
                        {
                            rt.LastSourceSeen = nowUtc;
                            if (rt.LastSourceUsed is not float used || MathF.Abs(t - used) >= HysteresisC)
                                rt.LastSourceUsed = t;
                        }
                        var lastSeen = rt.LastSourceSeen ?? _firstTick.Value;
                        if (src is null && nowUtc - lastSeen > SourceStaleAfter)
                        {
                            ReleaseLocked(ch, rt, FanChannelStatus.SourceUnavailable);
                            rt.LastSourceUsed = null;
                            break;
                        }
                        if (rt.LastSourceUsed is not float useTemp)
                        {
                            if (rt.Status != FanChannelStatus.WriteFailed) rt.Status = FanChannelStatus.WaitingForSource;
                            break; // no value yet (or holding through a short gap): keep current output
                        }
                        if (!FanCurve.TryCreate(pref.Points, out var curve)) curve = FanCurve.Default;
                        target = curve!.Evaluate(useTemp);
                        break;
                }

                if (target is float want)
                {
                    float min = ch.MinPercent;
                    if (ch.Name.Contains("pump", StringComparison.OrdinalIgnoreCase)) min = MathF.Max(min, PumpFloorPercent);
                    want = Math.Clamp(want, min, ch.MaxPercent);
                    if (rt.LastWritten is float last)
                        want = last + Math.Clamp(want - last, -MaxStepPerTick, MaxStepPerTick);
                    want = MathF.Round(want);
                    rt.Target = want;
                    if (!rt.InSoftware || rt.LastWritten != want) WriteLocked(ch, rt, want, pref!);
                    else if (rt.Status != FanChannelStatus.WriteFailed) rt.Status = FanChannelStatus.Active;
                }
                else if (mode != FanMode.Curve || rt.Status == FanChannelStatus.SourceUnavailable || rt.Status == FanChannelStatus.Idle)
                {
                    rt.Target = null;
                }

                rt.Percent = reported ?? (rt.InSoftware ? rt.LastWritten : null);
            }
        }
    }

    private void WriteLocked(FanChannel ch, Runtime rt, float percent, FanChannelPref pref)
    {
        try
        {
            _backend.SetPercent(ch.Id, percent);
            rt.InSoftware = true;
            rt.LastWritten = percent;
            rt.Failures = 0;
            rt.Status = FanChannelStatus.Active;
        }
        catch (Exception ex)
        {
            rt.Failures++;
            rt.Status = FanChannelStatus.WriteFailed;
            Trace.WriteLine($"[Stats.FanController] write {ch.Id}={percent} failed ({rt.Failures}/{MaxWriteFailures}): {ex.Message}");
            if (rt.Failures >= MaxWriteFailures)
            {
                pref.Mode = FanMode.Auto;
                ReleaseLocked(ch, rt, FanChannelStatus.WriteFailed);
                Trace.WriteLine($"[Stats.FanController] {ch.Id} set to Auto after repeated write failures");
            }
        }
    }

    /// <summary>Hands the channel back to device control if we were driving it. Always sets the status.</summary>
    private void ReleaseLocked(FanChannel ch, Runtime rt, FanChannelStatus status)
    {
        if (rt.InSoftware)
        {
            try { _backend.SetAuto(ch.Id); }
            catch (Exception ex) { Trace.WriteLine($"[Stats.FanController] SetAuto {ch.Id} failed: {ex.Message}"); }
            rt.InSoftware = false;
            rt.LastWritten = null;
        }
        rt.Target = null;
        rt.Status = status;
    }

    /// <summary>Return every channel we ever wrote to device control. Call after the poller is stopped (or from Tick's thread).</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach (var ch in _backend.Channels)
                ReleaseLocked(ch, Rt(ch.Id), FanChannelStatus.Idle);
        }
    }

    public IReadOnlyList<FanChannelView> Views()
    {
        lock (_gate)
        {
            var list = new List<FanChannelView>(_backend.Channels.Count);
            foreach (var ch in _backend.Channels)
            {
                var rt = Rt(ch.Id);
                _settings.FanChannels.TryGetValue(ch.Id, out var pref);
                list.Add(new FanChannelView(
                    ch.Id,
                    string.IsNullOrWhiteSpace(pref?.Name) ? ch.Name : pref!.Name!,
                    ch.Device,
                    pref?.Mode ?? FanMode.Auto,
                    rt.Rpm, rt.Percent, rt.Target, rt.SourceTemp, rt.Status,
                    ch.MinPercent, ch.MaxPercent,
                    pref?.SourceMetricId,
                    pref?.ManualPercent ?? 50f,
                    pref?.Points ?? FanCurve.DefaultPoints));
            }
            return list;
        }
    }

    private Runtime Rt(string id)
    {
        if (!_rt.TryGetValue(id, out var rt)) _rt[id] = rt = new Runtime();
        return rt;
    }

    private static float? Value(SensorSnapshot s, string? id) =>
        id is not null && s.Values.TryGetValue(id, out var v) && v is float f && !float.IsNaN(f) ? f : null;
}
```

Notes for the implementer, checked against the tests:
- `Curve_SourceGoesStale…`: at t=11 the source is null and `LastSourceSeen`=0 → `> 10 s` → `ReleaseLocked` (SetAuto) and `LastSourceUsed = null`; at t=12 source 70 → `LastSourceUsed = 70` → target 40, `LastWritten` is null after release so no slew → write 40 ✓.
- `Curve_NoSourceValueYet…`: `LastSourceSeen` null → `lastSeen = _firstTick` (t=0); t=5: not stale → `LastSourceUsed` null → `WaitingForSource`; t=11 → stale → `SourceUnavailable`, nothing written (never in software) ✓.
- `SlewLimit`: first write 0 (LastWritten null → immediate). Then wants 100 → 10, 20, …, 100 (10 writes) then no more ✓ (12 ticks, 11 writes total).
- `WriteFailures`: each failing tick increments; on the 3rd, pref.Mode=Auto + status WriteFailed kept; 4th tick mode Auto → `ReleaseLocked(Idle)` — wait: the test asserts status WriteFailed is *kept* after the 3rd tick, and then on the 4th tick asserts only that no writes happened. `ReleaseLocked` with `InSoftware=false` sets `Status = Idle`, which is fine for the 4th-tick assertion (it only checks writes). ✓
- `Setters_PersistAndSave`: 5 saves — `TrySetPoints` invalid must NOT save ✓ (returns before `Mutate`).
- `Views_Reflect…`: `Percent` = reported (null, no PercentMetricId) ?? LastWritten (20) ✓.

- [ ] **Step 4: Run** `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FanControllerTests" --nologo` → PASS; then full suite, 0 warnings.
- [ ] **Step 5: Commit**
```bash
git add src/Stats.Core/Fans/FanChannelView.cs src/Stats.Core/Fans/FanController.cs tests/Stats.Core.Tests/FanControllerTests.cs
git commit -m "feat(core): FanController — auto/manual/curve loop with hysteresis, slew, floors, failsafe, restore"
```

---

### Task 6: `FansViewModel`

**Files:**
- Create: `src/Stats.Core/ViewModels/FansViewModel.cs`
- Test: `tests/Stats.Core.Tests/FansViewModelTests.cs`

**Interfaces:**
- Consumes: `FanController`, `FanChannelView`, `MetricDefinition`, `AppSettings` (TilePrefs names), `FanMode`, `FanPoint`.
- Produces:
  ```csharp
  public sealed record FanSourceOption(string Id, string Label);
  public sealed partial class FanChannelViewModel : ObservableObject {
      string Id, Device; string Name (editable); string RpmText, PercentText, TargetText, SourceTempText, StatusText;
      FanMode Mode; bool IsManual, IsCurve; float ManualPercent; string? SourceMetricId; ObservableCollection<FanPoint> Points;
      float MinPercent, MaxPercent; double LiveTemp (NaN = none), LiveTarget (NaN = none); IReadOnlyList<FanSourceOption> SourceOptions;
      ICommand ResetCurveCommand; }
  public sealed partial class FanDeviceGroupViewModel { string Device; ObservableCollection<FanChannelViewModel> Channels; }
  public sealed partial class FansViewModel : ObservableObject {
      FansViewModel(FanController controller, IReadOnlyList<MetricDefinition> definitions, AppSettings settings);
      bool Enabled; bool HasChannels; ObservableCollection<FanDeviceGroupViewModel> Devices; void Refresh(); ICommand SetAllAutoCommand; }
  ```

- [ ] **Step 1: Tests**

`tests/Stats.Core.Tests/FansViewModelTests.cs`:
```csharp
using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class FansViewModelTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new();
        public List<(string Id, float? Pct)> Writes = new();
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p) => Writes.Add((id, p));
        public void SetAuto(string id) => Writes.Add((id, null));
    }

    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.tctl", "Core (Tctl/Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("gpu.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("cpu.load", "CPU Total", MetricGroup.Cpu, "Ryzen", "%"),
        new("cooler.liquid", "Liquid Temperature", MetricGroup.Cooler, "MSI CoreLiquid S360", "°C", "F1"),
    };

    private static (FansViewModel Vm, FanController C, FakeBackend B, AppSettings S) Make()
    {
        var b = new FakeBackend();
        b.Chans.Add(new FanChannel("/ite/control/0", "Fan #1", "ITE IT8696E", "mb.fan1", null, 0, 100));
        b.Chans.Add(new FanChannel("/ite/control/1", "Fan #2", "ITE IT8696E", "mb.fan2", null, 0, 100));
        b.Chans.Add(new FanChannel("/gpu/control/1", "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
        var s = new AppSettings();
        s.TilePrefs["cpu.tctl"] = new TilePref { Name = "CPU" };
        var c = new FanController(b, s, () => { });
        return (new FansViewModel(c, Defs, s), c, b, s);
    }

    [Fact]
    public void Devices_GroupedInBackendOrder_WithChannels()
    {
        var (vm, _, _, _) = Make();
        Assert.True(vm.HasChannels);
        Assert.Equal(new[] { "ITE IT8696E", "RTX 5070 Ti" }, vm.Devices.Select(d => d.Device));
        Assert.Equal(new[] { "Fan #1", "Fan #2" }, vm.Devices[0].Channels.Select(c => c.Name));
        Assert.Equal(30f, vm.Devices[1].Channels[0].MinPercent);
    }

    [Fact]
    public void SourceOptions_AreOnlyCelsiusMetrics_WithFriendlyNames()
    {
        var (vm, _, _, _) = Make();
        var opts = vm.Devices[0].Channels[0].SourceOptions;
        Assert.Equal(new[] { "cpu.tctl", "gpu.core", "cooler.liquid" }, opts.Select(o => o.Id));
        Assert.Equal("CPU", opts[0].Label);                                  // TilePref rename wins
        Assert.Equal("Liquid Temperature", opts[2].Label);
    }

    [Fact]
    public void Edits_FlowToController_AndSettings()
    {
        var (vm, c, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Curve;
        ch.SourceMetricId = "cpu.tctl";
        ch.ManualPercent = 66;
        ch.Name = "Front intake";
        Assert.True(ch.IsCurve); Assert.False(ch.IsManual);
        var p = s.FanChannels["/ite/control/0"];
        Assert.Equal(FanMode.Curve, p.Mode);
        Assert.Equal("cpu.tctl", p.SourceMetricId);
        Assert.Equal(66f, p.ManualPercent);
        Assert.Equal("Front intake", p.Name);
        Assert.Equal("Front intake", c.Views()[0].Name);
    }

    [Fact]
    public void Points_ReplaceInCollection_PersistsValidCurve_IgnoresInvalid()
    {
        var (vm, _, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Points[0] = new FanPoint(35, 30);
        Assert.Equal(35f, s.FanChannels["/ite/control/0"].Points[0].TempC);
        ch.Points.Clear();                          // 0 points → invalid → settings untouched
        Assert.Equal(4, s.FanChannels["/ite/control/0"].Points.Count);
    }

    [Fact]
    public void Refresh_ReflectsControllerViews()
    {
        var (vm, c, _, s) = Make();
        s.FanControlEnabled = true;
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Curve; ch.SourceMetricId = "cpu.tctl";
        c.Tick(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.tctl"] = 60f, ["mb.fan1"] = 1450f }, DateTime.UtcNow), DateTime.UtcNow);
        vm.Refresh();
        Assert.Equal("1450 RPM", ch.RpmText);
        Assert.Equal("60 %", ch.PercentText);      // default curve at 60 °C = 60 %
        Assert.Equal("60 %", ch.TargetText);
        Assert.Equal("60.0 °C", ch.SourceTempText);
        Assert.Equal(60.0, ch.LiveTemp, 3);
        Assert.Equal(60.0, ch.LiveTarget, 3);
        Assert.Equal("Active", ch.StatusText);
    }

    [Fact]
    public void Refresh_DoesNotEchoBackIntoController()
    {
        var (vm, c, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Manual;
        int before = s.FanChannels.Count;
        vm.Refresh(); vm.Refresh();
        Assert.Equal(before, s.FanChannels.Count);
        Assert.Equal(FanMode.Manual, c.Views()[0].Mode);
    }

    [Fact]
    public void Enabled_TwoWayWithController()
    {
        var (vm, c, _, s) = Make();
        Assert.False(vm.Enabled);
        vm.Enabled = true;
        Assert.True(c.Enabled); Assert.True(s.FanControlEnabled);
    }

    [Fact]
    public void SetAllAuto_SetsEveryChannelAuto()
    {
        var (vm, c, _, _) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.Devices[1].Channels[0].Mode = FanMode.Curve;
        vm.SetAllAutoCommand.Execute(null);
        Assert.All(c.Views(), v => Assert.Equal(FanMode.Auto, v.Mode));
        Assert.All(vm.Devices.SelectMany(d => d.Channels), ch => Assert.Equal(FanMode.Auto, ch.Mode));
    }

    [Fact]
    public void NoChannels_HasChannelsFalse()
    {
        var c = new FanController(new FakeBackend(), new AppSettings(), () => { });
        var vm = new FansViewModel(c, Defs, new AppSettings());
        Assert.False(vm.HasChannels);
        Assert.Empty(vm.Devices);
    }
}
```

- [ ] **Step 2: Run** → build error.
- [ ] **Step 3: Implement**

`src/Stats.Core/ViewModels/FansViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed record FanSourceOption(string Id, string Label);

/// <summary>One controllable fan row. Setters push desired state to the controller; Refresh pulls live values.</summary>
public sealed partial class FanChannelViewModel : ObservableObject
{
    private readonly FanController _controller;
    private bool _refreshing;

    public FanChannelViewModel(FanChannelView v, FanController controller, IReadOnlyList<FanSourceOption> sourceOptions)
    {
        _controller = controller;
        Id = v.Id;
        Device = v.Device;
        MinPercent = v.MinPercent;
        MaxPercent = v.MaxPercent;
        SourceOptions = sourceOptions;
        _name = v.Name;
        _mode = v.Mode;
        _manualPercent = v.ManualPercent;
        _sourceMetricId = v.SourceMetricId;
        Points = new ObservableCollection<FanPoint>(v.Points);
        Points.CollectionChanged += OnPointsChanged;
        Apply(v);
    }

    public string Id { get; }
    public string Device { get; }
    public float MinPercent { get; }
    public float MaxPercent { get; }
    public IReadOnlyList<FanSourceOption> SourceOptions { get; }
    public ObservableCollection<FanPoint> Points { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private FanMode _mode;
    [ObservableProperty] private float _manualPercent;
    [ObservableProperty] private string? _sourceMetricId;
    [ObservableProperty] private string _rpmText = "—";
    [ObservableProperty] private string _percentText = "—";
    [ObservableProperty] private string _targetText = "—";
    [ObservableProperty] private string _sourceTempText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _liveTemp = double.NaN;
    [ObservableProperty] private double _liveTarget = double.NaN;

    public bool IsManual => Mode == FanMode.Manual;
    public bool IsCurve => Mode == FanMode.Curve;

    partial void OnNameChanged(string value) { if (!_refreshing) _controller.SetName(Id, value); }
    partial void OnModeChanged(FanMode value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsCurve));
        if (!_refreshing) _controller.SetMode(Id, value);
    }
    partial void OnManualPercentChanged(float value) { if (!_refreshing) _controller.SetManualPercent(Id, value); }
    partial void OnSourceMetricIdChanged(string? value) { if (!_refreshing) _controller.SetSource(Id, value); }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _controller.TrySetPoints(Id, Points); // invalid intermediate states (e.g. during clear) are ignored
    }

    [RelayCommand]
    private void ResetCurve()
    {
        _controller.ResetCurve(Id);
        ReplacePoints(FanCurve.DefaultPoints);
    }

    /// <summary>Pull live values (and any controller-side changes, e.g. failsafe → Auto) without echoing back.</summary>
    public void Apply(FanChannelView v)
    {
        _refreshing = true;
        try
        {
            if (Name != v.Name) Name = v.Name;
            if (Mode != v.Mode) Mode = v.Mode;
            if (ManualPercent != v.ManualPercent) ManualPercent = v.ManualPercent;
            if (SourceMetricId != v.SourceMetricId) SourceMetricId = v.SourceMetricId;
            if (!Points.SequenceEqual(v.Points)) ReplacePoints(v.Points);
            RpmText = v.Rpm is float r ? $"{r:F0} RPM" : "—";
            PercentText = v.Percent is float p ? $"{p:F0} %" : "—";
            TargetText = v.TargetPercent is float t ? $"{t:F0} %" : "—";
            SourceTempText = v.SourceTemp is float s ? string.Create(CultureInfo.InvariantCulture, $"{s:F1} °C") : "—";
            LiveTemp = v.SourceTemp ?? double.NaN;
            LiveTarget = v.TargetPercent ?? double.NaN;
            StatusText = v.Status switch
            {
                FanChannelStatus.Idle => v.Mode == FanMode.Auto ? "Device control" : "",
                FanChannelStatus.Active => "Active",
                FanChannelStatus.WaitingForSource => "Waiting for temperature…",
                FanChannelStatus.SourceUnavailable => "Source unavailable — device control",
                FanChannelStatus.WriteFailed => "Write failed — check other fan software",
                _ => "",
            };
        }
        finally { _refreshing = false; }
    }

    private void ReplacePoints(IReadOnlyList<FanPoint> pts)
    {
        bool was = _refreshing; _refreshing = true;
        try { Points.Clear(); foreach (var p in pts) Points.Add(p); }
        finally { _refreshing = was; }
    }
}

public sealed partial class FanDeviceGroupViewModel : ObservableObject
{
    public FanDeviceGroupViewModel(string device) => Device = device;
    public string Device { get; }
    public ObservableCollection<FanChannelViewModel> Channels { get; } = new();
}

/// <summary>Fans window: master switch + channels grouped by device.</summary>
public sealed partial class FansViewModel : ObservableObject
{
    private readonly FanController _controller;
    private readonly Dictionary<string, FanChannelViewModel> _byId = new();

    public FansViewModel(FanController controller, IReadOnlyList<MetricDefinition> definitions, AppSettings settings)
    {
        _controller = controller;
        var options = definitions
            .Where(d => d.Unit == "°C")
            .Select(d => new FanSourceOption(d.Id,
                settings.TilePrefs.TryGetValue(d.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : d.DisplayName))
            .ToList();
        foreach (var v in controller.Views())
        {
            var group = Devices.FirstOrDefault(g => g.Device == v.Device);
            if (group is null) { group = new FanDeviceGroupViewModel(v.Device); Devices.Add(group); }
            var ch = new FanChannelViewModel(v, controller, options);
            group.Channels.Add(ch);
            _byId[v.Id] = ch;
        }
        _enabled = controller.Enabled;
    }

    public ObservableCollection<FanDeviceGroupViewModel> Devices { get; } = new();
    public bool HasChannels => _byId.Count > 0;

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) => _controller.Enabled = value;

    [RelayCommand]
    private void SetAllAuto()
    {
        foreach (var ch in _byId.Values) ch.Mode = FanMode.Auto;
    }

    public void Refresh()
    {
        foreach (var v in _controller.Views())
            if (_byId.TryGetValue(v.Id, out var ch)) ch.Apply(v);
        if (Enabled != _controller.Enabled) Enabled = _controller.Enabled;
    }
}
```

- [ ] **Step 4: Run** `dotnet test --nologo` → PASS, 0 warnings. (If `Refresh_ReflectsControllerViews` fails on `PercentText`: the default curve at 60 °C is 45 + (10/20)·30 = 60 ✓; `Percent` = reported null ?? LastWritten 60.)
- [ ] **Step 5: Commit**
```bash
git add src/Stats.Core/ViewModels/FansViewModel.cs tests/Stats.Core.Tests/FansViewModelTests.cs
git commit -m "feat(core): FansViewModel — per-device channel rows, source options, two-way desired state"
```

---

### Task 7: `FanCurveEditor` WPF control

**Files:**
- Create: `src/Stats.App/Controls/FanCurveEditor.cs`

No unit tests (rendering/mouse). Must build with zero warnings. Manual check in Task 10.

**Interfaces:**
- Produces: `FanCurveEditor : FrameworkElement` with DPs `Points (ObservableCollection<FanPoint>?)`, `MinPercent (double, 0)`, `MaxPercent (double, 100)`, `LiveTemp (double, NaN)`, `LiveTarget (double, NaN)`, `LineBrush`, `PointBrush`, `AxisBrush`, `TextBrush`, `FloorBrush`, `MarkerBrush`. Drag a point (left button); double-click empty space adds (max 8); right-click a point removes (min 2). Writes back to `Points` only on mouse-up / add / remove — never during drag.

- [ ] **Step 1: Implement**

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Stats.Core.Fans;
using Stats.Core.Settings;

namespace Stats.App.Controls;

/// <summary>Temperature→percent curve with draggable vertices. X: 20–100 °C, Y: 0–100 %.
/// Edits are committed to <see cref="Points"/> on mouse-up (drag), double-click (add), right-click (remove).</summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const double TempMin = 20, TempMax = 100;
    private const double PadL = 34, PadR = 10, PadT = 8, PadB = 22;
    private const double HitRadius = 10, DotRadius = 5.5;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(ObservableCollection<FanPoint>), typeof(FanCurveEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));
    public static readonly DependencyProperty MinPercentProperty = DependencyProperty.Register(
        nameof(MinPercent), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaxPercentProperty = DependencyProperty.Register(
        nameof(MaxPercent), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiveTempProperty = DependencyProperty.Register(
        nameof(LiveTemp), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiveTargetProperty = DependencyProperty.Register(
        nameof(LiveTarget), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LineBrushProperty = RegisterBrush(nameof(LineBrush), Color.FromRgb(0xE6, 0x8A, 0x2E));
    public static readonly DependencyProperty PointBrushProperty = RegisterBrush(nameof(PointBrush), Color.FromRgb(0xF0, 0xF0, 0xF0));
    public static readonly DependencyProperty AxisBrushProperty = RegisterBrush(nameof(AxisBrush), Color.FromRgb(0x3A, 0x3A, 0x40));
    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush), Color.FromRgb(0x9A, 0x9A, 0x9E));
    public static readonly DependencyProperty FloorBrushProperty = RegisterBrush(nameof(FloorBrush), Color.FromArgb(0x40, 0xE0, 0x5A, 0x4F));
    public static readonly DependencyProperty MarkerBrushProperty = RegisterBrush(nameof(MarkerBrush), Color.FromRgb(0x4F, 0xA3, 0xE0));

    // Named RegisterBrush (not Brush) so it does not shadow the System.Windows.Media.Brush type inside this class.
    private static DependencyProperty RegisterBrush(string name, Color c) => DependencyProperty.Register(
        name, typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(new SolidColorBrush(c), FrameworkPropertyMetadataOptions.AffectsRender));

    public ObservableCollection<FanPoint>? Points { get => (ObservableCollection<FanPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double MinPercent { get => (double)GetValue(MinPercentProperty); set => SetValue(MinPercentProperty, value); }
    public double MaxPercent { get => (double)GetValue(MaxPercentProperty); set => SetValue(MaxPercentProperty, value); }
    public double LiveTemp { get => (double)GetValue(LiveTempProperty); set => SetValue(LiveTempProperty, value); }
    public double LiveTarget { get => (double)GetValue(LiveTargetProperty); set => SetValue(LiveTargetProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush PointBrush { get => (Brush)GetValue(PointBrushProperty); set => SetValue(PointBrushProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush FloorBrush { get => (Brush)GetValue(FloorBrushProperty); set => SetValue(FloorBrushProperty, value); }
    public Brush MarkerBrush { get => (Brush)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    private List<FanPoint> _work = new();   // working copy during a drag
    private int _dragIndex = -1;
    private readonly Typeface _typeface = new("Segoe UI");

    public FanCurveEditor()
    {
        Focusable = true;
        MinHeight = 120;
        MinWidth = 200;
        ToolTip = "Drag points · double-click to add · right-click to remove";
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FanCurveEditor)d;
        if (e.OldValue is ObservableCollection<FanPoint> old) old.CollectionChanged -= self.OnCollectionChanged;
        if (e.NewValue is ObservableCollection<FanPoint> now) now.CollectionChanged += self.OnCollectionChanged;
        self._dragIndex = -1;
        self.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_dragIndex < 0) InvalidateVisual();
    }

    private IReadOnlyList<FanPoint> Current => _dragIndex >= 0 ? _work : (IReadOnlyList<FanPoint>?)Points ?? Array.Empty<FanPoint>();

    // ---- geometry ----
    private Rect Plot => new(PadL, PadT, Math.Max(1, ActualWidth - PadL - PadR), Math.Max(1, ActualHeight - PadT - PadB));
    private double X(double temp) => Plot.Left + (Math.Clamp(temp, TempMin, TempMax) - TempMin) / (TempMax - TempMin) * Plot.Width;
    private double Y(double pct) => Plot.Bottom - Math.Clamp(pct, 0, 100) / 100.0 * Plot.Height;
    private double TempAt(double x) => TempMin + Math.Clamp((x - Plot.Left) / Plot.Width, 0, 1) * (TempMax - TempMin);
    private double PctAt(double y) => Math.Clamp((Plot.Bottom - y) / Plot.Height, 0, 1) * 100.0;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 140 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // hit-test surface
        var axisPen = new Pen(AxisBrush, 1);

        // grid + labels every 20 °C / 25 %
        for (double t = TempMin; t <= TempMax; t += 20)
        {
            double x = X(t);
            dc.DrawLine(axisPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(dc, $"{t:F0}°", new Point(x - 8, plot.Bottom + 4));
        }
        for (double p = 0; p <= 100; p += 25)
        {
            double y = Y(p);
            dc.DrawLine(axisPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, $"{p:F0}%", new Point(2, y - 7));
        }

        // floor shading (channel min)
        if (MinPercent > 0)
            dc.DrawRectangle(FloorBrush, null, new Rect(plot.Left, Y(MinPercent), plot.Width, plot.Bottom - Y(MinPercent)));

        var pts = Current;
        if (pts.Count >= 2)
        {
            var sorted = pts.OrderBy(p => p.TempC).ToList();
            var linePen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
            // flat extensions beyond the ends
            dc.DrawLine(linePen, new Point(plot.Left, Y(sorted[0].Percent)), new Point(X(sorted[0].TempC), Y(sorted[0].Percent)));
            for (int i = 1; i < sorted.Count; i++)
                dc.DrawLine(linePen, new Point(X(sorted[i - 1].TempC), Y(sorted[i - 1].Percent)), new Point(X(sorted[i].TempC), Y(sorted[i].Percent)));
            dc.DrawLine(linePen, new Point(X(sorted[^1].TempC), Y(sorted[^1].Percent)), new Point(plot.Right, Y(sorted[^1].Percent)));
            foreach (var p in sorted)
                dc.DrawEllipse(PointBrush, new Pen(LineBrush, 1.5), new Point(X(p.TempC), Y(p.Percent)), DotRadius, DotRadius);
        }

        // live marker
        if (!double.IsNaN(LiveTemp))
        {
            double x = X(LiveTemp);
            var markerPen = new Pen(MarkerBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(markerPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (!double.IsNaN(LiveTarget))
                dc.DrawEllipse(MarkerBrush, null, new Point(x, Y(LiveTarget)), 4, 4);
        }
    }

    private void DrawText(DrawingContext dc, string text, Point at)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    // ---- interaction ----
    private int HitTestPoint(Point pos)
    {
        var pts = Current;
        int best = -1; double bestD = HitRadius * HitRadius;
        for (int i = 0; i < pts.Count; i++)
        {
            double dx = X(pts[i].TempC) - pos.X, dy = Y(pts[i].Percent) - pos.Y;
            double d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Points is null) return;
        var pos = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            if (HitTestPoint(pos) < 0 && Points.Count < FanCurve.MaxPoints && Plot.Contains(pos))
            {
                var np = new FanPoint((float)Math.Round(TempAt(pos.X)), (float)Math.Round(Math.Max(MinPercent, PctAt(pos.Y))));
                if (Points.All(p => Math.Abs(p.TempC - np.TempC) >= 1f)) Points.Add(np);
            }
            e.Handled = true;
            return;
        }
        int idx = HitTestPoint(pos);
        if (idx < 0) return;
        _work = Points.ToList();
        _dragIndex = idx;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex < 0 || Points is null) return;
        var pos = e.GetPosition(this);
        double temp = Math.Round(TempAt(pos.X));
        double pct = Math.Round(Math.Max(MinPercent, PctAt(pos.Y)));
        // keep strict ordering against neighbours (sorted by temp in _work)
        var order = _work.Select((p, i) => (p, i)).OrderBy(x => x.p.TempC).ToList();
        int rank = order.FindIndex(x => x.i == _dragIndex);
        if (rank > 0) temp = Math.Max(temp, order[rank - 1].p.TempC + 1);
        if (rank < order.Count - 1) temp = Math.Min(temp, order[rank + 1].p.TempC - 1);
        _work[_dragIndex] = new FanPoint((float)temp, (float)pct);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex < 0 || Points is null) return;
        int idx = _dragIndex;
        var committed = _work[idx];
        _dragIndex = -1;
        ReleaseMouseCapture();
        if (idx < Points.Count && Points[idx] != committed) Points[idx] = committed; // single Replace → one commit
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Points is null || Points.Count <= FanCurve.MinPoints) return;
        int idx = HitTestPoint(e.GetPosition(this));
        if (idx >= 0) { Points.RemoveAt(idx); e.Handled = true; }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragIndex >= 0) { _dragIndex = -1; InvalidateVisual(); }
    }
}
```

- [ ] **Step 2: Build** `dotnet build --nologo` → 0 warnings. (Note: `FanPoint` is a record → `!=` is value equality, so an unchanged drag doesn't commit.)
- [ ] **Step 3: Commit**
```bash
git add src/Stats.App/Controls/FanCurveEditor.cs
git commit -m "feat(app): FanCurveEditor — draggable temperature→percent curve control"
```

---

### Task 8: `FansWindow` + app wiring (toolbar, tray, controller tick, restore on exit)

**Files:**
- Create: `src/Stats.App/Views/FansWindow.xaml`, `src/Stats.App/Views/FansWindow.xaml.cs`
- Modify: `src/Stats.Core/ViewModels/DashboardViewModel.cs` (event + command next to `OpenPeaks`)
- Modify: `src/Stats.App/Views/DashboardWindow.xaml:20` (toolbar button)
- Modify: `src/Stats.App/App.xaml.cs` (fields, BuildReader return type, controller, ShowFans, tray item, poller hook, OnExit order, ExitApp AllowClose)

- [ ] **Step 1: DashboardViewModel** — after `public event Action? OpenPeaksRequested;` add `public event Action? OpenFansRequested;` and after `[RelayCommand] private void OpenPeaks() …` add
```csharp
    [RelayCommand] private void OpenFans() => OpenFansRequested?.Invoke();
```

- [ ] **Step 2: Toolbar** — in `DashboardWindow.xaml` after the Peaks button line add:
```xml
                <Button DockPanel.Dock="Right" Content="✢  Fans" Command="{Binding OpenFansCommand}" Style="{StaticResource HeaderButton}"/>
```

- [ ] **Step 3: FansWindow.xaml**
```xml
<Window x:Class="Stats.App.Views.FansWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ctl="clr-namespace:Stats.App.Controls"
        Title="Stats — Fans" Width="760" Height="640" MinWidth="560" MinHeight="360"
        Background="{StaticResource WindowBg}">
    <DockPanel Margin="12">
        <DockPanel DockPanel.Dock="Top" Margin="0,0,0,6">
            <Button DockPanel.Dock="Right" Content="All to Auto" Command="{Binding SetAllAutoCommand}" Style="{StaticResource HeaderButton}"/>
            <CheckBox DockPanel.Dock="Right" Content="Enable fan control" IsChecked="{Binding Enabled}" VerticalAlignment="Center" Margin="0,0,12,0"
                      Foreground="{StaticResource TextPrimary}"/>
            <TextBlock Text="Fans" FontSize="18" FontWeight="Bold" Foreground="{StaticResource TextPrimary}"/>
        </DockPanel>
        <Border DockPanel.Dock="Top" Background="#4A3A1E" CornerRadius="4" Padding="10,6" Margin="0,0,0,10">
            <TextBlock Foreground="{StaticResource TextPrimary}" TextWrapping="Wrap" FontSize="12"
                       Text="Writes fan speeds to your hardware. Close other fan software (MSI Center, Fan Control, Afterburner fan curves) first — two controllers fighting over the same fan is unsafe. Speeds return to device control when you switch a fan to Auto, turn this off, or exit Stats. Changes apply on the next sensor poll (≈1 s)."/>
        </Border>
        <TextBlock DockPanel.Dock="Top" Foreground="{StaticResource TextSecondary}" Margin="0,0,0,8"
                   Text="Fan control unavailable — the hardware reader is not active (degraded mode) or no controllable fans were found."
                   Visibility="{Binding HasChannels, Converter={StaticResource InverseBoolToVis}}"/>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Devices}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,14">
                            <TextBlock Text="{Binding Device}" FontSize="13" FontWeight="SemiBold" Foreground="{StaticResource TextSecondary}" Margin="0,0,0,4"/>
                            <ItemsControl ItemsSource="{Binding Channels}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border Background="{StaticResource TileBg}" CornerRadius="4" Padding="10,8" Margin="0,3">
                                            <StackPanel>
                                                <Grid>
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="*"/>
                                                        <ColumnDefinition Width="90"/>
                                                        <ColumnDefinition Width="70"/>
                                                        <ColumnDefinition Width="Auto"/>
                                                    </Grid.ColumnDefinitions>
                                                    <TextBox Text="{Binding Name, UpdateSourceTrigger=LostFocus}" Background="Transparent" BorderThickness="0"
                                                             Foreground="{StaticResource TextPrimary}" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center"
                                                             ToolTip="Click to rename"/>
                                                    <TextBlock Grid.Column="1" Text="{Binding RpmText}" Foreground="{StaticResource TextPrimary}" FontSize="13" TextAlignment="Right" VerticalAlignment="Center"/>
                                                    <TextBlock Grid.Column="2" Text="{Binding PercentText}" Foreground="{StaticResource TextSecondary}" FontSize="13" TextAlignment="Right" VerticalAlignment="Center"/>
                                                    <StackPanel Grid.Column="3" Orientation="Horizontal" Margin="16,0,0,0">
                                                        <!-- GroupName per channel: without it every RadioButton in the window shares one group. -->
                                                        <RadioButton Content="Auto" Margin="0,0,10,0" Foreground="{StaticResource TextPrimary}" GroupName="{Binding Id}"
                                                                     IsChecked="{Binding Mode, Converter={StaticResource Equals}, ConverterParameter=Auto}"/>
                                                        <RadioButton Content="Manual" Margin="0,0,10,0" Foreground="{StaticResource TextPrimary}" GroupName="{Binding Id}"
                                                                     IsChecked="{Binding Mode, Converter={StaticResource Equals}, ConverterParameter=Manual}"/>
                                                        <RadioButton Content="Curve" Foreground="{StaticResource TextPrimary}" GroupName="{Binding Id}"
                                                                     IsChecked="{Binding Mode, Converter={StaticResource Equals}, ConverterParameter=Curve}"/>
                                                    </StackPanel>
                                                </Grid>
                                                <TextBlock Text="{Binding StatusText}" Foreground="{StaticResource TextSecondary}" FontSize="11" Margin="0,2,0,0"/>
                                                <DockPanel Margin="0,6,0,0" Visibility="{Binding IsManual, Converter={StaticResource BoolToVis}}">
                                                    <TextBlock DockPanel.Dock="Right" Text="{Binding ManualPercent, StringFormat={}{0:F0} %}" Width="48" TextAlignment="Right"
                                                               Foreground="{StaticResource TextPrimary}" VerticalAlignment="Center"/>
                                                    <Slider Minimum="{Binding MinPercent}" Maximum="{Binding MaxPercent}" Value="{Binding ManualPercent, Delay=250}"
                                                            TickFrequency="5" IsSnapToTickEnabled="True" VerticalAlignment="Center"/>
                                                </DockPanel>
                                                <StackPanel Margin="0,6,0,0" Visibility="{Binding IsCurve, Converter={StaticResource BoolToVis}}">
                                                    <DockPanel Margin="0,0,0,6">
                                                        <Button DockPanel.Dock="Right" Content="Reset curve" Command="{Binding ResetCurveCommand}" Style="{StaticResource HeaderButton}" FontSize="11"/>
                                                        <TextBlock DockPanel.Dock="Right" Text="{Binding TargetText, StringFormat='target {0}'}" Foreground="{StaticResource TextSecondary}" Margin="12,0" VerticalAlignment="Center"/>
                                                        <TextBlock DockPanel.Dock="Right" Text="{Binding SourceTempText}" Foreground="{StaticResource TextPrimary}" VerticalAlignment="Center"/>
                                                        <TextBlock Text="Source" Foreground="{StaticResource TextSecondary}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                                        <ComboBox ItemsSource="{Binding SourceOptions}" DisplayMemberPath="Label" SelectedValuePath="Id"
                                                                  SelectedValue="{Binding SourceMetricId}" MinWidth="200" HorizontalAlignment="Left"/>
                                                    </DockPanel>
                                                    <ctl:FanCurveEditor Points="{Binding Points}" MinPercent="{Binding MinPercent}" MaxPercent="{Binding MaxPercent}"
                                                                        LiveTemp="{Binding LiveTemp}" LiveTarget="{Binding LiveTarget}" Height="150"
                                                                        LineBrush="{StaticResource AccentBrush}" PointBrush="{StaticResource TextPrimary}"
                                                                        AxisBrush="{StaticResource BorderDim}" TextBrush="{StaticResource TextSecondary}"/>
                                                </StackPanel>
                                            </StackPanel>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```
`InverseBoolToVis` does not exist yet: add to `src/Stats.App/Converters/` a 12-line `InverseBoolToVisibilityConverter : IValueConverter` (`true → Collapsed`, `false → Visible`) and register it in `Theme.xaml` as `<conv:InverseBoolToVisibilityConverter x:Key="InverseBoolToVis"/>`. `GroupHeader` style exists in App.xaml (used by the dashboard sections).

`FansWindow.xaml.cs` — identical to `PeaksWindow.xaml.cs` with the class renamed `FansWindow`.

- [ ] **Step 4: App.xaml.cs wiring**

Fields (after `_peaksVm`):
```csharp
    private FanController? _fanController;
    private FansWindow? _fans;
    private FansViewModel? _fansVm;
    private IReadOnlyList<MetricDefinition> _definitions = Array.Empty<MetricDefinition>();
```
`using Stats.Core.Fans;` at top. In `BuildReader` change the return tuple type to `(CompositeSensorReader Reader, FrameRateReader Frames)` and the field `_reader` stays `ISensorReader?` but add `private CompositeSensorReader? _composite;` assigned alongside: replace `(_reader, _frameReader) = BuildReader(…)` with
```csharp
            (_composite, _frameReader) = BuildReader(() => new LhmSensorReader());
            _reader = _composite;
            definitions = _reader.Discover();
```
(same in the catch branch). After `definitions` is final (right after the try/catch) add `_definitions = definitions;`.

After the `_poller` creation block (where `_frameReader.Window` is set) add:
```csharp
        _fanController = new FanController(_composite!, _settings, SaveSettings);
```
Register the controller tick **before** the existing Dispatcher-marshalling `SnapshotAvailable` handler:
```csharp
        var fanController = _fanController;
        _poller.SnapshotAvailable += snapshot => fanController.Tick(snapshot, DateTime.UtcNow); // poll thread: same thread as LHM reads
```
and inside the existing Dispatcher handler add `if (_fans is { IsVisible: true }) _fansVm?.Refresh();` after the peaks refresh line.

Hooks: `_dashboardVm.OpenFansRequested += ShowFans;` next to `OpenPeaksRequested`. Tray menu: after the `peaks` item add
```csharp
        var fans = new MenuItem { Header = "Fans…" };
        fans.Click += (_, _) => ShowFans();
```
and `menu.Items.Add(fans);` after `menu.Items.Add(peaks);`.

`ShowFans` (mirror `ShowPeaks`, using `FansLeft/Top/Width/Height` and `SaveFansBounds`):
```csharp
    private void ShowFans()
    {
        if (_fanController is null || _settings is null) return;
        if (_fans is null)
        {
            _fansVm = new FansViewModel(_fanController, _definitions, _settings);
            _fans = new FansWindow { DataContext = _fansVm };
            if (_settings.FansWidth is double w) _fans.Width = w;
            if (_settings.FansHeight is double h) _fans.Height = h;
            if (_settings.FansLeft is double l) _fans.Left = ClampToVirtualScreenX(l, 200);
            if (_settings.FansTop is double t) _fans.Top = ClampToVirtualScreenY(t, 100);
            _fans.LocationChanged += (_, _) => SaveFansBounds();
            _fans.SizeChanged += (_, _) => SaveFansBounds();
        }
        _fansVm!.Refresh();
        _fans.Show();
        _fans.WindowState = WindowState.Normal;
        _fans.Activate();
    }

    private void SaveFansBounds()
    {
        if (_fans is null || _settings is null) return;
        if (_fans.WindowState != WindowState.Normal) return;
        if (double.IsNaN(_fans.Left) || double.IsNaN(_fans.Top)) return;
        _settings.FansLeft = _fans.Left;
        _settings.FansTop = _fans.Top;
        _settings.FansWidth = _fans.Width;
        _settings.FansHeight = _fans.Height;
    }
```
`OnExit` order becomes:
```csharp
        _hotkey?.Dispose();
        _poller?.Dispose();          // stop the poll thread first …
        _fanController?.RestoreAll(); // … then hand every fan back to device control (no concurrent LHM access now)
        _reader?.Dispose();
        SaveSettings();
```
`ExitApp`: add `if (_fans is not null) _fans.AllowClose = true;`.

- [ ] **Step 5: Build + test** `dotnet build --nologo && dotnet test --nologo` → 0 warnings, all PASS.
- [ ] **Step 6: Smoke (worker, no hardware writes)** `dotnet run --project src/Stats.App` from a shell that can elevate (if the UAC prompt can't be satisfied from the worker's shell, record that and skip): the Fans window opens from the toolbar, lists devices/channels with live RPM, master switch **off**; changing modes while the switch is off writes nothing (visible: status stays "Device control"). Close the app from the tray. Do **not** enable the master switch.
- [ ] **Step 7: Commit**
```bash
git add src/Stats.App/Views/FansWindow.xaml src/Stats.App/Views/FansWindow.xaml.cs src/Stats.App/Converters/InverseBoolToVisibilityConverter.cs src/Stats.App/Views/Theme.xaml src/Stats.App/Views/DashboardWindow.xaml src/Stats.Core/ViewModels/DashboardViewModel.cs src/Stats.App/App.xaml.cs
git commit -m "feat(app): Fans window — per-device channels, modes, curve editor; controller on poll loop; restore on exit"
```

---

### Task 9: Docs

**Files:**
- Modify: `README.md` (features list + a "Fan control" section: what it controls, safety rules, MSI Center warning, crash caveat)
- Modify: `installer/THIRD-PARTY.txt` — no change needed (LHM already listed) — verify and skip.

- [ ] **Step 1:** Add to README after the FPS bullet: "**Fan control** — *Fans* window (toolbar / tray): every LibreHardwareMonitor-controllable fan (motherboard headers, GPU fans, supported USB coolers) can be Auto (device/BIOS), Manual %, or follow a temperature curve driven by any temperature Stats monitors. Off until you enable it; 2 °C hysteresis, max 10 %/s change, falls back to device control if the source temperature disappears for 10 s, pumps never below 50 %; fans return to device control when you exit Stats. Close MSI Center / Fan Control / Afterburner fan curves first. If Stats *crashes* (not exits), fans keep their last speed until Stats runs again or you reboot."
- [ ] **Step 2:** Commit `docs: fan control section in README`.

---

### Task 10: Manual hardware verification (user + Fable)

- [ ] Build installer `installer\build.ps1 -Version 1.3.0-beta`, install, launch from Start menu.
- [ ] Picker shows **Motherboard** (5 temps, Fan #1–#6 RPM, Fan #1–#6 %) and **Cooler** (Liquid Temperature, Radiator Fan, Pump Fan, Pump) groups.
- [ ] Fans window: 6 ITE channels, 2 GPU channels (min 30), 3 CoreLiquid channels; live RPM on Fan #1/#4, GPU fans, AIO.
- [ ] Make sure MSI Center is not running. Enable fan control. Fan #4 (889 rpm) → Manual 70 % → RPM rises within ~1 s; → Manual 30 % → drops ≤ 10 %/tick; → Auto → BIOS speed returns.
- [ ] Fan #4 → Curve, source CPU Tctl, default curve; run a CPU load → fan follows; stop load → eases down.
- [ ] GPU Fan 1 → Manual 30 → held at 30 (floor); Manual 60 → ramps; Auto → driver curve returns.
- [ ] Radiator Fan → Manual 40 → audible change; Pump → Manual 20 → held at 50 (floor). Back to Auto.
- [ ] Exit Stats → within seconds all fans at BIOS/device speeds (check RPM in BIOS tool or relaunch Stats in Auto).
- [ ] Negative: disable master switch with a Manual channel active → fan returns to device control on the next tick.

---

## Self-review notes

- Spec coverage: settings (T1), curve (T2), groups/mapping (T3), backend + LHM enable (T4), controller rules — hysteresis/slew/floors/failsafe/failures/restore (T5), VM + source options (T6), curve editor (T7), window/toolbar/tray/tick/exit order (T8), docs (T9), hardware verification (T10). Out-of-scope items untouched.
- Type consistency: `FanPoint(float TempC, float Percent)` everywhere; `FanChannel(Id, Name, Device, RpmMetricId, PercentMetricId, MinPercent, MaxPercent)`; `FanController.TrySetPoints(string, IEnumerable<FanPoint>)`; `FanChannelView` field order matches T5 ctor calls in T5/T6; `FansViewModel(FanController, IReadOnlyList<MetricDefinition>, AppSettings)` used identically in T6 tests and T8; `FanCurveEditor` DP names match the T8 XAML bindings.
- Thread model: `Tick` on poll thread via `SnapshotAvailable`; setters lock; `RestoreAll` after `_poller.Dispose()`.
