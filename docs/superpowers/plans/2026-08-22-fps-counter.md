# FPS Counter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the foreground game's FPS, 1% low FPS and frame time as ordinary Stats metrics, sourced from a bundled PresentMon child process that only runs while one of those metrics is selected.

**Architecture:** New `Stats.Core/Frames/` namespace: `PresentMonProcess` (child process, stdout CSV) → `PresentMonCsvParser` (header-driven, yields `FrameSample`) → `FrameStatsAggregator` (per-PID ring buffers, FPS / p99 math) → `FrameRateReader : ISensorReader` (foreground-PID lookup, start/stop lifecycle). A `CompositeSensorReader` merges it with the existing LHM/perf-counter reader so `SensorPoller`, `MetricStore`, tiles and overlay are untouched. `build.ps1` fetches the pinned PresentMon exe and ships it next to `Stats.App.exe`.

**Tech Stack:** .NET 8 / C# 12, WPF app (unchanged), xunit 2.9, Intel PresentMon 2.5.1 console exe, Inno Setup 6 (unchanged script), PowerShell build script.

**Spec:** `docs/superpowers/specs/2026-08-22-fps-counter-design.md`

## Global Constraints

- Target framework `net8.0-windows`; `Nullable` and `ImplicitUsings` enabled; no new NuGet packages.
- PresentMon pinned: `PresentMon-2.5.1-x64.exe`, 956,768 bytes, SHA-256 `9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191`, URL `https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe`. Shipped as `{app}\PresentMon.exe`.
- Launch flags (verbatim): `--output_stdout --no_console_stats --stop_existing_session --session_name StatsFps --no_track_gpu --no_track_input --exclude Stats.App.exe`.
- Metric ids `fps.avg`, `fps.low1`, `fps.frametime`; group `MetricGroup.Game` appended **last** in the enum; units `fps`, `fps`, `ms`; formats `F0`, `F0`, `F1`; HardwareName `Foreground app`.
- Thresholds: FPS never reported as window count < 10; 1% low null until ≥ 100 frames; ring buffer 1000 frames/PID; stale PID pruned after 10 s; restart backoff 1 s → 5 s → 30 s → give up; exit code 6 or stderr containing "access denied" → no restart.
- No default selections, no default threshold rules, no new settings.
- Tests: `dotnet test` must stay green after every task. Test classes live flat in `tests/Stats.Core.Tests/` like the existing ones, namespace `Stats.Core.Tests`, `using Xunit` is global.
- Commit messages follow the repo style (`feat(core): …`, `test(core): …`, `build(installer): …`, `docs: …`) and end with the two trailer lines the session uses:
  ```
  Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01XaNQkVXyDTEeBUyRZz22GE
  ```
- **Environment gotcha:** the Claude Code shell is the Store (MSIX) build of PowerShell; processes it starts inherit package identity and are denied ETW sessions. Workers must **not** try to run `PresentMon.exe` or exercise tracing; all process-level behaviour is verified manually by the user from a Start-menu launch (Task 10).

---

### Task 1: `MetricGroup.Game` and the frame metric definitions

**Files:**
- Modify: `src/Stats.Core/Metrics/MetricGroup.cs`
- Modify: `src/Stats.Core/ViewModels/DashboardViewModel.cs:12-13`
- Create: `src/Stats.Core/Frames/FrameMetrics.cs`
- Test: `tests/Stats.Core.Tests/FrameMetricsTests.cs`

**Interfaces:**
- Produces: `MetricGroup.Game`; `static class FrameMetrics { const string FpsId="fps.avg", LowId="fps.low1", FrameTimeId="fps.frametime"; const string IdPrefix="fps."; static IReadOnlyList<MetricDefinition> Definitions; static bool IsFrameMetric(string id); }`

- [ ] **Step 1: Write the failing test**

`tests/Stats.Core.Tests/FrameMetricsTests.cs`:
```csharp
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class FrameMetricsTests
{
    [Fact]
    public void Definitions_ThreeGameMetrics_WithExpectedIdsUnitsFormats()
    {
        var defs = FrameMetrics.Definitions;
        Assert.Equal(new[] { "fps.avg", "fps.low1", "fps.frametime" }, defs.Select(d => d.Id));
        Assert.All(defs, d => Assert.Equal(MetricGroup.Game, d.Group));
        Assert.All(defs, d => Assert.Equal("Foreground app", d.HardwareName));
        Assert.Equal(("fps", "F0"), (defs[0].Unit, defs[0].Format));
        Assert.Equal(("fps", "F0"), (defs[1].Unit, defs[1].Format));
        Assert.Equal(("ms", "F1"), (defs[2].Unit, defs[2].Format));
    }

    [Theory]
    [InlineData("fps.avg", true)]
    [InlineData("fps.frametime", true)]
    [InlineData("cpu.temp", false)]
    [InlineData("", false)]
    public void IsFrameMetric_MatchesPrefix(string id, bool expected) =>
        Assert.Equal(expected, FrameMetrics.IsFrameMetric(id));

    [Fact]
    public void Game_IsLastEnumMember_SoSerializedRulesStayStable() =>
        Assert.Equal(Enum.GetValues<MetricGroup>().Max(), MetricGroup.Game);

    [Fact]
    public void DashboardViewModel_PlacesGameSectionLast()
    {
        var defs = new List<MetricDefinition>
        {
            new("cpu.l", "CPU Total", MetricGroup.Cpu, "CPU", "%"),
            new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps"),
            new("net.d", "Eth · Download", MetricGroup.Network, "Eth", "B/s"),
        };
        var store = new MetricStore(defs);
        var s = new AppSettings { DashboardMetrics = new() { "fps.avg", "net.d", "cpu.l" } };
        var vm = new DashboardViewModel(store, s, () => { });
        Assert.Equal(new[] { "Cpu", "Network", "Game" }, vm.Sections.Select(x => x.Name));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FrameMetricsTests" --nologo`
Expected: build error — `Stats.Core.Frames` / `MetricGroup.Game` do not exist.

- [ ] **Step 3: Implement**

`src/Stats.Core/Metrics/MetricGroup.cs`:
```csharp
namespace Stats.Core.Metrics;

// Append only: ThresholdRule serializes this enum (as a string, via JsonStringEnumConverter) and
// DashboardViewModel.GroupOrder assumes every member is listed.
public enum MetricGroup { Cpu, Gpu, Memory, Storage, Network, Game }
```

`src/Stats.Core/ViewModels/DashboardViewModel.cs` lines 12-13 become:
```csharp
    private static readonly MetricGroup[] GroupOrder =
        { MetricGroup.Cpu, MetricGroup.Gpu, MetricGroup.Memory, MetricGroup.Storage, MetricGroup.Network, MetricGroup.Game };
```

`src/Stats.Core/Frames/FrameMetrics.cs`:
```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Frames;

/// <summary>Metric identities for the PresentMon-backed frame-rate reader.</summary>
public static class FrameMetrics
{
    public const string IdPrefix = "fps.";
    public const string FpsId = "fps.avg";
    public const string LowId = "fps.low1";
    public const string FrameTimeId = "fps.frametime";
    public const string HardwareName = "Foreground app";

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        new MetricDefinition(FpsId, "FPS", MetricGroup.Game, HardwareName, "fps", "F0"),
        new MetricDefinition(LowId, "1% Low FPS", MetricGroup.Game, HardwareName, "fps", "F0"),
        new MetricDefinition(FrameTimeId, "Frame Time", MetricGroup.Game, HardwareName, "ms", "F1"),
    };

    public static bool IsFrameMetric(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test --nologo`
Expected: all PASS (the new 4 plus the existing suite — nothing else switches over `MetricGroup` without a default).

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Metrics/MetricGroup.cs src/Stats.Core/ViewModels/DashboardViewModel.cs src/Stats.Core/Frames/FrameMetrics.cs tests/Stats.Core.Tests/FrameMetricsTests.cs
git commit -m "feat(core): MetricGroup.Game and fps.* metric definitions"
```

---

### Task 2: `PresentMonCsvParser`

**Files:**
- Create: `src/Stats.Core/Frames/FrameSample.cs`
- Create: `src/Stats.Core/Frames/PresentMonFormatException.cs`
- Create: `src/Stats.Core/Frames/PresentMonCsvParser.cs`
- Test: `tests/Stats.Core.Tests/PresentMonCsvParserTests.cs`

**Interfaces:**
- Produces: `readonly record struct FrameSample(int Pid, double FrameTimeMs)`; `class PresentMonFormatException : Exception`; `sealed class PresentMonCsvParser { bool HeaderParsed; int SkippedLines; FrameSample? Parse(string line); }`

Background for the implementer: PresentMon writes one CSV header line then one line per presented frame. Column names differ by version — 1.x used `msBetweenPresents`, some 2.x builds `MsBetweenPresents`, current 2.x `FrameTime`. All have `ProcessID`. Newer builds have `CPUStartTime` (seconds). Missing values print as `NA`. Match names case-insensitively.

- [ ] **Step 1: Write the failing tests**

`tests/Stats.Core.Tests/PresentMonCsvParserTests.cs`:
```csharp
using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class PresentMonCsvParserTests
{
    private const string V2Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,AnimationError";
    private const string V1Header =
        "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed";
    private const string StartOnlyHeader = "Application,ProcessID,CPUStartTime,Other";

    private static PresentMonCsvParser Primed(string header)
    {
        var p = new PresentMonCsvParser();
        Assert.Null(p.Parse(header));
        Assert.True(p.HeaderParsed);
        return p;
    }

    [Fact]
    public void V2_FrameTimeColumn_YieldsPidAndFrameTime()
    {
        var p = Primed(V2Header);
        var s = p.Parse("game.exe,1234,0x000001,DXGI,1,0,0,Hardware: Independent Flip,12.345678,16.667,10.1,6.5,NA,NA,NA,NA,NA,NA,NA");
        Assert.Equal(new FrameSample(1234, 16.667), s);
    }

    [Fact]
    public void V1_msBetweenPresentsColumn_YieldsPidAndFrameTime()
    {
        var p = Primed(V1Header);
        var s = p.Parse("game.exe,42,0x0,DXGI,1,0,0,Composed: Flip,0,1.5,0.2,8.333,5.0,9.1");
        Assert.Equal(new FrameSample(42, 8.333), s);
    }

    [Fact]
    public void HeaderMatching_IsCaseInsensitive()
    {
        var p = Primed("application,processid,MSBETWEENPRESENTS");
        Assert.Equal(new FrameSample(7, 4.0), p.Parse("x.exe,7,4.0"));
    }

    [Fact]
    public void CpuStartTimeFallback_DerivesDeltasPerPid_FirstFrameYieldsNothing()
    {
        var p = Primed(StartOnlyHeader);
        Assert.Null(p.Parse("a.exe,1,100.000,x"));      // first frame for pid 1: no interval yet
        Assert.Null(p.Parse("b.exe,2,100.005,x"));      // first frame for pid 2
        var a = p.Parse("a.exe,1,100.016,x");
        var b = p.Parse("b.exe,2,100.010,x");
        Assert.Equal(1, a!.Value.Pid);
        Assert.Equal(16.0, a.Value.FrameTimeMs, 6);     // (100.016 - 100.000) s → ms, within fp noise
        Assert.Equal(2, b!.Value.Pid);
        Assert.Equal(5.0, b.Value.FrameTimeMs, 6);
        Assert.Equal(0, p.SkippedLines);
    }

    [Theory]
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,NA,NA,NA,NA,NA,NA,NA,NA,NA,NA")]   // NA frame time
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,-3.0,NA,NA,NA,NA,NA,NA,NA,NA,NA")] // negative
    [InlineData("game.exe,1234,0x1,DXGI,1,0,0,Mode,1.0,0,NA,NA,NA,NA,NA,NA,NA,NA,NA")]    // zero
    [InlineData("game.exe,abc,0x1,DXGI,1,0,0,Mode,1.0,16.0,NA,NA,NA,NA,NA,NA,NA,NA,NA")]  // bad pid
    [InlineData("too,short")]
    [InlineData("")]
    public void MalformedDataLines_AreSkippedAndCounted(string line)
    {
        var p = Primed(V2Header);
        Assert.Null(p.Parse(line));
        Assert.Equal(1, p.SkippedLines);
    }

    [Fact]
    public void LeadingBlankLines_BeforeHeader_AreIgnored()
    {
        var p = new PresentMonCsvParser();
        Assert.Null(p.Parse(""));
        Assert.Null(p.Parse("   "));
        Assert.False(p.HeaderParsed);
        Assert.Null(p.Parse(V2Header));
        Assert.True(p.HeaderParsed);
    }

    [Fact]
    public void Header_WithoutProcessId_Throws() =>
        Assert.Throws<PresentMonFormatException>(() => new PresentMonCsvParser().Parse("Application,FrameTime"));

    [Fact]
    public void Header_WithoutAnyTimingColumn_Throws() =>
        Assert.Throws<PresentMonFormatException>(() => new PresentMonCsvParser().Parse("Application,ProcessID,Runtime"));

    [Fact]
    public void Values_ParseWithInvariantCulture()
    {
        var p = Primed("ProcessID,FrameTime");
        Assert.Equal(new FrameSample(3, 1234.5), p.Parse("3,1234.5"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~PresentMonCsvParserTests" --nologo`
Expected: build error — types do not exist.

- [ ] **Step 3: Implement**

`src/Stats.Core/Frames/FrameSample.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>One presented frame: which process, and how long since that process's previous present.</summary>
public readonly record struct FrameSample(int Pid, double FrameTimeMs);
```

`src/Stats.Core/Frames/PresentMonFormatException.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>PresentMon's CSV header lacks the columns the parser needs (ProcessID + a timing column).</summary>
public sealed class PresentMonFormatException : Exception
{
    public PresentMonFormatException(string message) : base(message) { }
}
```

`src/Stats.Core/Frames/PresentMonCsvParser.cs`:
```csharp
using System.Globalization;

namespace Stats.Core.Frames;

/// <summary>
/// Header-driven parser for PresentMon console CSV (stdout). Feed it lines in order; the first non-blank
/// line is the header. Tolerates 1.x (`msBetweenPresents`), 2.x (`FrameTime` / `MsBetweenPresents`) naming,
/// and falls back to per-process deltas of `CPUStartTime` (seconds) when no interval column exists.
/// Not thread-safe: call from the stdout reader thread only.
/// </summary>
public sealed class PresentMonCsvParser
{
    private static readonly string[] IntervalColumnNames = { "FrameTime", "MsBetweenPresents" };
    private const string PidColumnName = "ProcessID";
    private const string StartColumnName = "CPUStartTime";

    private int _pidIndex = -1;
    private int _intervalIndex = -1;
    private int _startIndex = -1;
    private int _fieldCount;
    private readonly Dictionary<int, double> _lastStartSeconds = new();

    public bool HeaderParsed { get; private set; }
    /// <summary>Data lines that could not be turned into a sample (wrong width, NA, non-positive, bad pid).</summary>
    public int SkippedLines { get; private set; }

    /// <summary>Returns a sample for a valid data line; null for the header, blank lines, skipped lines, or
    /// a first-seen process in CPUStartTime-fallback mode.</summary>
    /// <exception cref="PresentMonFormatException">Header lacks ProcessID or every timing column.</exception>
    public FrameSample? Parse(string line)
    {
        if (!HeaderParsed)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            ParseHeader(line);
            return null;
        }

        if (string.IsNullOrWhiteSpace(line)) { SkippedLines++; return null; }
        var fields = line.Split(',');
        if (fields.Length != _fieldCount) { SkippedLines++; return null; }

        if (!int.TryParse(fields[_pidIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
        { SkippedLines++; return null; }

        if (_intervalIndex >= 0)
        {
            if (!TryParsePositive(fields[_intervalIndex], out double ms)) { SkippedLines++; return null; }
            return new FrameSample(pid, ms);
        }

        // CPUStartTime fallback: interval = delta of this process's consecutive start times.
        if (!double.TryParse(fields[_startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out double startSec)
            || double.IsNaN(startSec))
        { SkippedLines++; return null; }
        if (_lastStartSeconds.TryGetValue(pid, out double prev))
        {
            _lastStartSeconds[pid] = startSec;
            double ms = (startSec - prev) * 1000.0;
            if (ms <= 0) { SkippedLines++; return null; }
            return new FrameSample(pid, ms);
        }
        _lastStartSeconds[pid] = startSec;
        return null;
    }

    private void ParseHeader(string line)
    {
        var names = line.Split(',');
        _fieldCount = names.Length;
        _pidIndex = IndexOf(names, PidColumnName);
        if (_pidIndex < 0)
            throw new PresentMonFormatException($"PresentMon CSV header has no '{PidColumnName}' column: {line}");
        foreach (var candidate in IntervalColumnNames)
        {
            _intervalIndex = IndexOf(names, candidate);
            if (_intervalIndex >= 0) break;
        }
        if (_intervalIndex < 0)
        {
            _startIndex = IndexOf(names, StartColumnName);
            if (_startIndex < 0)
                throw new PresentMonFormatException(
                    $"PresentMon CSV header has none of FrameTime/MsBetweenPresents/CPUStartTime: {line}");
        }
        HeaderParsed = true;
    }

    private static int IndexOf(string[] names, string wanted)
    {
        for (int i = 0; i < names.Length; i++)
            if (string.Equals(names[i].Trim(), wanted, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~PresentMonCsvParserTests" --nologo`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Frames/FrameSample.cs src/Stats.Core/Frames/PresentMonFormatException.cs src/Stats.Core/Frames/PresentMonCsvParser.cs tests/Stats.Core.Tests/PresentMonCsvParserTests.cs
git commit -m "feat(core): header-driven PresentMon CSV parser"
```

---

### Task 3: `FrameStatsAggregator`

**Files:**
- Create: `src/Stats.Core/Frames/FrameStats.cs`
- Create: `src/Stats.Core/Frames/FrameStatsAggregator.cs`
- Test: `tests/Stats.Core.Tests/FrameStatsAggregatorTests.cs`

**Interfaces:**
- Consumes: `FrameSample` (Task 2).
- Produces: `readonly record struct FrameStats(float? Fps, float? FrameTimeMs, float? OnePercentLowFps)` with `static FrameStats Empty`; `sealed class FrameStatsAggregator { FrameStatsAggregator(int capacityPerPid = 1000, TimeSpan? staleAfter = null); const int MinFramesInWindow = 10; const int MinFramesForLow = 100; void Add(FrameSample sample, DateTime nowUtc); FrameStats Snapshot(int pid, DateTime nowUtc, TimeSpan window); int TrackedProcessCount; void Clear(); }`

- [ ] **Step 1: Write the failing tests**

`tests/Stats.Core.Tests/FrameStatsAggregatorTests.cs`:
```csharp
using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class FrameStatsAggregatorTests
{
    private static readonly DateTime T0 = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan OneSec = TimeSpan.FromSeconds(1);

    /// <summary>Adds <paramref name="count"/> frames for pid evenly spread over the last second ending at <paramref name="end"/>.</summary>
    private static void AddBurst(FrameStatsAggregator a, int pid, int count, double frameTimeMs, DateTime end)
    {
        for (int i = count - 1; i >= 0; i--)
            a.Add(new FrameSample(pid, frameTimeMs), end - TimeSpan.FromMilliseconds(i * 1000.0 / count));
    }

    [Fact]
    public void Snapshot_UnknownPid_IsEmpty()
    {
        var a = new FrameStatsAggregator();
        Assert.Equal(FrameStats.Empty, a.Snapshot(99, T0, OneSec));
    }

    [Fact]
    public void Fps_IsFramesInWindowDividedBySeconds()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16.6, T0);
        var s = a.Snapshot(1, T0, OneSec);
        Assert.Equal(60f, s.Fps);
        Assert.Equal(16.6f, s.FrameTimeMs!.Value, 2);
    }

    [Fact]
    public void Fps_UsesWindowLength_NotFrameTimes()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 120, 8.3, T0);                     // 120 frames in the last second
        Assert.Equal(60f, a.Snapshot(1, T0, TimeSpan.FromSeconds(2)).Fps); // 120 / 2 s
    }

    [Fact]
    public void Fps_OnlyCountsFramesInsideWindow()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 50, 20, T0 - TimeSpan.FromSeconds(5)); // old
        AddBurst(a, 1, 30, 33.3, T0);                          // recent
        Assert.Equal(30f, a.Snapshot(1, T0, OneSec).Fps);
    }

    [Fact]
    public void FewerThanTenFramesInWindow_FpsAndFrameTimeNull()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 9, 100, T0);
        var s = a.Snapshot(1, T0, OneSec);
        Assert.Null(s.Fps);
        Assert.Null(s.FrameTimeMs);
        AddBurst(a, 2, 10, 100, T0);
        Assert.Equal(10f, a.Snapshot(2, T0, OneSec).Fps);
    }

    [Fact]
    public void OnePercentLow_NullUntilHundredFrames_ThenP99OfWholeBuffer()
    {
        var a = new FrameStatsAggregator();
        // 98 fast frames, then 2 slow ones → 100 frames; p99 index = ceil(0.99*100)-1 = 98 → the 40 ms sample.
        AddBurst(a, 1, 98, 10, T0 - TimeSpan.FromSeconds(1));
        Assert.Null(a.Snapshot(1, T0, OneSec).OnePercentLowFps); // 98 < 100
        a.Add(new FrameSample(1, 50), T0);
        a.Add(new FrameSample(1, 40), T0);
        // sorted: 98×10, 40, 50 → index ceil(99)-1 = 98 → 40 ms → 25 fps
        Assert.Equal(25f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void OnePercentLow_AtThousandFrames_UsesIndex989()
    {
        var a = new FrameStatsAggregator();
        for (int i = 0; i < 1000; i++)
            a.Add(new FrameSample(1, i < 990 ? 10 : 20), T0);   // 990×10ms, 10×20ms
        // ceil(0.99*1000)-1 = 989 → 10 ms → 100 fps (the slow 1% starts at index 990)
        Assert.Equal(100f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void RingBuffer_CapsAtCapacity_DroppingOldest()
    {
        var a = new FrameStatsAggregator(capacityPerPid: 100);
        for (int i = 0; i < 100; i++) a.Add(new FrameSample(1, 100), T0 - TimeSpan.FromSeconds(2)); // slow, old
        for (int i = 0; i < 100; i++) a.Add(new FrameSample(1, 10), T0);                             // fast, recent, evicts all slow
        Assert.Equal(100f, a.Snapshot(1, T0, OneSec).OnePercentLowFps);
    }

    [Fact]
    public void StalePid_PrunedAfterTenSeconds()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        AddBurst(a, 2, 60, 16, T0 + TimeSpan.FromSeconds(11));
        Assert.Equal(2, a.TrackedProcessCount);
        a.Snapshot(2, T0 + TimeSpan.FromSeconds(11), OneSec);   // prune runs on Snapshot
        Assert.Equal(1, a.TrackedProcessCount);
        Assert.Equal(FrameStats.Empty, a.Snapshot(1, T0 + TimeSpan.FromSeconds(11), OneSec));
    }

    [Fact]
    public void Pids_AreIsolated()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        AddBurst(a, 2, 30, 33, T0);
        Assert.Equal(60f, a.Snapshot(1, T0, OneSec).Fps);
        Assert.Equal(30f, a.Snapshot(2, T0, OneSec).Fps);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var a = new FrameStatsAggregator();
        AddBurst(a, 1, 60, 16, T0);
        a.Clear();
        Assert.Equal(0, a.TrackedProcessCount);
        Assert.Equal(FrameStats.Empty, a.Snapshot(1, T0, OneSec));
    }

    [Fact]
    public async Task ConcurrentAddAndSnapshot_DoesNotThrow()
    {
        var a = new FrameStatsAggregator();
        using var cts = new CancellationTokenSource(300);
        var writer = Task.Run(() => { int i = 0; while (!cts.IsCancellationRequested) a.Add(new FrameSample(1 + (i++ % 3), 16), DateTime.UtcNow); });
        var reader = Task.Run(() => { while (!cts.IsCancellationRequested) { a.Snapshot(1, DateTime.UtcNow, OneSec); a.Snapshot(2, DateTime.UtcNow, OneSec); } });
        await Task.WhenAll(writer, reader);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FrameStatsAggregatorTests" --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

`src/Stats.Core/Frames/FrameStats.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>Aggregate frame statistics for one process over the last poll window. Null = not enough frames.</summary>
public readonly record struct FrameStats(float? Fps, float? FrameTimeMs, float? OnePercentLowFps)
{
    public static readonly FrameStats Empty = new(null, null, null);
}
```

`src/Stats.Core/Frames/FrameStatsAggregator.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>
/// Thread-safe per-process store of recent frames. Producer thread calls Add; poller thread calls Snapshot.
/// FPS/frame time are computed over the caller's window; 1% low over the whole ring buffer.
/// </summary>
public sealed class FrameStatsAggregator
{
    public const int MinFramesInWindow = 10;
    public const int MinFramesForLow = 100;

    private readonly int _capacity;
    private readonly TimeSpan _staleAfter;
    private readonly object _gate = new();
    private readonly Dictionary<int, Queue<(DateTime At, double Ms)>> _frames = new();
    private readonly Dictionary<int, DateTime> _lastSeen = new();

    public FrameStatsAggregator(int capacityPerPid = 1000, TimeSpan? staleAfter = null)
    {
        _capacity = capacityPerPid;
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(10);
    }

    public int TrackedProcessCount { get { lock (_gate) return _frames.Count; } }

    public void Add(FrameSample sample, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_frames.TryGetValue(sample.Pid, out var q))
            {
                q = new Queue<(DateTime, double)>(_capacity);
                _frames[sample.Pid] = q;
            }
            q.Enqueue((nowUtc, sample.FrameTimeMs));
            while (q.Count > _capacity) q.Dequeue();
            _lastSeen[sample.Pid] = nowUtc;
        }
    }

    public FrameStats Snapshot(int pid, DateTime nowUtc, TimeSpan window)
    {
        lock (_gate)
        {
            Prune(nowUtc);
            if (!_frames.TryGetValue(pid, out var q) || q.Count == 0) return FrameStats.Empty;

            var cutoff = nowUtc - window;
            int inWindow = 0;
            double sumMs = 0;
            foreach (var (at, ms) in q)
            {
                if (at > cutoff && at <= nowUtc) { inWindow++; sumMs += ms; }
            }

            float? fps = null, frameTime = null;
            if (inWindow >= MinFramesInWindow && window.TotalSeconds > 0)
            {
                fps = (float)(inWindow / window.TotalSeconds);
                frameTime = (float)(sumMs / inWindow);
            }

            float? low = null;
            if (q.Count >= MinFramesForLow)
            {
                var sorted = new double[q.Count];
                int i = 0;
                foreach (var (_, ms) in q) sorted[i++] = ms;
                Array.Sort(sorted);
                int idx = (int)Math.Ceiling(0.99 * sorted.Length) - 1;
                double p99 = sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
                if (p99 > 0) low = (float)(1000.0 / p99);
            }
            return new FrameStats(fps, frameTime, low);
        }
    }

    public void Clear()
    {
        lock (_gate) { _frames.Clear(); _lastSeen.Clear(); }
    }

    private void Prune(DateTime nowUtc)
    {
        List<int>? stale = null;
        foreach (var (pid, seen) in _lastSeen)
            if (nowUtc - seen > _staleAfter) (stale ??= new()).Add(pid);
        if (stale is null) return;
        foreach (var pid in stale) { _frames.Remove(pid); _lastSeen.Remove(pid); }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FrameStatsAggregatorTests" --nologo`
Expected: all PASS. (If `OnePercentLow_NullUntilHundredFrames…` fails on the exact 25 f value, check the arithmetic comment in the test — 98 tens, then 40 and 50 appended; the p99 index is 98 which is the 40 ms sample.)

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Frames/FrameStats.cs src/Stats.Core/Frames/FrameStatsAggregator.cs tests/Stats.Core.Tests/FrameStatsAggregatorTests.cs
git commit -m "feat(core): per-process frame statistics aggregator (fps, frame time, 1% low)"
```

---

### Task 4: `CompositeSensorReader`

**Files:**
- Create: `src/Stats.Core/Sensors/CompositeSensorReader.cs`
- Test: `tests/Stats.Core.Tests/CompositeSensorReaderTests.cs`

**Interfaces:**
- Consumes: `ISensorReader` (existing).
- Produces: `sealed class CompositeSensorReader : ISensorReader { CompositeSensorReader(ISensorReader primary, params ISensorReader[] others); }` — `Name`/`IsDegraded` from primary; `Discover()` concatenates and caches; `Read()` merges, swallowing per-reader exceptions; `Dispose()` disposes all.

- [ ] **Step 1: Write the failing tests**

`tests/Stats.Core.Tests/CompositeSensorReaderTests.cs`:
```csharp
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class CompositeSensorReaderTests
{
    private sealed class Fake : ISensorReader
    {
        public Fake(string name, bool degraded, params (string Id, float? Value)[] values)
        { Name = name; IsDegraded = degraded; _values = values; }
        private readonly (string Id, float? Value)[] _values;
        public bool ThrowOnRead;
        public int DiscoverCalls, Disposed;
        public string Name { get; }
        public bool IsDegraded { get; }
        public IReadOnlyList<MetricDefinition> Discover()
        {
            DiscoverCalls++;
            return _values.Select(v => new MetricDefinition(v.Id, v.Id, MetricGroup.Cpu, Name, "%")).ToList();
        }
        public SensorSnapshot Read()
        {
            if (ThrowOnRead) throw new InvalidOperationException("boom");
            return new SensorSnapshot(_values.ToDictionary(v => v.Id, v => v.Value), DateTime.UtcNow);
        }
        public void Dispose() => Disposed++;
    }

    [Fact]
    public void NameAndDegraded_ComeFromPrimary()
    {
        var c = new CompositeSensorReader(new Fake("LHM", true), new Fake("PM", false));
        Assert.Equal("LHM", c.Name);
        Assert.True(c.IsDegraded);
    }

    [Fact]
    public void Discover_ConcatenatesInOrder_AndCallsEachReaderOnce()
    {
        var a = new Fake("A", false, ("a1", 1), ("a2", 2));
        var b = new Fake("B", false, ("b1", 3));
        var c = new CompositeSensorReader(a, b);
        Assert.Equal(new[] { "a1", "a2", "b1" }, c.Discover().Select(d => d.Id));
        Assert.Equal(new[] { "a1", "a2", "b1" }, c.Discover().Select(d => d.Id)); // cached
        Assert.Equal(1, a.DiscoverCalls);
        Assert.Equal(1, b.DiscoverCalls);
    }

    [Fact]
    public void Read_MergesAllValues()
    {
        var c = new CompositeSensorReader(new Fake("A", false, ("a1", 1)), new Fake("B", false, ("b1", null), ("b2", 5)));
        var s = c.Read();
        Assert.Equal(1f, s.Values["a1"]);
        Assert.Null(s.Values["b1"]);
        Assert.Equal(5f, s.Values["b2"]);
    }

    [Fact]
    public void Read_OneReaderThrowing_OthersStillReported()
    {
        var a = new Fake("A", false, ("a1", 1)) { ThrowOnRead = true };
        var b = new Fake("B", false, ("b1", 2));
        var s = new CompositeSensorReader(a, b).Read();
        Assert.False(s.Values.ContainsKey("a1"));
        Assert.Equal(2f, s.Values["b1"]);
    }

    [Fact]
    public void Dispose_DisposesEveryReader()
    {
        var a = new Fake("A", false); var b = new Fake("B", false);
        new CompositeSensorReader(a, b).Dispose();
        Assert.Equal(1, a.Disposed);
        Assert.Equal(1, b.Disposed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~CompositeSensorReaderTests" --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

`src/Stats.Core/Sensors/CompositeSensorReader.cs`:
```csharp
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Presents several readers as one. Identity (Name/IsDegraded) is the primary's; values are merged.</summary>
public sealed class CompositeSensorReader : ISensorReader
{
    private readonly ISensorReader[] _readers;
    private IReadOnlyList<MetricDefinition>? _definitions;

    public CompositeSensorReader(ISensorReader primary, params ISensorReader[] others)
    {
        _readers = new[] { primary }.Concat(others).ToArray();
    }

    public string Name => _readers[0].Name;
    public bool IsDegraded => _readers[0].IsDegraded;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        if (_definitions is not null) return _definitions;
        var all = new List<MetricDefinition>();
        foreach (var r in _readers) all.AddRange(r.Discover());
        _definitions = all;
        return _definitions;
    }

    public SensorSnapshot Read()
    {
        var values = new Dictionary<string, float?>();
        foreach (var r in _readers)
        {
            SensorSnapshot snap;
            try { snap = r.Read(); }
            catch { continue; } // that reader's ids are absent this tick; others still report
            foreach (var (id, v) in snap.Values) values[id] = v;
        }
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    public void Dispose()
    {
        foreach (var r in _readers)
        {
            try { r.Dispose(); } catch { /* best effort */ }
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~CompositeSensorReaderTests" --nologo`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Sensors/CompositeSensorReader.cs tests/Stats.Core.Tests/CompositeSensorReaderTests.cs
git commit -m "feat(core): CompositeSensorReader merges multiple sensor readers"
```

---

### Task 5: `IFrameSource`, `PresentMonProcess`, `PresentMonLocator`, `ForegroundProcess`

Thin OS shells — no unit tests (spec). Build must pass. Workers must not execute PresentMon.

**Files:**
- Create: `src/Stats.Core/Frames/IFrameSource.cs`
- Create: `src/Stats.Core/Frames/PresentMonProcess.cs`
- Create: `src/Stats.Core/Frames/PresentMonLocator.cs`
- Create: `src/Stats.Core/Frames/ForegroundProcess.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IFrameSource : IDisposable {
      event Action<string>? LineReceived;             // one stdout line (raw CSV), producer thread
      event Action<int, string>? Exited;              // (exitCode, stderrTail) when the child ends on its own
      bool IsRunning { get; }
      void Start();                                   // throws if the exe cannot be started
      void Stop();                                    // kills tree, waits ≤2 s, no Exited event
  }
  public sealed class PresentMonProcess : IFrameSource { PresentMonProcess(string exePath, string excludeExeName = "Stats.App.exe"); }
  public static class PresentMonLocator { static string? Find(string? baseDirectory = null); }
  public static class ForegroundProcess { static int? CurrentPid(); }   // null for own process / no window
  ```

- [ ] **Step 1: Create the interface**

`src/Stats.Core/Frames/IFrameSource.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>A running producer of PresentMon CSV lines (normally the PresentMon child process).</summary>
public interface IFrameSource : IDisposable
{
    /// <summary>Raw stdout line. Raised on the source's reader thread.</summary>
    event Action<string>? LineReceived;
    /// <summary>The source ended on its own: (exit code, last lines of stderr). Not raised by Stop().</summary>
    event Action<int, string>? Exited;
    bool IsRunning { get; }
    /// <exception cref="System.ComponentModel.Win32Exception">The executable could not be started.</exception>
    void Start();
    void Stop();
}
```

- [ ] **Step 2: Create the process wrapper**

`src/Stats.Core/Frames/PresentMonProcess.cs`:
```csharp
using System.Diagnostics;

namespace Stats.Core.Frames;

/// <summary>
/// Owns one PresentMon console process capturing all presenters to stdout. Filtering to the process of
/// interest happens downstream so alt-tabbing never restarts the ETW session.
/// </summary>
public sealed class PresentMonProcess : IFrameSource
{
    private const int StderrTailLines = 20;
    private readonly string _exePath;
    private readonly string _excludeExeName;
    private readonly object _gate = new();
    private Process? _process;
    private bool _stopping;
    private readonly Queue<string> _stderr = new();

    public PresentMonProcess(string exePath, string excludeExeName = "Stats.App.exe")
    {
        _exePath = exePath;
        _excludeExeName = excludeExeName;
    }

    public event Action<string>? LineReceived;
    public event Action<int, string>? Exited;

    public bool IsRunning { get { lock (_gate) return _process is { HasExited: false }; } }

    public static string BuildArguments(string excludeExeName) =>
        $"--output_stdout --no_console_stats --stop_existing_session --session_name StatsFps " +
        $"--no_track_gpu --no_track_input --exclude \"{excludeExeName}\"";

    public void Start()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;
            _stopping = false;
            _stderr.Clear();
            var psi = new ProcessStartInfo(_exePath, BuildArguments(_excludeExeName))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? Environment.CurrentDirectory,
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) LineReceived?.Invoke(e.Data); };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_stderr) { _stderr.Enqueue(e.Data); while (_stderr.Count > StderrTailLines) _stderr.Dequeue(); }
            };
            p.Exited += OnExited;
            p.Start(); // throws Win32Exception if the exe cannot start
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _process = p;
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        int code;
        bool announce;
        lock (_gate)
        {
            if (sender is not Process p || !ReferenceEquals(p, _process)) return;
            try { p.WaitForExit(); } catch { /* flushes the async stdout/stderr readers so the stderr tail is complete */ }
            code = SafeExitCode(p);
            announce = !_stopping;
            _process = null;
            p.Dispose();
        }
        if (!announce) return;
        string tail;
        lock (_stderr) tail = string.Join(Environment.NewLine, _stderr);
        Exited?.Invoke(code, tail);
    }

    public void Stop()
    {
        Process? p;
        lock (_gate)
        {
            p = _process;
            _stopping = true;
            _process = null;
        }
        if (p is null) return;
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            p.WaitForExit(2000);
        }
        catch { /* already gone */ }
        finally { p.Dispose(); }
    }

    public void Dispose() => Stop();

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
```

- [ ] **Step 3: Create the locator and foreground helper**

`src/Stats.Core/Frames/PresentMonLocator.cs`:
```csharp
namespace Stats.Core.Frames;

/// <summary>Finds the PresentMon executable: next to the app (installed), else installer/vendor (run from source).</summary>
public static class PresentMonLocator
{
    public const string ShippedFileName = "PresentMon.exe";

    public static string? Find(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var shipped = Path.Combine(dir, ShippedFileName);
        if (File.Exists(shipped)) return shipped;

        // Walk up from bin/<cfg>/<tfm>/ to the repo root looking for installer/vendor/PresentMon-*.exe.
        var probe = new DirectoryInfo(dir);
        for (int i = 0; i < 8 && probe is not null; i++, probe = probe.Parent)
        {
            var vendor = Path.Combine(probe.FullName, "installer", "vendor");
            if (!Directory.Exists(vendor)) continue;
            var hit = Directory.EnumerateFiles(vendor, "PresentMon-*.exe").OrderByDescending(f => f).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }
}
```

`src/Stats.Core/Frames/ForegroundProcess.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Stats.Core.Frames;

/// <summary>PID of the process owning the foreground window; null when there is none or it is this process.</summary>
public static class ForegroundProcess
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static int? CurrentPid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        if (pid == (uint)Environment.ProcessId) return null;
        return (int)pid;
    }
}
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build --nologo && dotnet test --nologo`
Expected: build succeeds with no new warnings; all tests PASS (none added here).

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Frames/IFrameSource.cs src/Stats.Core/Frames/PresentMonProcess.cs src/Stats.Core/Frames/PresentMonLocator.cs src/Stats.Core/Frames/ForegroundProcess.cs
git commit -m "feat(core): PresentMon child-process wrapper, locator, foreground-window PID helper"
```

---

### Task 6: `FrameRateReader`

**Files:**
- Create: `src/Stats.Core/Frames/FrameRateReader.cs`
- Test: `tests/Stats.Core.Tests/FrameRateReaderTests.cs`

**Interfaces:**
- Consumes: `FrameMetrics` (T1), `PresentMonCsvParser`/`FrameSample`/`PresentMonFormatException` (T2), `FrameStatsAggregator`/`FrameStats` (T3), `IFrameSource`/`PresentMonProcess`/`PresentMonLocator`/`ForegroundProcess` (T5), `ISensorReader`.
- Produces:
  ```csharp
  public sealed class FrameRateReader : ISensorReader {
      FrameRateReader(string? exePath, Func<IFrameSource> sourceFactory, Func<int?> foregroundPid,
                      Func<DateTime>? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null);
      static FrameRateReader CreateDefault();                      // locator + PresentMonProcess + ForegroundProcess
      static bool ShouldBeActive(IEnumerable<string> dashboardIds, IEnumerable<string> overlayIds);
      TimeSpan Window { get; set; }                                 // = poll interval; default 1 s
      bool IsActive { get; }  bool IsAvailable { get; }  string? StatusMessage { get; }
      void SetActive(bool active);                                  // idempotent, UI thread
      // ISensorReader: Name="PresentMon", IsDegraded=false, Discover(), Read(), Dispose()
  }
  ```

- [ ] **Step 1: Write the failing tests**

`tests/Stats.Core.Tests/FrameRateReaderTests.cs`:
```csharp
using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class FrameRateReaderTests
{
    private const string Header = "Application,ProcessID,FrameTime";

    private sealed class FakeSource : IFrameSource
    {
        public int Starts, Stops;
        public bool ThrowOnStart;
        public event Action<string>? LineReceived;
        public event Action<int, string>? Exited;
        public bool IsRunning { get; private set; }
        public void Start() { if (ThrowOnStart) throw new System.ComponentModel.Win32Exception(2, "not found"); Starts++; IsRunning = true; }
        public void Stop() { Stops++; IsRunning = false; }
        public void Dispose() => Stop();
        public void Emit(string line) => LineReceived?.Invoke(line);
        public void Die(int code, string stderr) { IsRunning = false; Exited?.Invoke(code, stderr); }
    }

    private sealed class Harness
    {
        public readonly FakeSource Source = new();
        public int? ForegroundPid = 1234;
        public DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        public readonly List<TimeSpan> Delays = new();
        public readonly List<TaskCompletionSource> Pending = new();
        public FrameRateReader Reader;
        public Harness(string? exePath = @"C:\fake\PresentMon.exe")
        {
            Reader = new FrameRateReader(exePath, () => Source, () => ForegroundPid, () => Now,
                (d, _) => { Delays.Add(d); var tcs = new TaskCompletionSource(); Pending.Add(tcs); return tcs.Task; });
        }
        public void EmitFrames(int pid, int count, double ms)
        {
            for (int i = 0; i < count; i++) Source.Emit($"game.exe,{pid},{ms}");
        }
        /// <summary>Let the most recent pending backoff delay elapse.</summary>
        public void ElapseBackoff() { var t = Pending[^1]; Pending.RemoveAt(Pending.Count - 1); t.SetResult(); }
    }

    [Fact]
    public void Discover_EmptyWhenNoExe()
    {
        var h = new Harness(exePath: null);
        Assert.Empty(h.Reader.Discover());
        Assert.False(h.Reader.IsAvailable);
    }

    [Fact]
    public void Discover_ThreeDefinitions_DoesNotStartProcess()
    {
        var h = new Harness();
        Assert.Equal(3, h.Reader.Discover().Count);
        Assert.Equal(0, h.Source.Starts);
    }

    [Fact]
    public void ShouldBeActive_AnyFpsIdInEitherList()
    {
        Assert.False(FrameRateReader.ShouldBeActive(new[] { "cpu.temp" }, Array.Empty<string>()));
        Assert.True(FrameRateReader.ShouldBeActive(new[] { "fps.avg" }, Array.Empty<string>()));
        Assert.True(FrameRateReader.ShouldBeActive(Array.Empty<string>(), new[] { "gpu.temp", "fps.frametime" }));
    }

    [Fact]
    public void SetActive_Idempotent_StartsOnceStopsOnce()
    {
        var h = new Harness();
        h.Reader.SetActive(true); h.Reader.SetActive(true);
        Assert.Equal(1, h.Source.Starts);
        Assert.True(h.Reader.IsActive);
        h.Reader.SetActive(false); h.Reader.SetActive(false);
        Assert.Equal(1, h.Source.Stops);
        Assert.False(h.Reader.IsActive);
    }

    [Fact]
    public void Read_Inactive_AllNull()
    {
        var h = new Harness();
        var s = h.Reader.Read();
        Assert.Equal(3, s.Values.Count);
        Assert.Contains("fps.avg", s.Values.Keys);
        Assert.Contains("fps.low1", s.Values.Keys);
        Assert.Contains("fps.frametime", s.Values.Keys);
        Assert.All(s.Values.Values, v => Assert.Null(v));
    }

    [Fact]
    public void Read_Active_ForegroundPidFrames_ProduceValues()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        var s = h.Reader.Read();
        Assert.Equal(60f, s.Values["fps.avg"]);
        Assert.Equal(16.6f, s.Values["fps.frametime"]!.Value, 2);
        Assert.Null(s.Values["fps.low1"]); // < 100 frames
    }

    [Fact]
    public void Read_ForegroundIsSomeoneElse_Null()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        h.ForegroundPid = 999;
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
        h.ForegroundPid = null;
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void Window_ControlsFpsDenominator()
    {
        var h = new Harness();
        h.Reader.Window = TimeSpan.FromSeconds(2);
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        Assert.Equal(30f, h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void SetActiveFalse_ClearsAggregator()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        h.Reader.SetActive(false);
        h.Reader.SetActive(true);
        Assert.Null(h.Reader.Read().Values["fps.avg"]); // old frames gone, header must arrive again
    }

    [Fact]
    public void Crash_RestartsWithBackoff_1_5_30_ThenGivesUp()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "boom");
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, h.Delays);
        h.ElapseBackoff();
        Assert.Equal(2, h.Source.Starts);
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(5), h.Delays[^1]);
        h.ElapseBackoff();
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(30), h.Delays[^1]);
        h.ElapseBackoff();
        Assert.Equal(4, h.Source.Starts);
        h.Source.Die(1, "boom");
        Assert.Equal(3, h.Delays.Count);           // no fourth delay
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("gave up", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crash_AfterSuccessfulFrames_ResetsBackoff()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "boom");
        h.ElapseBackoff();                          // 1 s
        h.Source.Emit(Header);
        h.EmitFrames(1234, 20, 16.6);               // healthy again
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(1), h.Delays[^1]);
    }

    [Fact]
    public void AccessDenied_ExitCode6_NoRestart_Unavailable()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(6, "error: failed to start trace session: access denied.");
        Assert.Empty(h.Delays);
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("access denied", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void AccessDenied_StderrText_NoRestart()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "something something Access Denied");
        Assert.Empty(h.Delays);
        Assert.False(h.Reader.IsAvailable);
    }

    [Fact]
    public void Reactivate_AfterGivingUp_TriesAgain()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(6, "access denied");
        h.Reader.SetActive(false);
        h.Reader.SetActive(true);
        Assert.Equal(2, h.Source.Starts);
        Assert.True(h.Reader.IsAvailable);
    }

    [Fact]
    public void BadHeader_StopsSource_Unavailable()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit("Application,Nothing,Useful");
        Assert.Equal(1, h.Source.Stops);
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("header", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartThrows_Unavailable_NoCrash()
    {
        var h = new Harness();
        h.Source.ThrowOnStart = true;
        h.Reader.SetActive(true);
        Assert.False(h.Reader.IsAvailable);
        Assert.NotNull(h.Reader.StatusMessage);
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void Dispose_StopsSource()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Reader.Dispose();
        Assert.True(h.Source.Stops >= 1);
    }

    [Fact]
    public void LateExitedEvent_AfterDeactivate_IsIgnored()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Reader.SetActive(false);
        h.Source.Die(1, "late");
        Assert.Empty(h.Delays);
        Assert.Equal(1, h.Source.Starts);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FrameRateReaderTests" --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

`src/Stats.Core/Frames/FrameRateReader.cs`:
```csharp
using System.Diagnostics;
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Frames;

/// <summary>
/// ISensorReader for the foreground process's frame rate, fed by a PresentMon child process that runs
/// only while <see cref="SetActive"/> is true. Read() is called by SensorPoller on its thread; SetActive
/// from the UI thread; LineReceived/Exited from the source's threads — all state is guarded by _gate.
/// </summary>
public sealed class FrameRateReader : ISensorReader
{
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) };

    private readonly string? _exePath;
    private readonly Func<IFrameSource> _sourceFactory;
    private readonly Func<int?> _foregroundPid;
    private readonly Func<DateTime> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _gate = new();

    private IFrameSource? _source;
    private PresentMonCsvParser _parser = new();
    private readonly FrameStatsAggregator _aggregator = new();
    private CancellationTokenSource? _restartCts;
    private int _failures;           // consecutive exits without frames since last start
    private bool _sawFrames;         // frames received since last (re)start → resets backoff
    private int _generation;         // bumped on every SetActive so stale callbacks are ignored

    public FrameRateReader(string? exePath, Func<IFrameSource> sourceFactory, Func<int?> foregroundPid,
        Func<DateTime>? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _exePath = exePath;
        _sourceFactory = sourceFactory;
        _foregroundPid = foregroundPid;
        _clock = clock ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
        IsAvailable = exePath is not null;
        if (exePath is null) StatusMessage = "PresentMon.exe not found; FPS metrics unavailable.";
    }

    /// <summary>Production wiring: bundled exe, real child process, real foreground window.</summary>
    public static FrameRateReader CreateDefault()
    {
        var exe = PresentMonLocator.Find();
        return new FrameRateReader(exe, () => new PresentMonProcess(exe!), ForegroundProcess.CurrentPid);
    }

    /// <summary>True when any selected metric (dashboard ∪ overlay) is a frame metric.</summary>
    public static bool ShouldBeActive(IEnumerable<string> dashboardIds, IEnumerable<string> overlayIds) =>
        dashboardIds.Any(FrameMetrics.IsFrameMetric) || overlayIds.Any(FrameMetrics.IsFrameMetric);

    public string Name => "PresentMon";
    public bool IsDegraded => false;
    /// <summary>Poll interval; FPS = frames in the last Window ÷ Window seconds.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);
    public bool IsActive { get; private set; }
    /// <summary>False when the exe is missing, tracing was denied, the CSV was unreadable, or restarts were exhausted.</summary>
    public bool IsAvailable { get; private set; }
    public string? StatusMessage { get; private set; }

    public IReadOnlyList<MetricDefinition> Discover() =>
        _exePath is null ? Array.Empty<MetricDefinition>() : FrameMetrics.Definitions;

    public SensorSnapshot Read()
    {
        FrameStats stats = FrameStats.Empty;
        lock (_gate)
        {
            if (IsActive && IsAvailable && _foregroundPid() is int pid)
                stats = _aggregator.Snapshot(pid, _clock(), Window);
        }
        return new SensorSnapshot(new Dictionary<string, float?>
        {
            [FrameMetrics.FpsId] = stats.Fps,
            [FrameMetrics.LowId] = stats.OnePercentLowFps,
            [FrameMetrics.FrameTimeId] = stats.FrameTimeMs,
        }, _clock());
    }

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (active == IsActive) return;
            IsActive = active;
            _generation++;
            CancelPendingRestart();
            if (active)
            {
                if (_exePath is null) return;
                IsAvailable = true;
                StatusMessage = null;
                _failures = 0;
                StartSourceLocked();
            }
            else
            {
                StopSourceLocked();
                _aggregator.Clear();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            IsActive = false;
            _generation++;
            CancelPendingRestart();
            StopSourceLocked();
        }
    }

    // ---- internals (all called with _gate held unless noted) ----

    private void StartSourceLocked()
    {
        _parser = new PresentMonCsvParser();
        _sawFrames = false;
        var src = _sourceFactory();
        int gen = _generation;
        src.LineReceived += line => OnLine(src, gen, line);
        src.Exited += (code, stderr) => OnExited(src, gen, code, stderr);
        _source = src;
        try
        {
            src.Start();
        }
        catch (Exception ex)
        {
            _source = null;
            MarkUnavailable($"PresentMon could not be started: {ex.Message}");
        }
    }

    private void StopSourceLocked()
    {
        var src = _source;
        _source = null;
        if (src is null) return;
        try { src.Stop(); } catch { /* best effort */ }
        try { src.Dispose(); } catch { /* best effort */ }
    }

    private void CancelPendingRestart()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;
    }

    private void MarkUnavailable(string message)
    {
        IsAvailable = false;
        StatusMessage = message;
        Trace.WriteLine("[Stats.FrameRateReader] " + message);
    }

    private void OnLine(IFrameSource src, int gen, string line)
    {
        FrameSample? sample;
        lock (_gate)
        {
            if (gen != _generation || !ReferenceEquals(src, _source)) return;
            try
            {
                sample = _parser.Parse(line);
            }
            catch (PresentMonFormatException ex)
            {
                MarkUnavailable("PresentMon CSV header not understood: " + ex.Message);
                StopSourceLocked();
                return;
            }
            if (sample is FrameSample s)
            {
                _sawFrames = true;
                _aggregator.Add(s, _clock());
            }
        }
    }

    private void OnExited(IFrameSource src, int gen, int exitCode, string stderrTail)
    {
        lock (_gate)
        {
            if (gen != _generation || !ReferenceEquals(src, _source)) return;
            _source = null;
            try { src.Dispose(); } catch { }

            bool denied = exitCode == 6 || stderrTail.Contains("access denied", StringComparison.OrdinalIgnoreCase);
            if (denied)
            {
                MarkUnavailable("PresentMon: access denied starting the ETW trace session (exit " + exitCode + "). " +
                                "Launch Stats from the Start menu or a non-Store terminal; processes with MSIX package identity cannot trace. " +
                                stderrTail);
                return;
            }

            if (_sawFrames) _failures = 0;
            if (_failures >= Backoff.Length)
            {
                MarkUnavailable($"PresentMon exited repeatedly (last exit {exitCode}); gave up until FPS metrics are re-selected. {stderrTail}");
                return;
            }
            var wait = Backoff[_failures++];
            Trace.WriteLine($"[Stats.FrameRateReader] PresentMon exited ({exitCode}); restarting in {wait.TotalSeconds:F0}s. {stderrTail}");
            ScheduleRestartLocked(wait, gen);
        }
    }

    private void ScheduleRestartLocked(TimeSpan wait, int gen)
    {
        CancelPendingRestart();
        var cts = new CancellationTokenSource();
        _restartCts = cts;
        var token = cts.Token;
        _ = _delay(wait, token).ContinueWith(t =>
        {
            if (t.IsCanceled || token.IsCancellationRequested) return;
            lock (_gate)
            {
                if (gen != _generation || !IsActive || !IsAvailable) return;
                if (ReferenceEquals(_restartCts, cts)) { _restartCts = null; cts.Dispose(); }
                StartSourceLocked();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
}
```

Notes for the implementer:
- The test's fake `delay` returns a `TaskCompletionSource.Task` that completes synchronously inside `ElapseBackoff()`, and `ExecuteSynchronously` makes the continuation run on that same call stack — that's why the tests can assert `Starts` immediately after `ElapseBackoff()`.
- `ReferenceEquals(src, _source)` plus the generation counter are what make `LateExitedEvent_AfterDeactivate_IsIgnored` pass: `SetActive(false)` nulls `_source` and bumps the generation before the fake raises `Exited`.
- Do **not** call into `_source` outside the lock except via the event callbacks designed above; `Stop()` on `PresentMonProcess` can block up to 2 s, which is acceptable on deactivate/exit.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~FrameRateReaderTests" --nologo`
Expected: all PASS. Then `dotnet test --nologo` — whole suite PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Stats.Core/Frames/FrameRateReader.cs tests/Stats.Core.Tests/FrameRateReaderTests.cs
git commit -m "feat(core): FrameRateReader — foreground FPS/frametime/1% low with start/stop lifecycle"
```

---

### Task 7: Wire into the app (`App.xaml.cs`)

**Files:**
- Modify: `src/Stats.App/App.xaml.cs:23` (field), `:50-64` (reader construction), `:102,119` (selection-change hooks), `:116` (window), `:151-160` (OnExit), `:309-344` (OnSettingsChanged PollInterval)

**Interfaces:**
- Consumes: `FrameRateReader.CreateDefault()`, `.SetActive(bool)`, `.Window`, `FrameRateReader.ShouldBeActive(...)`, `CompositeSensorReader(primary, others)`.

- [ ] **Step 1: Add the field and using**

At the top of `App.xaml.cs` add `using Stats.Core.Frames;`. After line 23 (`private ISensorReader? _reader;`) add:
```csharp
    private FrameRateReader? _frameReader;
```

- [ ] **Step 2: Replace reader construction (lines 50-61)**

Replace the block from `IReadOnlyList<MetricDefinition> definitions;` through the end of the `catch` with:
```csharp
        IReadOnlyList<MetricDefinition> definitions;
        try
        {
            (_reader, _frameReader) = BuildReader(() => new LhmSensorReader());
            definitions = _reader.Discover();
        }
        catch (Exception)
        {
            try { _reader?.Dispose(); } catch (Exception) { /* failed reader; fall through to fallback */ }
            (_reader, _frameReader) = BuildReader(() => new PerfCounterSensorReader());
            definitions = _reader.Discover();
        }
```
and add this private static method anywhere in the class (e.g. after `SaveSettings`):
```csharp
    /// <summary>Primary hardware reader + the PresentMon frame reader, merged. Discover() is the caller's.</summary>
    private static (ISensorReader Reader, FrameRateReader Frames) BuildReader(Func<ISensorReader> primaryFactory)
    {
        var frames = FrameRateReader.CreateDefault();
        return (new CompositeSensorReader(primaryFactory(), frames), frames);
    }
```
(`CompositeSensorReader.Dispose` disposes the frame reader on the fallback path; `BuildReader` creates a fresh one, so nothing is reused after disposal.)

- [ ] **Step 3: Activate tracing from selections**

After line 81 (`};` closing the `SensorPoller` initializer) add:
```csharp
        if (_frameReader is not null) _frameReader.Window = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        ApplyFrameTracing();
```
Change line 102 and line 119 to also re-evaluate tracing:
```csharp
        _dashboardVm.OverlayMetricsChanged += () => { _overlayVm.Rebuild(); ApplyFrameTracing(); };
```
```csharp
        _dashboardVm.DashboardMetricsChanged += () => { _peaksVm?.RebuildRows(); ApplyFrameTracing(); };
```
Add the method:
```csharp
    private void ApplyFrameTracing()
    {
        if (_frameReader is null || _settings is null) return;
        _frameReader.SetActive(FrameRateReader.ShouldBeActive(_settings.DashboardMetrics, _settings.OverlayMetrics));
    }
```

- [ ] **Step 4: Keep the window in sync with the poll interval**

In `OnSettingsChanged`, `case SettingsChange.PollInterval:` add a line:
```csharp
                if (_frameReader is not null) _frameReader.Window = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
```

- [ ] **Step 5: Shutdown order**

`OnExit` already calls `_reader?.Dispose()` which disposes the composite → frame reader → kills PresentMon. No change needed; confirm by reading the code.

- [ ] **Step 6: Build, test, and smoke-run**

Run: `dotnet build --nologo && dotnet test --nologo`
Expected: success, all PASS.

Smoke (worker may do this — it does **not** exercise PresentMon): `dotnet run --project src/Stats.App` from the repo; the dashboard opens; open the metric picker and confirm a **Game — Foreground app** group with FPS / 1% Low FPS / Frame Time exists (PresentMon is found in `installer/vendor/` via the locator). Tick FPS: the tile appears showing `—`. Untick. Close the app from the tray. Any crash or missing group is a failure.

- [ ] **Step 7: Commit**

```bash
git add src/Stats.App/App.xaml.cs
git commit -m "feat(app): wire FrameRateReader into the sensor pipeline; trace only while fps.* selected"
```

---

### Task 8: Installer — fetch and ship PresentMon, third-party notice

**Files:**
- Modify: `installer/build.ps1:31-34` (constants), `:65-80` (PawnIO section — add PresentMon after it)
- Modify: `installer/THIRD-PARTY.txt`
- Modify: `README.md` (one paragraph in the install/run-from-source sections)

- [ ] **Step 1: Constants**

After line 34 (`$PawnIoExe = …`) add:
```powershell
$PresentMonVersion = '2.5.1'
$PresentMonUrl     = "https://github.com/GameTechDev/PresentMon/releases/download/v$PresentMonVersion/PresentMon-$PresentMonVersion-x64.exe"
$PresentMonSha256  = '9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191'
$PresentMonExe     = Join-Path $VendorDir "PresentMon-$PresentMonVersion-x64.exe"
```

- [ ] **Step 2: Generalise the hash check**

Replace `Test-PawnIoHash` (lines 48-51) with:
```powershell
function Test-FileHashMatches([string]$Path, [string]$Sha256) {
    if (-not (Test-Path $Path)) { return $false }
    return ((Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256)
}

function Get-VerifiedDownload([string]$Name, [string]$Url, [string]$Path, [string]$Sha256) {
    Write-Host "==> $Name"
    if (Test-FileHashMatches $Path $Sha256) {
        Write-Host "    cached copy verified (SHA-256 OK)"
        return
    }
    Write-Host "    downloading $Url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Path
    if (-not (Test-FileHashMatches $Path $Sha256)) {
        $actual = if (Test-Path $Path) { (Get-FileHash $Path -Algorithm SHA256).Hash } else { '<missing>' }
        Remove-Item $Path -Force -ErrorAction SilentlyContinue
        throw "$Name SHA-256 mismatch. expected $Sha256 actual $actual. Refusing to build."
    }
    Write-Host "    downloaded and verified (SHA-256 OK)"
}
```
and replace the whole `# 2. PawnIO` section (lines 65-80) with:
```powershell
# 2. Third-party binaries -------------------------------------------------
New-Item -ItemType Directory -Force -Path $VendorDir | Out-Null
Get-VerifiedDownload "PawnIO_setup.exe $PawnIoVersion" $PawnIoUrl $PawnIoExe $PawnIoSha256
Get-VerifiedDownload "PresentMon $PresentMonVersion" $PresentMonUrl $PresentMonExe $PresentMonSha256
# PresentMon ships inside the app folder (the [Files] publish glob picks it up); PawnIO stays in vendor/ (dontcopy).
Copy-Item $PresentMonExe (Join-Path $PublishDir 'PresentMon.exe') -Force
Write-Host "    PresentMon.exe copied into publish dir"
```

- [ ] **Step 3: Third-party notice**

In `installer/THIRD-PARTY.txt`, after the LibreHardwareMonitorLib paragraph, add:
```
PresentMon (frame-timing capture tool) — https://github.com/GameTechDev/PresentMon
  Copyright (c) Intel Corporation. Licensed under the MIT License.
  Stats ships the unmodified PresentMon 2.5.1 console executable as PresentMon.exe and
  runs it as a helper process only while an FPS metric is selected. It uses Windows
  Event Tracing to observe frame presentation; it does not modify or inject into games.
```

- [ ] **Step 4: README**

In the README's install section add one bullet: "**FPS counter** — select FPS / 1% Low FPS / Frame Time from the *Game* group. Stats runs the bundled Intel PresentMon helper only while one of them is selected. Launch Stats from the Start menu or desktop shortcut: processes started from the Microsoft Store build of PowerShell/Terminal inherit an MSIX identity that Windows blocks from ETW tracing, so FPS stays blank there." In the run-from-source section add: "For FPS while running from source, run `installer\build.ps1` once (or download `PresentMon-2.5.1-x64.exe` into `installer\vendor\`); the app finds it there."

- [ ] **Step 5: Verify the build script**

Run: `pwsh -NoProfile -File installer/build.ps1 -Version 0.0.0-dev` (takes a few minutes; requires Inno Setup — present on this machine).
Expected output includes `PresentMon 2.5.1` → `cached copy verified (SHA-256 OK)` (the file is already in vendor/), `PresentMon.exe copied into publish dir`, and `Built …\dist\Stats-Setup-0.0.0-dev.exe`. Then:
`Test-Path installer/publish/PresentMon.exe` → True.

- [ ] **Step 6: Commit**

```bash
git add installer/build.ps1 installer/THIRD-PARTY.txt README.md
git commit -m "build(installer): fetch, verify and ship PresentMon 2.5.1; third-party notice; README"
```

---

### Task 9: Real-capture regression fixture (needs a file only the user can produce)

**Files:**
- Create: `tests/Stats.Core.Tests/Fixtures/presentmon-2.5.1-sample.csv` (user-provided)
- Modify: `tests/Stats.Core.Tests/Stats.Core.Tests.csproj` (copy fixture to output)
- Modify: `tests/Stats.Core.Tests/PresentMonCsvParserTests.cs` (append one test)

**Precondition:** the user runs, from an **elevated non-Store** terminal (Win+X → "Terminal (Admin)" is fine *if* its profile is Windows PowerShell; otherwise run `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` as admin), in the repo root:
```powershell
.\installer\vendor\PresentMon-2.5.1-x64.exe --output_file tests\Stats.Core.Tests\Fixtures\presentmon-2.5.1-sample.csv --timed 5 --terminate_after_timed --stop_existing_session --session_name StatsFixture --no_console_stats --no_track_gpu --no_track_input
```
with any 3D app or a browser animating on screen. If the file does not exist when this task is reached, **skip the task and report it as deferred** — do not fabricate a fixture.

- [ ] **Step 1: Trim the fixture** to the header + first 200 data lines (keeps the repo small):
```powershell
$f='tests/Stats.Core.Tests/Fixtures/presentmon-2.5.1-sample.csv'; Get-Content $f -TotalCount 201 | Set-Content $f
```

- [ ] **Step 2: Copy to output** — add to `Stats.Core.Tests.csproj`:
```xml
  <ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Append the test** to `PresentMonCsvParserTests`:
```csharp
    [Fact]
    public void RealCapture_2_5_1_ParsesWithoutFormatError_AndYieldsSamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "presentmon-2.5.1-sample.csv");
        var p = new PresentMonCsvParser();
        int samples = 0;
        foreach (var line in File.ReadLines(path))
            if (p.Parse(line) is FrameSample s) { samples++; Assert.True(s.Pid > 0); Assert.True(s.FrameTimeMs > 0); }
        Assert.True(p.HeaderParsed);
        Assert.True(samples > 0, "expected at least one frame sample from the real capture");
    }
```

- [ ] **Step 4: Run** `dotnet test --nologo` → PASS. If the header uses a column name the parser does not know, **fix the parser** (add the name to `IntervalColumnNames`) and extend the unit tests — that is exactly what this fixture is for.

- [ ] **Step 5: Commit**

```bash
git add tests/Stats.Core.Tests/Fixtures/presentmon-2.5.1-sample.csv tests/Stats.Core.Tests/Stats.Core.Tests.csproj tests/Stats.Core.Tests/PresentMonCsvParserTests.cs
git commit -m "test(core): real PresentMon 2.5.1 capture as parser regression fixture"
```

---

### Task 10: Manual end-to-end verification (user + Fable, not a worker)

- [ ] Build and install: `.\installer\build.ps1 -Version 1.2.0-beta` then run `dist\Stats-Setup-1.2.0-beta.exe` (upgrades in place; it closes the running Stats).
- [ ] Launch Stats **from the Start menu** (not from any terminal). Open the picker → Game group → put **FPS** and **Frame Time** on the overlay, **1% Low FPS** on the dashboard.
- [ ] Task Manager → Details: `PresentMon.exe` is running as a child of `Stats.App.exe`.
- [ ] Start a game (or any fullscreen 3D app). Overlay FPS roughly matches the game's own counter / RTSS; frame time ≈ 1000/FPS; 1% low appears after ~2 s and is ≤ FPS.
- [ ] Alt-tab to the desktop: within one poll the tiles show `—`. Alt-tab back: values return without restart.
- [ ] Untick all three: `PresentMon.exe` disappears from Task Manager. Re-tick one: it reappears.
- [ ] Exit Stats from the tray: no orphan `PresentMon.exe`.
- [ ] Negative check: launch `C:\Program Files\Stats\Stats.App.exe` from the Store pwsh (this session's shell) with an FPS metric selected → tiles stay `—`, app keeps working, no crash. (Expected: access-denied path.)

---

## Self-review notes

- Spec coverage: metrics/group (T1), parser incl. fallback & errors (T2), aggregator thresholds 10/100/1000/10 s (T3), composite merge + fault isolation (T4), process flags/locator/foreground/exclude-self (T5), lifecycle + backoff + access-denied + bad-header (T6), activation predicate & Window from settings & shutdown (T7), build.ps1/THIRD-PARTY/README (T8), real fixture (T9), manual verification incl. negative MSIX case (T10). Thresholds for FPS deliberately absent per spec. `Stats.iss` unchanged per spec.
- Type consistency checked: `FrameSample(int Pid, double FrameTimeMs)`, `FrameStats(float? Fps, float? FrameTimeMs, float? OnePercentLowFps)`, `IFrameSource` events `Action<string>` / `Action<int,string>`, `FrameRateReader` ctor `(string?, Func<IFrameSource>, Func<int?>, Func<DateTime>?, Func<TimeSpan,CancellationToken,Task>?)`, `CompositeSensorReader(ISensorReader primary, params ISensorReader[] others)` used identically in T4 tests and T7.
