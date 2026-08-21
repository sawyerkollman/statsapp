# Stats Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Native Windows WPF app that consolidates CPU/GPU/RAM/disk/network live stats (Ryzen Master + Task Manager + Core Temp + Afterburner territory) into one dark dashboard with user-selectable metric tiles, sparkline history, tray icon, and an always-on-top overlay.

**Architecture:** Single process. `Stats.Core` (net8.0-windows class library, no WPF) holds the sensor engine wrapper, metric model, ring-buffer store, settings, and ViewModels — all unit-testable. `Stats.App` (WPF) holds Views, a custom Sparkline control, tray integration, and the composition root. LibreHardwareMonitorLib polled on a background loop; snapshots marshaled to UI thread.

**Tech Stack:** .NET 8, WPF, LibreHardwareMonitorLib, CommunityToolkit.Mvvm, H.NotifyIcon.Wpf, System.Diagnostics.PerformanceCounter (fallback), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-stats-dashboard-design.md`

**Working directory for all commands:** `C:\claude-projects\Stats`

**Spec deviation (agreed rationale):** PPT/TDC/EDC "% of limit" rendering — LibreHardwareMonitor does not expose the user's PBO limits. v1 shows the raw sensor value; if the user adds a limit to `MetricLimits` in settings.json (e.g. `"cpu…ppt": 150`), the tile additionally shows "% of N". No UI for editing limits in v1.

---

## File structure

```
Stats.sln
.gitignore
src/Stats.Core/Stats.Core.csproj
src/Stats.Core/Metrics/MetricGroup.cs          — enum Cpu/Gpu/Memory/Storage/Network
src/Stats.Core/Metrics/MetricDefinition.cs     — id, name, group, hardware name, unit, format
src/Stats.Core/Metrics/MetricHistory.cs        — 120-sample ring buffer + session min/max/avg
src/Stats.Core/Metrics/MetricStore.cs          — id → MetricHistory, Apply(snapshot)
src/Stats.Core/Metrics/ValueFormatter.cs       — value → display string (B/s auto-scaling)
src/Stats.Core/Metrics/DefaultSelector.cs      — default dashboard/overlay metric sets
src/Stats.Core/Sensors/SensorSnapshot.cs       — immutable id → value dict
src/Stats.Core/Sensors/ISensorReader.cs        — Discover()/Read() boundary
src/Stats.Core/Sensors/RawSensor.cs            — plain strings describing one LHM sensor
src/Stats.Core/Sensors/SensorMapper.cs         — RawSensor → MetricDefinition (pure)
src/Stats.Core/Sensors/LhmSensorReader.cs      — LibreHardwareMonitor implementation
src/Stats.Core/Sensors/PerfCounterSensorReader.cs — degraded WMI/PerfCounter fallback
src/Stats.Core/Sensors/SensorPoller.cs         — background poll loop, event per snapshot
src/Stats.Core/Settings/AppSettings.cs         — POCO
src/Stats.Core/Settings/SettingsService.cs     — JSON load/save, corrupt-file fallback
src/Stats.Core/ViewModels/MetricTileViewModel.cs
src/Stats.Core/ViewModels/MetricPickerItem.cs
src/Stats.Core/ViewModels/DashboardViewModel.cs
src/Stats.Core/ViewModels/OverlayViewModel.cs
src/Stats.App/Stats.App.csproj
src/Stats.App/app.manifest                     — requireAdministrator
src/Stats.App/App.xaml / App.xaml.cs           — theme resources; composition root; tray
src/Stats.App/Controls/Sparkline.cs            — FrameworkElement, OnRender polyline
src/Stats.App/Views/DashboardWindow.xaml(.cs)  — tile grid + picker flyout + degraded banner
src/Stats.App/Views/OverlayWindow.xaml(.cs)    — borderless topmost strip
tests/Stats.Core.Tests/Stats.Core.Tests.csproj
tests/Stats.Core.Tests/MetricHistoryTests.cs
tests/Stats.Core.Tests/MetricStoreTests.cs
tests/Stats.Core.Tests/SensorMapperTests.cs
tests/Stats.Core.Tests/ValueFormatterTests.cs
tests/Stats.Core.Tests/DefaultSelectorTests.cs
tests/Stats.Core.Tests/SettingsServiceTests.cs
tests/Stats.Core.Tests/SensorPollerTests.cs
tests/Stats.Core.Tests/ViewModelTests.cs
```

---

### Task 1: Solution scaffold

**Files:**
- Create: `Stats.sln`, `.gitignore`, `src/Stats.Core/`, `src/Stats.App/`, `tests/Stats.Core.Tests/`

- [ ] **Step 1: Create projects and solution**

```bash
cd C:/claude-projects/Stats
dotnet new sln -n Stats
dotnet new classlib -n Stats.Core -o src/Stats.Core
dotnet new wpf -n Stats.App -o src/Stats.App
dotnet new xunit -n Stats.Core.Tests -o tests/Stats.Core.Tests
dotnet sln add src/Stats.Core src/Stats.App tests/Stats.Core.Tests
dotnet add src/Stats.App reference src/Stats.Core
dotnet add tests/Stats.Core.Tests reference src/Stats.Core
```

Expected: each command reports success; `Stats.sln` lists 3 projects.

- [ ] **Step 2: Set Stats.Core target framework to net8.0-windows**

Replace contents of `src/Stats.Core/Stats.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Also edit `tests/Stats.Core.Tests/Stats.Core.Tests.csproj`: change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net8.0-windows</TargetFramework>` (keep everything else the template generated).

Delete template files: `src/Stats.Core/Class1.cs`, `tests/Stats.Core.Tests/UnitTest1.cs`.

- [ ] **Step 3: Add NuGet packages**

```bash
cd C:/claude-projects/Stats
dotnet add src/Stats.Core package LibreHardwareMonitorLib
dotnet add src/Stats.Core package CommunityToolkit.Mvvm
dotnet add src/Stats.Core package System.Diagnostics.PerformanceCounter
dotnet add src/Stats.App package H.NotifyIcon.Wpf
dotnet add src/Stats.App package System.Drawing.Common
```

Expected: all resolve. (Use latest stable of each; no version pins.)

- [ ] **Step 4: Add .gitignore and verify build**

Create `.gitignore`:

```
bin/
obj/
.vs/
*.user
```

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s)` (template WPF window still present — fine for now).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution (Stats.Core, Stats.App WPF, tests)"
```

---

### Task 2: Metric model + MetricHistory (ring buffer, session stats)

**Files:**
- Create: `src/Stats.Core/Metrics/MetricGroup.cs`, `src/Stats.Core/Metrics/MetricDefinition.cs`, `src/Stats.Core/Metrics/MetricHistory.cs`
- Test: `tests/Stats.Core.Tests/MetricHistoryTests.cs`

- [ ] **Step 1: Create the model types (needed to compile tests)**

`src/Stats.Core/Metrics/MetricGroup.cs`:

```csharp
namespace Stats.Core.Metrics;

public enum MetricGroup { Cpu, Gpu, Memory, Storage, Network }
```

`src/Stats.Core/Metrics/MetricDefinition.cs`:

```csharp
namespace Stats.Core.Metrics;

/// <summary>Stable identity of one monitorable value. Id survives restarts and settings round-trips.</summary>
public sealed record MetricDefinition(
    string Id,
    string DisplayName,
    MetricGroup Group,
    string HardwareName,
    string Unit,
    string Format = "F0");
```

- [ ] **Step 2: Write failing tests for MetricHistory**

`tests/Stats.Core.Tests/MetricHistoryTests.cs`:

```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class MetricHistoryTests
{
    [Fact]
    public void NewHistory_HasNoCurrentAndNaNStats()
    {
        var h = new MetricHistory(4);
        Assert.Null(h.Current);
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.True(float.IsNaN(h.SessionMax));
        Assert.True(float.IsNaN(h.SessionAvg));
        Assert.Empty(h.ToArray());
    }

    [Fact]
    public void Add_UpdatesCurrentAndStats()
    {
        var h = new MetricHistory(4);
        h.Add(10f);
        h.Add(20f);
        Assert.Equal(20f, h.Current);
        Assert.Equal(10f, h.SessionMin);
        Assert.Equal(20f, h.SessionMax);
        Assert.Equal(15f, h.SessionAvg);
        Assert.Equal(new[] { 10f, 20f }, h.ToArray());
    }

    [Fact]
    public void Add_BeyondCapacity_WrapsOldestOut_ButSessionStatsKeepAll()
    {
        var h = new MetricHistory(3);
        h.Add(1f); h.Add(2f); h.Add(3f); h.Add(4f);
        Assert.Equal(new[] { 2f, 3f, 4f }, h.ToArray());
        Assert.Equal(1f, h.SessionMin);   // session min survives buffer eviction
        Assert.Equal(4f, h.SessionMax);
        Assert.Equal(2.5f, h.SessionAvg);
    }

    [Fact]
    public void Add_Null_SetsCurrentNull_DoesNotPolluteStatsOrBuffer()
    {
        var h = new MetricHistory(4);
        h.Add(5f);
        h.Add(null);
        Assert.Null(h.Current);
        Assert.Equal(5f, h.SessionMin);
        Assert.Equal(5f, h.SessionMax);
        Assert.Equal(new[] { 5f }, h.ToArray());
    }

    [Fact]
    public void Add_NaN_TreatedAsGap()
    {
        var h = new MetricHistory(4);
        h.Add(float.NaN);
        Assert.True(float.IsNaN(h.SessionMin));
        Assert.Empty(h.ToArray());
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~MetricHistoryTests`
Expected: compile error — `MetricHistory` not defined.

- [ ] **Step 4: Implement MetricHistory**

`src/Stats.Core/Metrics/MetricHistory.cs`:

```csharp
namespace Stats.Core.Metrics;

/// <summary>Fixed-capacity ring buffer of samples plus session-wide min/max/avg (which outlive buffer eviction).</summary>
public sealed class MetricHistory
{
    private readonly float[] _buffer;
    private int _next;
    private int _count;
    private double _sum;
    private long _samples;

    public MetricHistory(int capacity = 120)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new float[capacity];
    }

    public float? Current { get; private set; }
    public float SessionMin { get; private set; } = float.NaN;
    public float SessionMax { get; private set; } = float.NaN;
    public float SessionAvg => _samples == 0 ? float.NaN : (float)(_sum / _samples);

    public void Add(float? value)
    {
        Current = value is float f && float.IsNaN(f) ? null : value;
        if (Current is not float v) return;

        _buffer[_next] = v;
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;

        _sum += v;
        _samples++;
        if (float.IsNaN(SessionMin) || v < SessionMin) SessionMin = v;
        if (float.IsNaN(SessionMax) || v > SessionMax) SessionMax = v;
    }

    /// <summary>Buffered samples, oldest first.</summary>
    public float[] ToArray()
    {
        var result = new float[_count];
        int start = (_next - _count + _buffer.Length) % _buffer.Length;
        for (int i = 0; i < _count; i++)
            result[i] = _buffer[(start + i) % _buffer.Length];
        return result;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~MetricHistoryTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: metric model and MetricHistory ring buffer with session stats"
```

---

### Task 3: SensorSnapshot + MetricStore

**Files:**
- Create: `src/Stats.Core/Sensors/SensorSnapshot.cs`, `src/Stats.Core/Metrics/MetricStore.cs`
- Test: `tests/Stats.Core.Tests/MetricStoreTests.cs`

- [ ] **Step 1: Create SensorSnapshot**

`src/Stats.Core/Sensors/SensorSnapshot.cs`:

```csharp
namespace Stats.Core.Sensors;

/// <summary>One poll tick: metric id → value (null = sensor produced nothing this tick).</summary>
public sealed record SensorSnapshot(IReadOnlyDictionary<string, float?> Values, DateTime TimestampUtc);
```

- [ ] **Step 2: Write failing tests**

`tests/Stats.Core.Tests/MetricStoreTests.cs`:

```csharp
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class MetricStoreTests
{
    private static MetricDefinition Def(string id) =>
        new(id, id, MetricGroup.Cpu, "CPU", "°C", "F1");

    [Fact]
    public void Apply_RoutesValuesToMatchingHistories()
    {
        var store = new MetricStore(new[] { Def("a"), Def("b") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["a"] = 1f, ["b"] = 2f }, DateTime.UtcNow));
        Assert.Equal(1f, store["a"].Current);
        Assert.Equal(2f, store["b"].Current);
    }

    [Fact]
    public void Apply_MissingIdInSnapshot_SetsCurrentNull()
    {
        var store = new MetricStore(new[] { Def("a") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["a"] = 1f }, DateTime.UtcNow));
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>(), DateTime.UtcNow));
        Assert.Null(store["a"].Current);
        Assert.Equal(1f, store["a"].SessionMax); // history retained
    }

    [Fact]
    public void Apply_UnknownIdsInSnapshot_Ignored()
    {
        var store = new MetricStore(new[] { Def("a") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["zzz"] = 9f }, DateTime.UtcNow));
        Assert.False(store.TryGet("zzz", out _));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~MetricStoreTests`
Expected: compile error — `MetricStore` not defined.

- [ ] **Step 4: Implement MetricStore**

`src/Stats.Core/Metrics/MetricStore.cs`:

```csharp
using Stats.Core.Sensors;

namespace Stats.Core.Metrics;

/// <summary>Holds one MetricHistory per known metric. UI reads from here; poller writes via Apply.</summary>
public sealed class MetricStore
{
    private readonly Dictionary<string, MetricHistory> _histories = new();

    public IReadOnlyList<MetricDefinition> Definitions { get; }

    public MetricStore(IEnumerable<MetricDefinition> definitions, int capacity = 120)
    {
        Definitions = definitions.ToList();
        foreach (var d in Definitions)
            _histories[d.Id] = new MetricHistory(capacity);
    }

    public MetricHistory this[string id] => _histories[id];

    public bool TryGet(string id, out MetricHistory history) => _histories.TryGetValue(id, out history!);

    public void Apply(SensorSnapshot snapshot)
    {
        foreach (var (id, history) in _histories)
            history.Add(snapshot.Values.TryGetValue(id, out var v) ? v : null);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~MetricStoreTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: SensorSnapshot and MetricStore"
```

---

### Task 4: RawSensor + SensorMapper (pure LHM→metric mapping)

**Files:**
- Create: `src/Stats.Core/Sensors/RawSensor.cs`, `src/Stats.Core/Sensors/SensorMapper.cs`
- Test: `tests/Stats.Core.Tests/SensorMapperTests.cs`

Mapping rules: hardware type string (LHM `HardwareType.ToString()`) → group (`Cpu`→Cpu; `GpuNvidia`/`GpuAmd`/`GpuIntel`→Gpu; `Memory`→Memory; `Storage`→Storage; `Network`→Network; anything else → skipped). Sensor type → unit/format. Id = `{group}.{slug(hardwareName)}.{slug(sensorType)}.{slug(sensorName)}`. Multi-instance groups (Gpu/Storage/Network) prefix display name with hardware name. Duplicate ids get `-2`, `-3` suffixes.

- [ ] **Step 1: Create RawSensor**

`src/Stats.Core/Sensors/RawSensor.cs`:

```csharp
namespace Stats.Core.Sensors;

/// <summary>Plain-string description of one discovered sensor. Keeps SensorMapper free of LHM types.</summary>
public sealed record RawSensor(string HardwareType, string HardwareName, string SensorType, string SensorName);
```

- [ ] **Step 2: Write failing tests**

`tests/Stats.Core.Tests/SensorMapperTests.cs`:

```csharp
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class SensorMapperTests
{
    [Fact]
    public void Map_CpuTemperature_ProducesCpuGroupCelsiusAndSlugId()
    {
        var def = SensorMapper.Map(new RawSensor("Cpu", "AMD Ryzen 7 9800X3D", "Temperature", "Core (Tctl/Tdie)"));
        Assert.NotNull(def);
        Assert.Equal(MetricGroup.Cpu, def!.Group);
        Assert.Equal("°C", def.Unit);
        Assert.Equal("cpu.amd-ryzen-7-9800x3d.temperature.core-tctl-tdie", def.Id);
        Assert.Equal("Core (Tctl/Tdie)", def.DisplayName); // single-instance group: no hardware prefix
    }

    [Fact]
    public void Map_NvidiaGpuClock_ProducesGpuGroupWithHardwarePrefixedName()
    {
        var def = SensorMapper.Map(new RawSensor("GpuNvidia", "NVIDIA GeForce RTX 5070 Ti", "Clock", "GPU Core"));
        Assert.NotNull(def);
        Assert.Equal(MetricGroup.Gpu, def!.Group);
        Assert.Equal("MHz", def.Unit);
        Assert.Contains("RTX 5070 Ti", def.DisplayName); // multi-instance group keeps hardware name visible
    }

    [Fact]
    public void Map_MotherboardSensor_ReturnsNull()
    {
        Assert.Null(SensorMapper.Map(new RawSensor("Motherboard", "X870", "Temperature", "System")));
    }

    [Fact]
    public void Map_UnknownSensorType_ReturnsNull()
    {
        Assert.Null(SensorMapper.Map(new RawSensor("Cpu", "X", "Factor", "Weird")));
    }

    [Theory]
    [InlineData("Temperature", "°C")]
    [InlineData("Clock", "MHz")]
    [InlineData("Load", "%")]
    [InlineData("Power", "W")]
    [InlineData("Voltage", "V")]
    [InlineData("Fan", "RPM")]
    [InlineData("Throughput", "B/s")]
    [InlineData("Data", "GB")]
    [InlineData("SmallData", "MB")]
    [InlineData("Level", "%")]
    [InlineData("Control", "%")]
    public void Map_SensorTypeUnits(string sensorType, string expectedUnit)
    {
        var def = SensorMapper.Map(new RawSensor("Cpu", "X", sensorType, "S"));
        Assert.Equal(expectedUnit, def!.Unit);
    }

    [Fact]
    public void MapAll_DuplicateIds_GetNumericSuffixes()
    {
        var raw = new RawSensor("Storage", "Samsung SSD", "Load", "Used Space");
        var defs = SensorMapper.MapAll(new[] { raw, raw, raw });
        Assert.Equal(3, defs.Count);
        Assert.Equal(defs[0]!.Id + "-2", defs[1]!.Id);
        Assert.Equal(defs[0]!.Id + "-3", defs[2]!.Id);
    }

    [Fact]
    public void MapAll_PreservesInputOrderAndNullsForSkipped()
    {
        var defs = SensorMapper.MapAll(new[]
        {
            new RawSensor("Cpu", "X", "Temperature", "A"),
            new RawSensor("Motherboard", "X", "Temperature", "B"),
        });
        Assert.NotNull(defs[0]);
        Assert.Null(defs[1]);
    }

    [Fact]
    public void Slug_LowercasesAndCollapsesNonAlphanumerics()
    {
        Assert.Equal("core-tctl-tdie", SensorMapper.Slug("Core (Tctl/Tdie)"));
        Assert.Equal("gpu-core", SensorMapper.Slug("GPU Core"));
        Assert.Equal("d", SensorMapper.Slug("(((D:)))"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SensorMapperTests`
Expected: compile error — `SensorMapper` not defined.

- [ ] **Step 4: Implement SensorMapper**

`src/Stats.Core/Sensors/SensorMapper.cs`:

```csharp
using System.Text;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

public static class SensorMapper
{
    /// <summary>Maps each raw sensor; result list is parallel to input (null = sensor not exposed). Ids are unique.</summary>
    public static IReadOnlyList<MetricDefinition?> MapAll(IReadOnlyList<RawSensor> sensors)
    {
        var used = new HashSet<string>();
        var result = new List<MetricDefinition?>(sensors.Count);
        foreach (var raw in sensors)
        {
            var def = Map(raw);
            if (def is null) { result.Add(null); continue; }
            var id = def.Id;
            int n = 2;
            while (!used.Add(id)) id = $"{def.Id}-{n++}";
            result.Add(def with { Id = id });
        }
        return result;
    }

    public static MetricDefinition? Map(RawSensor raw)
    {
        MetricGroup? group = raw.HardwareType switch
        {
            "Cpu" => MetricGroup.Cpu,
            "GpuNvidia" or "GpuAmd" or "GpuIntel" => MetricGroup.Gpu,
            "Memory" => MetricGroup.Memory,
            "Storage" => MetricGroup.Storage,
            "Network" => MetricGroup.Network,
            _ => null,
        };
        if (group is null) return null;

        (string Unit, string Format)? kind = raw.SensorType switch
        {
            "Temperature" => ("°C", "F1"),
            "Clock" => ("MHz", "F0"),
            "Load" => ("%", "F0"),
            "Power" => ("W", "F1"),
            "Voltage" => ("V", "F3"),
            "Fan" => ("RPM", "F0"),
            "Control" => ("%", "F0"),
            "Throughput" => ("B/s", "F0"),
            "Data" => ("GB", "F1"),
            "SmallData" => ("MB", "F0"),
            "Level" => ("%", "F0"),
            _ => null,
        };
        if (kind is null) return null;

        bool multiInstance = group is MetricGroup.Gpu or MetricGroup.Storage or MetricGroup.Network;
        string display = multiInstance ? $"{raw.HardwareName} · {raw.SensorName}" : raw.SensorName;
        string id = $"{Slug(group.Value.ToString())}.{Slug(raw.HardwareName)}.{Slug(raw.SensorType)}.{Slug(raw.SensorName)}";
        return new MetricDefinition(id, display, group.Value, raw.HardwareName, kind.Value.Unit, kind.Value.Format);
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool lastDash = false;
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().TrimEnd('-');
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SensorMapperTests`
Expected: `Passed! - Failed: 0, Passed: 19` (11 theory cases + 8 facts).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: SensorMapper — pure LHM sensor to MetricDefinition mapping"
```

---

### Task 5: ValueFormatter

**Files:**
- Create: `src/Stats.Core/Metrics/ValueFormatter.cs`
- Test: `tests/Stats.Core.Tests/ValueFormatterTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/Stats.Core.Tests/ValueFormatterTests.cs`:

```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class ValueFormatterTests
{
    private static MetricDefinition Def(string unit, string format = "F1") =>
        new("id", "Name", MetricGroup.Cpu, "HW", unit, format);

    [Fact]
    public void Format_Null_ReturnsDash() => Assert.Equal("—", ValueFormatter.Format(Def("°C"), null));

    [Fact]
    public void Format_NaN_ReturnsDash() => Assert.Equal("—", ValueFormatter.Format(Def("°C"), float.NaN));

    [Fact]
    public void Format_Temperature_UsesFormatAndUnit() =>
        Assert.Equal("42.5 °C", ValueFormatter.Format(Def("°C"), 42.5f));

    [Theory]
    [InlineData(512f, "512 B/s")]
    [InlineData(40_000f, "40.0 KB/s")]
    [InlineData(16_500_000f, "16.5 MB/s")]
    public void Format_Throughput_AutoScales(float value, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(Def("B/s", "F0"), value));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~ValueFormatterTests`
Expected: compile error — `ValueFormatter` not defined.

- [ ] **Step 3: Implement**

`src/Stats.Core/Metrics/ValueFormatter.cs`:

```csharp
using System.Globalization;

namespace Stats.Core.Metrics;

public static class ValueFormatter
{
    public static string Format(MetricDefinition def, float? value)
    {
        if (value is not float v || float.IsNaN(v)) return "—";
        if (def.Unit == "B/s")
        {
            if (v >= 1_000_000f) return string.Create(CultureInfo.InvariantCulture, $"{v / 1_000_000f:F1} MB/s");
            if (v >= 1_000f) return string.Create(CultureInfo.InvariantCulture, $"{v / 1_000f:F1} KB/s");
            return string.Create(CultureInfo.InvariantCulture, $"{v:F0} B/s");
        }
        return $"{v.ToString(def.Format, CultureInfo.InvariantCulture)} {def.Unit}".TrimEnd();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~ValueFormatterTests`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: ValueFormatter with throughput auto-scaling"
```

---

### Task 6: DefaultSelector

**Files:**
- Create: `src/Stats.Core/Metrics/DefaultSelector.cs`
- Test: `tests/Stats.Core.Tests/DefaultSelectorTests.cs`

Spec defaults — dashboard: CPU package temp, CPU total load, CPU package power, PPT if present, GPU temp, GPU core clock, GPU power, memory %, one activity per disk, one download per adapter. Overlay: CPU temp, GPU temp, GPU core clock.

- [ ] **Step 1: Write failing tests**

`tests/Stats.Core.Tests/DefaultSelectorTests.cs`:

```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class DefaultSelectorTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp.tctl", "Core (Tctl/Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.temp.ccd1", "CCD1 (Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.load.total", "CPU Total", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.load.core1", "CPU Core #1", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.power.package", "Package", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("cpu.power.ppt", "CPU PPT", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("gpu.temp.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("gpu.clock.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.clock.mem", "RTX · GPU Memory", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.power.total", "RTX · GPU Package", MetricGroup.Gpu, "RTX", "W", "F1"),
        new("mem.load", "Memory", MetricGroup.Memory, "Generic Memory", "%"),
        new("disk.c.activity", "SSD-C · Total Activity", MetricGroup.Storage, "SSD-C", "%"),
        new("disk.c.used", "SSD-C · Used Space", MetricGroup.Storage, "SSD-C", "%"),
        new("disk.d.activity", "HDD-D · Total Activity", MetricGroup.Storage, "HDD-D", "%"),
        new("net.eth4.down", "Eth4 · Download Speed", MetricGroup.Network, "Eth4", "B/s"),
        new("net.eth4.up", "Eth4 · Upload Speed", MetricGroup.Network, "Eth4", "B/s"),
        new("net.eth5.down", "Eth5 · Download Speed", MetricGroup.Network, "Eth5", "B/s"),
    };

    [Fact]
    public void DashboardDefaults_PicksHeadlineMetricsAndPerInstanceEntries()
    {
        var ids = DefaultSelector.DashboardDefaults(Defs);
        Assert.Contains("cpu.temp.tctl", ids);      // prefers tctl over ccd
        Assert.Contains("cpu.load.total", ids);      // prefers total over per-core
        Assert.Contains("cpu.power.package", ids);
        Assert.Contains("cpu.power.ppt", ids);
        Assert.Contains("gpu.temp.core", ids);
        Assert.Contains("gpu.clock.core", ids);
        Assert.Contains("gpu.power.total", ids);
        Assert.Contains("mem.load", ids);
        Assert.Contains("disk.c.activity", ids);     // activity, not used-space
        Assert.Contains("disk.d.activity", ids);     // one per disk
        Assert.Contains("net.eth4.down", ids);       // download, not upload
        Assert.Contains("net.eth5.down", ids);       // one per adapter
        Assert.DoesNotContain("cpu.load.core1", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void OverlayDefaults_CpuTempGpuTempGpuClock()
    {
        var ids = DefaultSelector.OverlayDefaults(Defs);
        Assert.Equal(new[] { "cpu.temp.tctl", "gpu.temp.core", "gpu.clock.core" }, ids);
    }

    [Fact]
    public void Defaults_EmptyDiscovery_ReturnsEmpty()
    {
        Assert.Empty(DefaultSelector.DashboardDefaults(new List<MetricDefinition>()));
        Assert.Empty(DefaultSelector.OverlayDefaults(new List<MetricDefinition>()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~DefaultSelectorTests`
Expected: compile error — `DefaultSelector` not defined.

- [ ] **Step 3: Implement**

`src/Stats.Core/Metrics/DefaultSelector.cs`:

```csharp
namespace Stats.Core.Metrics;

/// <summary>Builds the out-of-box metric selections from whatever sensors were discovered.</summary>
public static class DefaultSelector
{
    public static List<string> DashboardDefaults(IReadOnlyList<MetricDefinition> defs)
    {
        var picks = new List<string?>
        {
            FirstId(defs, MetricGroup.Cpu, "°C", "tctl", "package"),
            FirstId(defs, MetricGroup.Cpu, "%", "total"),
            FirstId(defs, MetricGroup.Cpu, "W", "package"),
            defs.FirstOrDefault(d => d.Group == MetricGroup.Cpu &&
                d.DisplayName.Contains("ppt", StringComparison.OrdinalIgnoreCase))?.Id,
            FirstId(defs, MetricGroup.Gpu, "°C", "core"),
            FirstId(defs, MetricGroup.Gpu, "MHz", "core"),
            FirstId(defs, MetricGroup.Gpu, "W", "package", "power"),
            FirstId(defs, MetricGroup.Memory, "%", "memory"),
        };

        foreach (var hw in defs.Where(d => d.Group == MetricGroup.Storage).Select(d => d.HardwareName).Distinct())
        {
            var perDisk = defs.Where(d => d.Group == MetricGroup.Storage && d.HardwareName == hw && d.Unit == "%").ToList();
            picks.Add(perDisk.FirstOrDefault(d => d.DisplayName.Contains("activity", StringComparison.OrdinalIgnoreCase))?.Id
                      ?? perDisk.FirstOrDefault()?.Id);
        }

        foreach (var hw in defs.Where(d => d.Group == MetricGroup.Network).Select(d => d.HardwareName).Distinct())
        {
            picks.Add(defs.FirstOrDefault(d => d.Group == MetricGroup.Network && d.HardwareName == hw && d.Unit == "B/s" &&
                (d.DisplayName.Contains("download", StringComparison.OrdinalIgnoreCase) ||
                 d.DisplayName.Contains("received", StringComparison.OrdinalIgnoreCase)))?.Id);
        }

        return picks.Where(p => p is not null).Cast<string>().Distinct().ToList();
    }

    public static List<string> OverlayDefaults(IReadOnlyList<MetricDefinition> defs) =>
        new[]
        {
            FirstId(defs, MetricGroup.Cpu, "°C", "tctl", "package"),
            FirstId(defs, MetricGroup.Gpu, "°C", "core"),
            FirstId(defs, MetricGroup.Gpu, "MHz", "core"),
        }.Where(p => p is not null).Cast<string>().Distinct().ToList();

    private static string? FirstId(IReadOnlyList<MetricDefinition> defs, MetricGroup group, string unit, params string[] preferredNameParts)
    {
        var candidates = defs.Where(d => d.Group == group && d.Unit == unit).ToList();
        foreach (var part in preferredNameParts)
        {
            var hit = candidates.FirstOrDefault(d => d.DisplayName.Contains(part, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Id;
        }
        return candidates.FirstOrDefault()?.Id;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~DefaultSelectorTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: DefaultSelector for out-of-box metric selections"
```

---

### Task 7: AppSettings + SettingsService

**Files:**
- Create: `src/Stats.Core/Settings/AppSettings.cs`, `src/Stats.Core/Settings/SettingsService.cs`
- Test: `tests/Stats.Core.Tests/SettingsServiceTests.cs`

- [ ] **Step 1: Create AppSettings**

`src/Stats.Core/Settings/AppSettings.cs`:

```csharp
namespace Stats.Core.Settings;

public sealed class AppSettings
{
    public double PollIntervalSeconds { get; set; } = 1.0;
    public List<string> DashboardMetrics { get; set; } = new();
    public List<string> OverlayMetrics { get; set; } = new();
    /// <summary>Optional user-entered limits (e.g. PBO PPT watts) keyed by metric id; tile shows "% of limit" when set.</summary>
    public Dictionary<string, float> MetricLimits { get; set; } = new();
    public double OverlayOpacity { get; set; } = 0.85;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}
```

- [ ] **Step 2: Write failing tests**

`tests/Stats.Core.Tests/SettingsServiceTests.cs`:

```csharp
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "StatsTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var svc = new SettingsService(_dir);
        var s = svc.Load();
        Assert.Equal(1.0, s.PollIntervalSeconds);
        Assert.Empty(s.DashboardMetrics);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings
        {
            PollIntervalSeconds = 2.5,
            DashboardMetrics = { "a", "b" },
            OverlayMetrics = { "c" },
            MetricLimits = { ["a"] = 150f },
            OverlayOpacity = 0.5,
            WindowWidth = 1200,
        };
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(2.5, loaded.PollIntervalSeconds);
        Assert.Equal(new[] { "a", "b" }, loaded.DashboardMetrics);
        Assert.Equal(new[] { "c" }, loaded.OverlayMetrics);
        Assert.Equal(150f, loaded.MetricLimits["a"]);
        Assert.Equal(0.5, loaded.OverlayOpacity);
        Assert.Equal(1200, loaded.WindowWidth);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not valid json !!!");
        var s = new SettingsService(_dir).Load();
        Assert.Equal(1.0, s.PollIntervalSeconds);
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(60.0, 5.0)]
    public void Load_ClampsPollIntervalToSpecRange(double stored, double expected)
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { PollIntervalSeconds = stored });
        Assert.Equal(expected, svc.Load().PollIntervalSeconds);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SettingsServiceTests`
Expected: compile error — `SettingsService` not defined.

- [ ] **Step 4: Implement SettingsService**

`src/Stats.Core/Settings/SettingsService.cs`:

```csharp
using System.Text.Json;

namespace Stats.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _directory;

    public SettingsService(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            settings = new AppSettings();
        }
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 0.5, 5.0);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SettingsServiceTests`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: AppSettings and SettingsService with corrupt-file fallback and clamping"
```

---

### Task 8: ISensorReader + SensorPoller

**Files:**
- Create: `src/Stats.Core/Sensors/ISensorReader.cs`, `src/Stats.Core/Sensors/SensorPoller.cs`
- Test: `tests/Stats.Core.Tests/SensorPollerTests.cs`

- [ ] **Step 1: Create ISensorReader**

`src/Stats.Core/Sensors/ISensorReader.cs`:

```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

public interface ISensorReader : IDisposable
{
    string Name { get; }
    /// <summary>True when running on the WMI/PerfCounter fallback (no temps/clocks/power).</summary>
    bool IsDegraded { get; }
    IReadOnlyList<MetricDefinition> Discover();
    SensorSnapshot Read();
}
```

- [ ] **Step 2: Write failing tests**

`tests/Stats.Core.Tests/SensorPollerTests.cs`:

```csharp
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class SensorPollerTests
{
    private sealed class FakeReader : ISensorReader
    {
        public int ReadCount;
        public bool ThrowOnRead;
        public string Name => "Fake";
        public bool IsDegraded => false;
        public IReadOnlyList<MetricDefinition> Discover() => Array.Empty<MetricDefinition>();
        public SensorSnapshot Read()
        {
            ReadCount++;
            if (ThrowOnRead) throw new InvalidOperationException("boom");
            return new SensorSnapshot(new Dictionary<string, float?> { ["x"] = ReadCount }, DateTime.UtcNow);
        }
        public void Dispose() { }
    }

    [Fact]
    public void PollOnce_RaisesEventWithSnapshot()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader);
        SensorSnapshot? received = null;
        poller.SnapshotAvailable += s => received = s;
        poller.PollOnce();
        Assert.NotNull(received);
        Assert.Equal(1f, received!.Values["x"]);
    }

    [Fact]
    public async Task StartStop_PollsRepeatedly_ThenStops()
    {
        var reader = new FakeReader();
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(30) };
        int events = 0;
        poller.SnapshotAvailable += _ => Interlocked.Increment(ref events);
        poller.Start();
        await Task.Delay(400);
        poller.Stop();
        int atStop = events;
        Assert.True(atStop >= 2, $"expected >=2 polls, got {atStop}");
        await Task.Delay(200);
        Assert.Equal(atStop, events); // no polls after Stop
    }

    [Fact]
    public async Task ReaderThrow_DoesNotKillLoop()
    {
        var reader = new FakeReader { ThrowOnRead = true };
        using var poller = new SensorPoller(reader) { Interval = TimeSpan.FromMilliseconds(20) };
        poller.Start();
        await Task.Delay(150);
        poller.Stop();
        Assert.True(reader.ReadCount >= 2, $"loop should survive exceptions, got {reader.ReadCount} reads");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SensorPollerTests`
Expected: compile error — `SensorPoller` not defined.

- [ ] **Step 4: Implement SensorPoller**

`src/Stats.Core/Sensors/SensorPoller.cs`:

```csharp
namespace Stats.Core.Sensors;

/// <summary>Polls an ISensorReader on a background task. Event fires on the background thread — UI must marshal.</summary>
public sealed class SensorPoller : IDisposable
{
    private readonly ISensorReader _reader;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SensorPoller(ISensorReader reader) => _reader = reader;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public event Action<SensorSnapshot>? SnapshotAvailable;

    public SensorSnapshot? PollOnce()
    {
        try
        {
            var snapshot = _reader.Read();
            SnapshotAvailable?.Invoke(snapshot);
            return snapshot;
        }
        catch
        {
            return null; // transient sensor hiccup; next tick retries
        }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                PollOnce();
                try { await Task.Delay(Interval, ct); }
                catch (TaskCanceledException) { break; }
            }
        }, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~SensorPollerTests`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: ISensorReader boundary and SensorPoller background loop"
```

---

### Task 9: LhmSensorReader (LibreHardwareMonitor integration)

**Files:**
- Create: `src/Stats.Core/Sensors/LhmSensorReader.cs`

No unit tests — output depends on physical hardware and admin rights. Verified by build here and by live smoke test in Task 15. All mapping logic it delegates to is already covered by SensorMapperTests.

- [ ] **Step 1: Implement**

`src/Stats.Core/Sensors/LhmSensorReader.cs`:

```csharp
using LibreHardwareMonitor.Hardware;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Wraps LibreHardwareMonitor. Requires admin for the kernel driver (CPU temps/power/clocks).</summary>
public sealed class LhmSensorReader : ISensorReader
{
    private readonly Computer _computer;
    private readonly List<(ISensor Sensor, string MetricId)> _map = new();
    private List<MetricDefinition> _definitions = new();

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
        };
        _computer.Open();
    }

    public string Name => "LibreHardwareMonitor";
    public bool IsDegraded => false;

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
        for (int i = 0; i < sensors.Count; i++)
        {
            if (defs[i] is not MetricDefinition def) continue;
            _map.Add((sensors[i], def.Id));
            _definitions.Add(def);
        }
        return _definitions;
    }

    public SensorSnapshot Read()
    {
        UpdateAll();
        var values = new Dictionary<string, float?>(_map.Count);
        foreach (var (sensor, id) in _map)
            values[id] = sensor.Value;
        return new SensorSnapshot(values, DateTime.UtcNow);
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

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: LhmSensorReader wrapping LibreHardwareMonitor"
```

---

### Task 10: PerfCounterSensorReader (degraded fallback)

**Files:**
- Create: `src/Stats.Core/Sensors/PerfCounterSensorReader.cs`

No unit tests — PerformanceCounter categories are machine/locale dependent. Each category guarded individually; verified live in Task 15. Provides: CPU total + per-core load, memory used, per-disk activity, per-adapter throughput. No temps/clocks/power (that's what "degraded" means).

- [ ] **Step 1: Implement**

`src/Stats.Core/Sensors/PerfCounterSensorReader.cs`:

```csharp
using System.Diagnostics;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Windows performance-counter fallback used when the LHM driver can't initialize.</summary>
public sealed class PerfCounterSensorReader : ISensorReader
{
    private readonly List<(MetricDefinition Def, PerformanceCounter Counter)> _counters = new();
    private readonly List<MetricDefinition> _definitions = new();
    private PerformanceCounter? _memAvailable;
    private MetricDefinition? _memUsedDef;
    private readonly double _totalMemoryGb =
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1_000_000_000.0;

    public string Name => "Performance Counters (degraded)";
    public bool IsDegraded => true;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        TryAdd(() =>
        {
            Add(new MetricDefinition("cpu.perf.load.total", "CPU Total", MetricGroup.Cpu, "CPU", "%"),
                new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true));
            for (int i = 0; i < Environment.ProcessorCount; i++)
                Add(new MetricDefinition($"cpu.perf.load.core{i}", $"CPU Core #{i}", MetricGroup.Cpu, "CPU", "%"),
                    new PerformanceCounter("Processor", "% Processor Time", i.ToString(), readOnly: true));
        });

        TryAdd(() =>
        {
            _memAvailable = new PerformanceCounter("Memory", "Available Bytes", readOnly: true);
            _memUsedDef = new MetricDefinition("mem.perf.used", "Memory Used", MetricGroup.Memory, "Memory", "GB", "F1");
            _definitions.Add(_memUsedDef);
        });

        TryAdd(() =>
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            foreach (var instance in category.GetInstanceNames().Where(n => n != "_Total").OrderBy(n => n))
                Add(new MetricDefinition($"disk.perf.{SensorMapper.Slug(instance)}.activity", $"{instance} · Activity",
                        MetricGroup.Storage, instance, "%"),
                    new PerformanceCounter("PhysicalDisk", "% Disk Time", instance, readOnly: true));
        });

        TryAdd(() =>
        {
            var category = new PerformanceCounterCategory("Network Interface");
            foreach (var instance in category.GetInstanceNames().OrderBy(n => n))
            {
                Add(new MetricDefinition($"net.perf.{SensorMapper.Slug(instance)}.down", $"{instance} · Download",
                        MetricGroup.Network, instance, "B/s"),
                    new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, readOnly: true));
                Add(new MetricDefinition($"net.perf.{SensorMapper.Slug(instance)}.up", $"{instance} · Upload",
                        MetricGroup.Network, instance, "B/s"),
                    new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, readOnly: true));
            }
        });

        return _definitions;
    }

    public SensorSnapshot Read()
    {
        var values = new Dictionary<string, float?>(_counters.Count + 1);
        foreach (var (def, counter) in _counters)
        {
            try { values[def.Id] = counter.NextValue(); }
            catch { values[def.Id] = null; }
        }
        if (_memAvailable is not null && _memUsedDef is not null)
        {
            try { values[_memUsedDef.Id] = (float)(_totalMemoryGb - _memAvailable.NextValue() / 1_000_000_000.0); }
            catch { values[_memUsedDef.Id] = null; }
        }
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    private void Add(MetricDefinition def, PerformanceCounter counter)
    {
        _counters.Add((def, counter));
        _definitions.Add(def);
    }

    private static void TryAdd(Action action)
    {
        try { action(); }
        catch { /* category unavailable on this machine — skip it */ }
    }

    public void Dispose()
    {
        foreach (var (_, counter) in _counters) counter.Dispose();
        _memAvailable?.Dispose();
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: PerfCounterSensorReader degraded fallback"
```

---

### Task 11: ViewModels

**Files:**
- Create: `src/Stats.Core/ViewModels/MetricTileViewModel.cs`, `src/Stats.Core/ViewModels/MetricPickerItem.cs`, `src/Stats.Core/ViewModels/DashboardViewModel.cs`, `src/Stats.Core/ViewModels/OverlayViewModel.cs`
- Test: `tests/Stats.Core.Tests/ViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/Stats.Core.Tests/ViewModelTests.cs`:

```csharp
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class ViewModelTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition CpuPpt = new("cpu.ppt", "CPU PPT", MetricGroup.Cpu, "CPU", "W", "F1");
    private static readonly MetricDefinition GpuClock = new("gpu.clock", "GPU Core", MetricGroup.Gpu, "GPU", "MHz");

    private static MetricStore NewStore() => new(new[] { CpuTemp, CpuPpt, GpuClock });

    private static void Push(MetricStore store, float temp, float ppt, float clock) =>
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        { ["cpu.temp"] = temp, ["cpu.ppt"] = ppt, ["gpu.clock"] = clock }, DateTime.UtcNow));

    [Fact]
    public void Tile_Refresh_FormatsCurrentMinMax()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        Push(store, 45f, 71f, 2500f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"]);
        tile.Refresh();
        Assert.Equal("45.0 °C", tile.CurrentText);
        Assert.Contains("40.0", tile.MinMaxText);
        Assert.Contains("45.0", tile.MinMaxText);
        Assert.Equal(2, tile.HistoryValues.Length);
    }

    [Fact]
    public void Tile_WithLimit_ShowsPercentOfLimit()
    {
        var store = NewStore();
        Push(store, 40f, 75f, 2400f);
        var tile = new MetricTileViewModel(CpuPpt, store["cpu.ppt"], limit: 150f);
        tile.Refresh();
        Assert.Equal("50% of 150.0 W", tile.LimitText);
    }

    [Fact]
    public void Dashboard_BuildsTilesOnlyForSelectedMetrics_InDefinitionOrder()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "gpu.clock", "cpu.temp" } };
        var vm = new DashboardViewModel(store, settings, () => { });
        Assert.Equal(new[] { "cpu.temp", "gpu.clock" }, vm.Tiles.Select(t => t.Definition.Id));
    }

    [Fact]
    public void Picker_Uncheck_RemovesTileAndUpdatesSettingsAndSaves()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "cpu.temp" } };
        int saves = 0;
        var vm = new DashboardViewModel(store, settings, () => saves++);
        var item = vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp");

        item.IsChecked = false;
        Assert.Empty(vm.Tiles);
        Assert.Empty(settings.DashboardMetrics);
        Assert.Equal(1, saves);

        item.IsChecked = true;
        Assert.Single(vm.Tiles);
        Assert.Contains("cpu.temp", settings.DashboardMetrics);
    }

    [Fact]
    public void Picker_OverlayToggle_UpdatesOverlaySettingsAndRaisesEvent()
    {
        var store = NewStore();
        var settings = new AppSettings();
        var vm = new DashboardViewModel(store, settings, () => { });
        int raised = 0;
        vm.OverlayMetricsChanged += () => raised++;

        vm.PickerItems.Single(p => p.Definition.Id == "gpu.clock").IsOnOverlay = true;
        Assert.Contains("gpu.clock", settings.OverlayMetrics);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Overlay_Rebuild_TracksSettings()
    {
        var store = NewStore();
        var settings = new AppSettings { OverlayMetrics = { "cpu.temp" } };
        var vm = new OverlayViewModel(store, settings);
        Assert.Single(vm.Tiles);
        settings.OverlayMetrics.Add("gpu.clock");
        vm.Rebuild();
        Assert.Equal(2, vm.Tiles.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~ViewModelTests`
Expected: compile error — ViewModels not defined.

- [ ] **Step 3: Implement ViewModels**

`src/Stats.Core/ViewModels/MetricTileViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class MetricTileViewModel : ObservableObject
{
    private readonly MetricHistory _history;
    private readonly float? _limit;

    public MetricTileViewModel(MetricDefinition definition, MetricHistory history, float? limit = null)
    {
        Definition = definition;
        _history = history;
        _limit = limit;
    }

    public MetricDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string GroupName => Definition.Group.ToString();

    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private string _minMaxText = "";
    [ObservableProperty] private string _limitText = "";
    [ObservableProperty] private float[] _historyValues = Array.Empty<float>();

    public void Refresh()
    {
        CurrentText = ValueFormatter.Format(Definition, _history.Current);
        MinMaxText = float.IsNaN(_history.SessionMin)
            ? ""
            : $"min {ValueFormatter.Format(Definition, _history.SessionMin)}   max {ValueFormatter.Format(Definition, _history.SessionMax)}";
        LimitText = _limit is float limit && _history.Current is float current
            ? $"{current / limit * 100:F0}% of {ValueFormatter.Format(Definition, limit)}"
            : "";
        HistoryValues = _history.ToArray();
    }
}
```

`src/Stats.Core/ViewModels/MetricPickerItem.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class MetricPickerItem : ObservableObject
{
    public MetricPickerItem(MetricDefinition definition, bool isChecked, bool isOnOverlay)
    {
        Definition = definition;
        _isChecked = isChecked;
        _isOnOverlay = isOnOverlay;
    }

    public MetricDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string GroupName => $"{Definition.Group} — {Definition.HardwareName}";

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isOnOverlay;
}
```

`src/Stats.Core/ViewModels/DashboardViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public DashboardViewModel(MetricStore store, AppSettings settings, Action saveSettings)
    {
        _store = store;
        _settings = settings;
        _saveSettings = saveSettings;

        foreach (var def in store.Definitions)
        {
            var item = new MetricPickerItem(def,
                settings.DashboardMetrics.Contains(def.Id),
                settings.OverlayMetrics.Contains(def.Id));
            item.PropertyChanged += OnPickerItemChanged;
            PickerItems.Add(item);
        }
        RebuildTiles();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();
    public List<MetricPickerItem> PickerItems { get; } = new();

    public event Action? OverlayMetricsChanged;

    [ObservableProperty] private bool _isDegraded;
    [ObservableProperty] private bool _isPickerOpen;

    [RelayCommand]
    private void TogglePicker() => IsPickerOpen = !IsPickerOpen;

    public void RefreshAll()
    {
        foreach (var tile in Tiles) tile.Refresh();
    }

    private void OnPickerItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MetricPickerItem item) return;

        if (e.PropertyName == nameof(MetricPickerItem.IsChecked))
        {
            if (item.IsChecked && !_settings.DashboardMetrics.Contains(item.Definition.Id))
                _settings.DashboardMetrics.Add(item.Definition.Id);
            else if (!item.IsChecked)
                _settings.DashboardMetrics.Remove(item.Definition.Id);
            RebuildTiles();
            _saveSettings();
        }
        else if (e.PropertyName == nameof(MetricPickerItem.IsOnOverlay))
        {
            if (item.IsOnOverlay && !_settings.OverlayMetrics.Contains(item.Definition.Id))
                _settings.OverlayMetrics.Add(item.Definition.Id);
            else if (!item.IsOnOverlay)
                _settings.OverlayMetrics.Remove(item.Definition.Id);
            OverlayMetricsChanged?.Invoke();
            _saveSettings();
        }
    }

    private void RebuildTiles()
    {
        Tiles.Clear();
        var selected = _settings.DashboardMetrics.ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            float? limit = _settings.MetricLimits.TryGetValue(def.Id, out var l) ? l : null;
            var tile = new MetricTileViewModel(def, _store[def.Id], limit);
            tile.Refresh();
            Tiles.Add(tile);
        }
    }
}
```

`src/Stats.Core/ViewModels/OverlayViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed class OverlayViewModel
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;

    public OverlayViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        Rebuild();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();

    public void Rebuild()
    {
        Tiles.Clear();
        var selected = _settings.OverlayMetrics.ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            var tile = new MetricTileViewModel(def, _store[def.Id]);
            tile.Refresh();
            Tiles.Add(tile);
        }
    }

    public void RefreshAll()
    {
        foreach (var tile in Tiles) tile.Refresh();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Stats.Core.Tests --filter FullyQualifiedName~ViewModelTests`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Run full test suite (regression)**

Run: `dotnet test`
Expected: all tests pass, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: dashboard, overlay, tile, and picker ViewModels"
```

---

### Task 12: Dark theme, Sparkline control, DashboardWindow, minimal composition root

App becomes runnable at the end of this task (dashboard only; picker/overlay/tray follow).

**Files:**
- Create: `src/Stats.App/Controls/Sparkline.cs`, `src/Stats.App/Views/DashboardWindow.xaml`, `src/Stats.App/Views/DashboardWindow.xaml.cs`
- Modify: `src/Stats.App/App.xaml`, `src/Stats.App/App.xaml.cs`
- Delete: `src/Stats.App/MainWindow.xaml`, `src/Stats.App/MainWindow.xaml.cs`

- [ ] **Step 1: Sparkline control**

`src/Stats.App/Controls/Sparkline.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace Stats.App.Controls;

/// <summary>Minimal polyline sparkline. Auto-scales vertically to the window of values it is given.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(float[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[]? Values
    {
        get => (float[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        double w = ActualWidth, h = ActualHeight;
        if (values is null || values.Length < 2 || w <= 0 || h <= 0) return;

        float min = values.Min(), max = values.Max();
        float range = max - min;
        if (range < 1e-6f) range = 1f;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < values.Length; i++)
            {
                double x = w * i / (values.Length - 1);
                double y = h - 2 - (values[i] - min) / range * (h - 4);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.5), geometry);
    }
}
```

- [ ] **Step 2: Theme resources in App.xaml**

Replace `src/Stats.App/App.xaml` (note: no `StartupUri` — startup is code-driven):

```xml
<Application x:Class="Stats.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <SolidColorBrush x:Key="WindowBg" Color="#FF1B1B1C"/>
        <SolidColorBrush x:Key="TileBg" Color="#FF252528"/>
        <SolidColorBrush x:Key="FlyoutBg" Color="#FF2B2B2F"/>
        <SolidColorBrush x:Key="TextPrimary" Color="#FFF0F0F0"/>
        <SolidColorBrush x:Key="TextSecondary" Color="#FF9A9A9E"/>
        <SolidColorBrush x:Key="AccentBrush" Color="#FFE68A2E"/>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>

        <Style TargetType="TextBlock" x:Key="GroupHeader">
            <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Margin" Value="8,14,0,4"/>
        </Style>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: DashboardWindow**

`src/Stats.App/Views/DashboardWindow.xaml`:

```xml
<Window x:Class="Stats.App.Views.DashboardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:Stats.App.Controls"
        xmlns:vm="clr-namespace:Stats.Core.ViewModels;assembly=Stats.Core"
        Title="Stats" Width="1180" Height="720"
        Background="{StaticResource WindowBg}">
    <Window.Resources>
        <DataTemplate DataType="{x:Type vm:MetricTileViewModel}">
            <Border Width="215" Height="120" Background="{StaticResource TileBg}"
                    CornerRadius="6" Margin="6" Padding="10">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="{Binding DisplayName}"
                               Foreground="{StaticResource TextSecondary}" FontSize="11"
                               TextTrimming="CharacterEllipsis" ToolTip="{Binding DisplayName}"/>
                    <TextBlock DockPanel.Dock="Top" Text="{Binding CurrentText}"
                               Foreground="{StaticResource TextPrimary}" FontSize="26"
                               FontWeight="SemiBold" Margin="0,2,0,0"/>
                    <TextBlock DockPanel.Dock="Top" Text="{Binding LimitText}"
                               Foreground="{StaticResource AccentBrush}" FontSize="11"/>
                    <TextBlock DockPanel.Dock="Bottom" Text="{Binding MinMaxText}"
                               Foreground="{StaticResource TextSecondary}" FontSize="10"/>
                    <controls:Sparkline Values="{Binding HistoryValues}"
                                        Stroke="{StaticResource AccentBrush}" Margin="0,4"/>
                </DockPanel>
            </Border>
        </DataTemplate>
    </Window.Resources>

    <DockPanel>
        <Border DockPanel.Dock="Top" Background="#7A2D2D" Padding="10,6"
                Visibility="{Binding IsDegraded, Converter={StaticResource BoolToVis}}">
            <TextBlock Foreground="White" TextWrapping="Wrap"
                       Text="Sensor driver unavailable — degraded mode (loads and usage only; no temperatures, clocks, power, or voltages). Try running as administrator."/>
        </Border>

        <Border DockPanel.Dock="Top" Padding="14,10">
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="⚙  Metrics"
                        Command="{Binding TogglePickerCommand}"
                        Padding="10,4" Background="{StaticResource TileBg}"
                        Foreground="{StaticResource TextPrimary}" BorderThickness="0"/>
                <TextBlock Text="Stats" FontSize="20" FontWeight="Bold"
                           Foreground="{StaticResource TextPrimary}"/>
            </DockPanel>
        </Border>

        <Grid>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl x:Name="TileList">
                    <ItemsControl.GroupStyle>
                        <GroupStyle>
                            <GroupStyle.HeaderTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Name}" Style="{StaticResource GroupHeader}"/>
                                </DataTemplate>
                            </GroupStyle.HeaderTemplate>
                        </GroupStyle>
                    </ItemsControl.GroupStyle>
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                </ItemsControl>
            </ScrollViewer>

            <!-- Metric picker flyout: filled in Task 13 -->
            <Border x:Name="PickerFlyout" HorizontalAlignment="Right" Width="380"
                    Background="{StaticResource FlyoutBg}"
                    Visibility="{Binding IsPickerOpen, Converter={StaticResource BoolToVis}}"/>
        </Grid>
    </DockPanel>
</Window>
```

`src/Stats.App/Views/DashboardWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Stats.Core.ViewModels;

namespace Stats.App.Views;

public partial class DashboardWindow : Window
{
    /// <summary>Set by App: true only when exiting via tray menu; otherwise close hides to tray.</summary>
    public bool AllowClose { get; set; }

    public DashboardWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not DashboardViewModel vm) return;
            var view = new ListCollectionView(vm.Tiles);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricTileViewModel.GroupName)));
            TileList.ItemsSource = view;
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
```

Note: `ListCollectionView` over an `ObservableCollection` tracks adds/removes automatically, so picker changes reflow groups without extra code.

- [ ] **Step 4: Minimal composition root**

Delete `src/Stats.App/MainWindow.xaml` and `src/Stats.App/MainWindow.xaml.cs`.

Replace `src/Stats.App/App.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using Stats.App.Views;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private AppSettings? _settings;
    private ISensorReader? _reader;
    private MetricStore? _store;
    private SensorPoller? _poller;
    private DashboardWindow? _dashboard;
    private DashboardViewModel? _dashboardVm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats");
        _settingsService = new SettingsService(settingsDir);
        _settings = _settingsService.Load();

        try
        {
            _reader = new LhmSensorReader();
        }
        catch (Exception)
        {
            _reader = new PerfCounterSensorReader();
        }

        var definitions = _reader.Discover();
        if (_settings.DashboardMetrics.Count == 0)
            _settings.DashboardMetrics = DefaultSelector.DashboardDefaults(definitions);
        if (_settings.OverlayMetrics.Count == 0)
            _settings.OverlayMetrics = DefaultSelector.OverlayDefaults(definitions);

        _store = new MetricStore(definitions);
        _poller = new SensorPoller(_reader)
        {
            Interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds),
        };

        _dashboardVm = new DashboardViewModel(_store, _settings, SaveSettings)
        {
            IsDegraded = _reader.IsDegraded,
        };

        _dashboard = new DashboardWindow { DataContext = _dashboardVm };
        RestoreWindowBounds();

        _poller.SnapshotAvailable += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _store.Apply(snapshot);
            _dashboardVm.RefreshAll();
        });

        _dashboard.AllowClose = true; // becomes false when tray lands (Task 14)
        _dashboard.Closing += (_, _) => SaveWindowBounds();
        _dashboard.Closed += (_, _) => Shutdown();
        _dashboard.Show();
        _poller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poller?.Dispose();
        _reader?.Dispose();
        SaveSettings();
        base.OnExit(e);
    }

    private void SaveSettings()
    {
        if (_settings is not null) _settingsService?.Save(_settings);
    }

    private void RestoreWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        if (_settings.WindowLeft is double left) _dashboard.Left = left;
        if (_settings.WindowTop is double top) _dashboard.Top = top;
        if (_settings.WindowWidth is double width) _dashboard.Width = width;
        if (_settings.WindowHeight is double height) _dashboard.Height = height;
    }

    private void SaveWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        _settings.WindowLeft = _dashboard.Left;
        _settings.WindowTop = _dashboard.Top;
        _settings.WindowWidth = _dashboard.Width;
        _settings.WindowHeight = _dashboard.Height;
    }
}
```

- [ ] **Step 5: Build and manual smoke**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`

Manual (needs elevation for real sensors): run `src\Stats.App\bin\Debug\net8.0-windows\Stats.App.exe` from an admin terminal. Expected: dark window, grouped tiles with live values updating every second, sparklines drawing after a few seconds. Non-admin launch: LHM loads with reduced sensors — still runs.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: dark dashboard window with sparkline tiles and live polling"
```

---

### Task 13: Metric picker flyout

**Files:**
- Modify: `src/Stats.App/Views/DashboardWindow.xaml` (fill `PickerFlyout`)
- Modify: `src/Stats.App/Views/DashboardWindow.xaml.cs` (grouped picker view)

- [ ] **Step 1: Fill the flyout**

In `DashboardWindow.xaml`, replace the self-closing `<Border x:Name="PickerFlyout" ... />` with:

```xml
<Border x:Name="PickerFlyout" HorizontalAlignment="Right" Width="380"
        Background="{StaticResource FlyoutBg}"
        Visibility="{Binding IsPickerOpen, Converter={StaticResource BoolToVis}}">
    <DockPanel Margin="12">
        <Grid DockPanel.Dock="Top" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="46"/>
                <ColumnDefinition Width="52"/>
            </Grid.ColumnDefinitions>
            <TextBlock Text="Metrics" FontSize="16" FontWeight="Bold"
                       Foreground="{StaticResource TextPrimary}"/>
            <TextBlock Grid.Column="1" Text="Dash" FontSize="11"
                       Foreground="{StaticResource TextSecondary}" VerticalAlignment="Bottom"/>
            <TextBlock Grid.Column="2" Text="Overlay" FontSize="11"
                       Foreground="{StaticResource TextSecondary}" VerticalAlignment="Bottom"/>
        </Grid>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl x:Name="PickerList">
                <ItemsControl.GroupStyle>
                    <GroupStyle>
                        <GroupStyle.HeaderTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Name}" Style="{StaticResource GroupHeader}" Margin="0,10,0,2"/>
                            </DataTemplate>
                        </GroupStyle.HeaderTemplate>
                    </GroupStyle>
                </ItemsControl.GroupStyle>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="0,1">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="46"/>
                                <ColumnDefinition Width="52"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{Binding DisplayName}" FontSize="12"
                                       Foreground="{StaticResource TextPrimary}"
                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding DisplayName}"
                                       VerticalAlignment="Center"/>
                            <CheckBox Grid.Column="1" IsChecked="{Binding IsChecked}" HorizontalAlignment="Center"/>
                            <CheckBox Grid.Column="2" IsChecked="{Binding IsOnOverlay}" HorizontalAlignment="Center"/>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Border>
```

- [ ] **Step 2: Bind grouped picker view in code-behind**

In `DashboardWindow.xaml.cs`, inside the `DataContextChanged` handler after the tile view setup, add:

```csharp
var pickerView = new ListCollectionView(vm.PickerItems);
pickerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricPickerItem.GroupName)));
PickerList.ItemsSource = pickerView;
```

- [ ] **Step 3: Build and manual smoke**

Run: `dotnet build`
Expected: build succeeds. Manual: launch, click "⚙ Metrics" — flyout lists every discovered sensor grouped by hardware; unchecking "Dash" removes a tile immediately; restart preserves choices (check `%AppData%\Stats\settings.json` updated).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: metric picker flyout with dashboard and overlay columns"
```

---

### Task 14: OverlayWindow + tray icon + close-to-tray

**Files:**
- Create: `src/Stats.App/Views/OverlayWindow.xaml`, `src/Stats.App/Views/OverlayWindow.xaml.cs`
- Modify: `src/Stats.App/App.xaml.cs`

- [ ] **Step 1: OverlayWindow**

`src/Stats.App/Views/OverlayWindow.xaml`:

```xml
<Window x:Class="Stats.App.Views.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" SizeToContent="WidthAndHeight"
        ResizeMode="NoResize">
    <Border Background="#E01B1B1C" CornerRadius="8" Padding="12,8">
        <ItemsControl ItemsSource="{Binding Tiles}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <StackPanel Margin="10,0">
                        <TextBlock Text="{Binding DisplayName}" FontSize="10"
                                   Foreground="{StaticResource TextSecondary}"
                                   TextTrimming="CharacterEllipsis" MaxWidth="120"/>
                        <TextBlock Text="{Binding CurrentText}" FontSize="16" FontWeight="SemiBold"
                                   Foreground="{StaticResource TextPrimary}"/>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Border>
</Window>
```

`src/Stats.App/Views/OverlayWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace Stats.App.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, _) => DragMove();
    }
}
```

- [ ] **Step 2: Wire overlay + tray into App**

In `src/Stats.App/App.xaml.cs`:

Add usings at top:

```csharp
using System.Windows.Controls;
using H.NotifyIcon;
using Stats.Core.ViewModels;
```

Add fields:

```csharp
    private OverlayWindow? _overlay;
    private OverlayViewModel? _overlayVm;
    private TaskbarIcon? _tray;
    private string? _trayCpuTempId;
    private string? _trayGpuTempId;
```

In `OnStartup`, after `_dashboardVm` creation, add:

```csharp
        _overlayVm = new OverlayViewModel(_store, _settings);
        _overlay = new OverlayWindow
        {
            DataContext = _overlayVm,
            Opacity = _settings.OverlayOpacity,
        };
        if (_settings.OverlayLeft is double ol) _overlay.Left = ol;
        if (_settings.OverlayTop is double ot) _overlay.Top = ot;
        _overlay.LocationChanged += (_, _) =>
        {
            _settings.OverlayLeft = _overlay.Left;
            _settings.OverlayTop = _overlay.Top;
        };
        _dashboardVm.OverlayMetricsChanged += () => _overlayVm.Rebuild();

        _trayCpuTempId = DefaultSelector.OverlayDefaults(definitions).FirstOrDefault();
        _trayGpuTempId = definitions.FirstOrDefault(d =>
            d.Group == MetricGroup.Gpu && d.Unit == "°C")?.Id;
        SetupTray();
```

Replace the existing snapshot handler body with:

```csharp
        _poller.SnapshotAvailable += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _store.Apply(snapshot);
            _dashboardVm.RefreshAll();
            _overlayVm?.RefreshAll();
            UpdateTrayTooltip();
        });
```

Replace `_dashboard.AllowClose = true;` and the `Closed` handler lines with:

```csharp
        _dashboard.AllowClose = false; // close button hides to tray; exit via tray menu
        _dashboard.Closing += (_, _) => SaveWindowBounds();
```

Add methods:

```csharp
    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Stats",
            Icon = System.Drawing.SystemIcons.Application,
        };
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open dashboard" };
        open.Click += (_, _) => ShowDashboard();
        var overlay = new MenuItem { Header = "Toggle overlay" };
        overlay.Click += (_, _) => ToggleOverlay();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(open);
        menu.Items.Add(overlay);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _tray.ContextMenu = menu;
        _tray.TrayLeftMouseUp += (_, _) => ShowDashboard();
    }

    private void UpdateTrayTooltip()
    {
        if (_tray is null || _store is null) return;
        string Part(string? id, string label) =>
            id is not null && _store.TryGet(id, out var h) && h.Current is float v
                ? $"{label} {v:F0}°C" : "";
        var text = $"Stats  {Part(_trayCpuTempId, "CPU")}  {Part(_trayGpuTempId, "GPU")}".Trim();
        _tray.ToolTipText = text.Length > 0 ? text : "Stats";
    }

    private void ShowDashboard()
    {
        if (_dashboard is null) return;
        _dashboard.Show();
        _dashboard.WindowState = WindowState.Normal;
        _dashboard.Activate();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.Hide();
        else _overlay.Show();
    }

    private void ExitApp()
    {
        if (_dashboard is not null) _dashboard.AllowClose = true;
        _tray?.Dispose();
        SaveWindowBounds();
        Shutdown();
    }
```

Note: `_trayCpuTempId` reuses `OverlayDefaults`' first pick (CPU temp) — id heuristics live in one place.

- [ ] **Step 3: Build and manual smoke**

Run: `dotnet build`
Expected: build succeeds. Manual: tray icon appears; closing dashboard hides it (app keeps running); tray left-click reopens; "Toggle overlay" shows draggable topmost strip with CPU/GPU temp + GPU clock; tray "Exit" quits fully; tooltip shows temps after a tick.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: always-on-top overlay, tray icon, close-to-tray"
```

---

### Task 15: Admin manifest + README + final verification

**Files:**
- Create: `src/Stats.App/app.manifest`, `README.md`
- Modify: `src/Stats.App/Stats.App.csproj`

- [ ] **Step 1: Add admin manifest**

`src/Stats.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

In `src/Stats.App/Stats.App.csproj`, inside the first `<PropertyGroup>`, add:

```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
```

- [ ] **Step 2: README**

`README.md`:

```markdown
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
```

- [ ] **Step 3: Full verification**

```bash
dotnet test
```

Expected: all tests pass, 0 failed.

```bash
dotnet build -c Release
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`

- [ ] **Step 4: Manual smoke checklist (live hardware, run as admin)**

Launch `src\Stats.App\bin\Release\net8.0-windows\Stats.App.exe` (accept UAC) and verify against spec success criteria:

1. CPU group shows per-core clocks, Tctl/Tdie temp, package power, PPT (if exposed), voltages.
2. GPU group shows core/memory clock, temp, hotspot, fan, power, VRAM for the RTX 5070 Ti.
3. All 5 disks appear; both Ethernet adapters appear.
4. Unchecking a metric removes its tile; restart preserves the choice.
5. Overlay stays on top of a fullscreen-windowed app and drags smoothly.
6. Sparklines scroll; min/max footers update.
7. Task Manager: Stats.App CPU usage low single digits at 1 s poll.

Record any failures as issues; do not silently patch during smoke.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: admin manifest, README, release verification"
```

---

## Self-review (done at plan time)

- **Spec coverage:** every spec bullet maps to a task — metric groups (T4/T9/T10), history graphs + min/max/avg (T2/T12), picker (T11/T13), tray (T14), overlay (T11/T14), settings (T7), degraded fallback (T10/T12 banner), admin manifest (T15), defaults (T6). PPT %-of-limit deviation documented in header.
- **Placeholder scan:** none — every code step has full code.
- **Type consistency:** `MetricDefinition(Id, DisplayName, Group, HardwareName, Unit, Format)` used identically in Tasks 2–11; `ISensorReader.{Name,IsDegraded,Discover,Read}` consistent across T8–T10; `AllowClose` introduced T12, consumed T14.
