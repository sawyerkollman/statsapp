# Per-process top consumers (Processes tab) — design

Date: 2026-09-11. Base: `feature/v1.10` @ `4323b0b` (master v1.9.2 + dashboard layout modes + graph effects).
Branch `feature/process-monitor`. Touches the same shared files both base features touched
(`AppSettings`, `SettingsViewModel`, the Settings flyout of `DashboardWindow.xaml`, `App.xaml.cs`, the harness
composition/catalog/manifest) and, like them, only *appends* to each; everything else is new files under
`src/Stats.Core/Processes/`, a new tab in `PeaksWindow.xaml`, and new harness/test files.

Owner ask: answer "what is eating my CPU, GPU, or memory right now" — a background sampler that lists the top
processes by CPU %, memory and (when available) GPU %, shown as a new **Processes** tab in the Peaks window with
sortable columns and a Copy-as-TSV action like the Peaks tab.

## Goal

1. **Processes tab** in `PeaksWindow` (third `TabItem`, after Peaks and Alerts): one table, top **12** rows,
   columns **Process | CPU % | GPU % | Memory**, grouped by process name with a pid count ("chrome ×14"), sorted
   by the active column (descending), with a **Copy** button that puts the shown rows on the clipboard as TSV.
2. **Sampler** on its own background loop (`ProcessSampler`, modelled on `SensorPoller`), interval
   `max(2 s, PollIntervalSeconds)`, running **only while the Peaks window is visible**; parked (not polling) the
   moment the window hides, so the idle cost of the feature with the window closed is exactly zero.
3. **Degrades, never crashes**: a process that denies access or exits mid-read is skipped silently; if
   enumeration itself fails the tab shows "Process list unavailable — <reason>" and retries next tick; the GPU
   column exists only while the "GPU Engine" performance-counter category is present and fast enough.
4. **Testable core**: deltas, grouping, sorting, top-N, formatting and TSV live in `Stats.Core` behind an
   `IProcessSource` interface with a fake; the harness renders the tab from fixture rows through the *real*
   sampler/VM path without ever enumerating a process.

## Owner decisions (already made — recorded, not re-litigated)

1. Sampling runs on its own timer, interval = `max(2 s, PollIntervalSeconds)`; never on the LHM poller thread
   and never touching LibreHardwareMonitor. Per-process access failures (protected/system processes) are skipped
   silently; the whole sampler degrades to "unavailable" with a visible reason if enumeration fails.
2. CPU % = Δ`TotalProcessorTime` / (interval × `Environment.ProcessorCount`); memory = `WorkingSet64` and
   `PrivateMemorySize64`; GPU % from the "GPU Engine" performance-counter category summed per pid across engine
   instances, optional and cached, with the whole GPU column hidden when the category is missing or too slow
   (> 250 ms per sample).
3. Top N = 12 rows, processes grouped by name with pid count (e.g. "chrome ×14") and aggregated resources; Idle
   and System excluded; the sampler is off while the Peaks window is closed (`ProcessSamplingEnabled`, default
   true, only gates the feature).
4. Core logic (deltas, grouping, sorting, formatting) lives in `Stats.Core` behind an `IProcessSource` interface
   with a fake for tests; the real source lives in `Stats.Core` using `System.Diagnostics` only.

## Owner decisions assumed (decided here; flag in the PR if you disagree)

1. **Stats itself is listed**, not excluded (unlike `Frames/ForegroundProcess.cs`, which excludes
   `Environment.ProcessId` for a different reason): the honest answer to "what's eating my CPU" includes Stats.
2. **Sort is session-only**: default CPU, always descending, and only the three numeric headers are clickable
   (sorting a "top consumers" list by name is not a question anyone asks). Nothing is persisted, so no new
   enum converter/Normalize work (rule 3 is satisfied by the one new bool).
3. **The visible Memory column is the working set** (`WorkingSet64` — resident RAM right now); private bytes
   (`PrivateMemorySize64`) are in the cell tooltip and the TSV. Sorting by Memory sorts by working set.
4. **PID reuse** is handled by "processor time went backwards → treat as a new process (CPU shows —)" instead of
   reading `Process.StartTime` (one more handle query per process, and it throws for protected processes).
5. **"Too slow" GPU counters** = three *consecutive* reads over 250 ms (a debounce, so one hiccup doesn't hide
   the column for the session); once disabled, the column stays hidden until Stats restarts. Per-pid and
   per-group GPU sums are clamped to 100.
6. **Priming**: `Start`/`Resume` samples immediately, so rows with memory appear at once and CPU/GPU read "—"
   until the second sample one interval later. Deltas are reset on every `Resume` so a value never averages over
   the time the window was hidden.
7. **Hide = `Pause` (non-blocking park), not `Stop` (join)**: hiding a window must never block the UI thread for
   up to 2 s. `Stop()` (cancel + join, like `SensorPoller.Stop`) is exit-only.
8. **Formatting**: percentages `F1` + " %" ("18.4 %"), memory "845 MB" (`F0`) below 1 GiB and "3.9 GB" (`F1`)
   above; "—" for unknown. TSV numbers: percent `0.#`, memory in MB `0.#`, no units.
9. **TSV always has the GPU column** (empty cells when unavailable), so the pasted schema is stable.
10. **No per-tab gating**: owner decision 3 says "while the Peaks window is closed", so the sampler runs
    whenever the window is visible, whichever tab is selected (a 2 s process-table walk is tens of ms).
11. **Window title "Stats — Session peaks" and the tray/dashboard "Peaks" labels are unchanged** (non-goal here;
    listed as an open question for the owner).
12. A one-line summary under the tab header: "Showing 12 of 143 processes · updated 14:30:05".
13. The setting lives under **Settings → Monitoring**, new header **"Processes"** after "History window".
14. Harness substate names are prefixed `proc-` (the `PreviewComposition.ApplySubstate` switch is one global
    switch, and `empty`/`populated`/`long-names`/`off` are already taken by peaks/fans).
15. The harness runs the **real** `ProcessSampler.SampleOnce()` synchronously over a `FakeProcessSource` (loop
    never started) with a pinned processor count of 16, so captured numbers are machine-independent.
16. Evidence and review docs go in `docs/process-monitor/` (`EVIDENCE.md`, `REVIEW.md`), mirroring
    `docs/graph-effects/`.

## Settings (rule 3: every field defaulted, sanitized, old files load)

- `AppSettings.ProcessSamplingEnabled` (`bool`, default **true**) in a new `// ---- v1.10 processes ----` block
  at the end of `src/Stats.Core/Settings/AppSettings.cs` (after the `// ---- graph effects ----` block). Doc
  comment: "Sample the top processes while the Peaks window is open (Processes tab). Off = the tab shows an
  'off' message and no process enumeration ever runs."
- `SettingsService.Normalize`: nothing to clamp (bool); no collection; no enum. A pre-1.10 `settings.json`
  without the field loads as `true`.
- `SettingsChange` (`src/Stats.Core/ViewModels/SettingsViewModel.cs` line 13): append **`Processes`** at the
  END (`…, UiScale, Graphs, Processes`). Never reorder.
- `SettingsViewModel`: `[ObservableProperty] private bool _processSamplingEnabled;` mirrored in the ctor
  (`_processSamplingEnabled = settings.ProcessSamplingEnabled;`), and
  `partial void OnProcessSamplingEnabledChanged(bool value) { if (!_loaded) return; _s.ProcessSamplingEnabled = value; Raise(SettingsChange.Processes); }`
  — exactly the `OnSmoothLinesChanged` shape.
- Sampling interval is **not** a setting: `ProcessSampler.IntervalFor(settings.PollIntervalSeconds)` =
  `TimeSpan.FromSeconds(Math.Max(ProcessSampler.MinIntervalSeconds /* 2.0 */, pollIntervalSeconds))`, re-applied
  on `SettingsChange.PollInterval`.

## Core (`Stats.Core`, WPF-free, testable)

All new types are in namespace `Stats.Core.Processes` under `src/Stats.Core/Processes/` unless noted.
`System.Diagnostics.Process` is BCL and `System.Diagnostics.PerformanceCounter` 10.0.11 is already referenced
by `src/Stats.Core/Stats.Core.csproj` — no new packages.

### Records (`ProcessSample.cs`)

```csharp
/// One process as read by an IProcessSource on one tick. Name is Process.ProcessName (no ".exe").
public sealed record ProcessSample(int Pid, string Name, TimeSpan TotalProcessorTime, long WorkingSetBytes, long PrivateBytes);

/// One source tick. GpuPercentByPid null = no per-pid values this tick (priming read, or counters unavailable);
/// GpuAvailable is the column's visibility (category present and not disabled), independent of that.
public sealed record ProcessSourceSnapshot(
    IReadOnlyList<ProcessSample> Processes,
    DateTime TimestampUtc,
    bool GpuAvailable,
    IReadOnlyDictionary<int, float>? GpuPercentByPid);

/// Per-pid usage after the CPU delta; CpuPercent null on first sight / pid reuse / bad elapsed.
public sealed record ProcessUsage(int Pid, string Name, float? CpuPercent, long WorkingSetBytes, long PrivateBytes);

/// Aggregated by process name.
public sealed record ProcessGroup(string Name, int PidCount, float? CpuPercent, float? GpuPercent, long WorkingSetBytes, long PrivateBytes);

/// What ProcessSampler publishes. Groups is unsorted and uncut (the VM sorts and takes TopN, so a sort change
/// never needs a new sample). Immutable — crosses from the sampler thread to the Dispatcher as-is.
public sealed record ProcessListSnapshot(IReadOnlyList<ProcessGroup> Groups, bool GpuAvailable, DateTime TimestampUtc, string? UnavailableReason = null)
{
    public bool IsUnavailable => UnavailableReason is not null;
    public static ProcessListSnapshot Unavailable(string reason, DateTime timestampUtc) =>
        new(Array.Empty<ProcessGroup>(), GpuAvailable: false, timestampUtc, reason);
}
```

### `ProcessSortColumn.cs`

`public enum ProcessSortColumn { Cpu, Memory, Gpu }` — session-only UI state, never serialized (append-only by
habit anyway).

### `IProcessSource.cs`

```csharp
public interface IProcessSource : IDisposable
{
    string Name { get; }
    /// Enumerates every readable process right now. Per-process failures are skipped inside; this throws only when
    /// enumeration itself fails (Process.GetProcesses()), which the sampler turns into an "unavailable" snapshot.
    ProcessSourceSnapshot Sample();
    /// Forget inter-sample state (GPU counter previous samples) so the next Sample() is a first sight.
    void Reset();
}
```

### `ProcessCpuTracker.cs` (pure)

```csharp
public sealed class ProcessCpuTracker
{
    public ProcessCpuTracker(int processorCount);   // > 0; production passes Environment.ProcessorCount
    public int ProcessorCount { get; }
    public IReadOnlyList<ProcessUsage> Apply(ProcessSourceSnapshot snapshot);
    public void Reset();
}
```

`Apply`: `elapsed = (snapshot.TimestampUtc - _previousTimestamp).TotalSeconds`. For each sample: if the pid was
seen last tick with `prev.TotalProcessorTime <= cur.TotalProcessorTime` and `elapsed > 0`, then
`CpuPercent = (float)Math.Clamp((cur - prev).TotalSeconds / (elapsed * ProcessorCount) * 100.0, 0, 100)`; otherwise
`null` (first sight, pid reuse where time went backwards, zero/negative elapsed). The previous map is *replaced*
by this tick's pids (exited processes are forgotten; a pid that reappears later starts fresh). `Reset()` clears
the map and the timestamp.

### `ProcessGrouping.cs` (pure static)

```csharp
public static class ProcessGrouping
{
    public static IReadOnlyList<ProcessGroup> Group(IReadOnlyList<ProcessUsage> usages, IReadOnlyDictionary<int, float>? gpuPercentByPid);
}
```

Groups by `Name` with `StringComparer.OrdinalIgnoreCase`; `Name` = the first-seen spelling; `PidCount` = group
size; `WorkingSetBytes`/`PrivateBytes` = sums; `CpuPercent` = sum of the non-null members clamped to 100, or
`null` when every member is null; `GpuPercent` = `null` when `gpuPercentByPid` is null, else the sum over the
group's pids (pids with no entry count 0) clamped to 100. Output order is unspecified (the VM sorts).

### `ProcessSampler.cs` (background loop, modelled on `src/Stats.Core/Sensors/SensorPoller.cs`)

```csharp
public sealed class ProcessSampler : IDisposable
{
    public const double MinIntervalSeconds = 2.0;
    public static TimeSpan IntervalFor(double pollIntervalSeconds) => TimeSpan.FromSeconds(Math.Max(MinIntervalSeconds, pollIntervalSeconds));

    public ProcessSampler(IProcessSource source, int? processorCount = null);  // null → Environment.ProcessorCount
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(MinIntervalSeconds);
    public bool IsRunning { get; }   // loop task exists (Start called, Stop not yet)
    public bool IsActive { get; }    // sampling, i.e. not parked by Pause (volatile)
    /// Fires on the sampler thread. Each subscriber runs in its own try/catch (Trace "[Stats.ProcessSampler] …").
    public event Action<ProcessListSnapshot>? SampleAvailable;
    /// One sample: source → tracker → grouping → snapshot; raises SampleAvailable; never throws. Not thread-safe —
    /// the loop is its only production caller (the harness calls it on a sampler it never starts).
    public ProcessListSnapshot SampleOnce();
    public void Start();    // idempotent; creates the loop and activates it (first sample immediately, then every Interval)
    public void Pause();    // IsActive = false; the loop parks after its current iteration; never blocks
    public void Resume();   // no-op when already active; else source.Reset() + tracker.Reset(), IsActive = true, wake the loop (Start() if never started)
    public bool Stop();     // cancel + Wait(2 s) → true when the loop joined (exit only)
    public void Dispose();  // Stop(); source.Dispose() only when joined, else Trace and skip (same rule as reader.Dispose in App.OnExit)
}
```

Loop body:

```
while (!ct.IsCancellationRequested)
{
    if (!IsActive) { await _wake.WaitAsync(ct); continue; }   // SemaphoreSlim(0, 1): parked = zero CPU, no timer
    SampleOnce();
    await Task.Delay(Interval, ct);
}
```

`Resume()` releases `_wake` only when `CurrentCount == 0`. `SampleOnce()`: `try { src = _source.Sample(); }
catch (Exception ex) { failure episode → ProcessListSnapshot.Unavailable(FirstLine(ex.Message), DateTime.UtcNow) }`;
otherwise `usages = _tracker.Apply(src)`, `groups = ProcessGrouping.Group(usages, src.GpuPercentByPid)`,
`snapshot = new(groups, src.GpuAvailable, src.TimestampUtc)`. Failure episodes trace once on the first failure
and once on recovery (the `SensorPoller.RecordFailure`/`RecordRecovery` idea, without the health type). The
`ProcessCpuTracker` is constructed with `processorCount ?? Environment.ProcessorCount`.

### `SlowSampleGate.cs` (pure)

```csharp
public sealed class SlowSampleGate
{
    public SlowSampleGate(TimeSpan threshold, int consecutiveSamples);
    public bool IsTripped { get; }
    /// Records one sample's duration; a slow sample extends the streak, a fast one resets it; trips (sticky) once
    /// the streak reaches consecutiveSamples. Returns IsTripped.
    public bool Record(TimeSpan elapsed);
}
```

### `GpuEngineCounters.cs` (real, `System.Diagnostics.PerformanceCounterCategory`)

```csharp
public sealed class GpuEngineCounters
{
    public const string CategoryName = "GPU Engine";
    public const string CounterName = "Utilization Percentage";
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(250);
    public const int SlowSamplesToDisable = 3;

    public bool IsAvailable { get; }            // true until the first failure or the slow gate trips
    public string? UnavailableReason { get; }   // e.g. "GPU Engine counters missing" / "GPU Engine counters too slow (>250 ms)"
    /// One read of the whole category (PerformanceCounterCategory.ReadCategory(), a single registry query — not one
    /// PerformanceCounter per instance). Returns per-pid utilisation (0–100, engines summed then clamped) computed
    /// with CounterSample.Calculate(previous, current) — the framework's per-counter-type formula — so the counter
    /// type is never hard-coded; null on the priming read and whenever unavailable. Never throws.
    public IReadOnlyDictionary<int, float>? TryRead();
    public void Reset();   // drop previous samples (next TryRead primes again)
    /// "pid_1234_luid_0x00000000_0x0000E5A3_phys_0_eng_3_engtype_3D" → 1234. Pure; false for anything malformed.
    public static bool TryParsePid(string instanceName, out int pid);
}
```

Rules: never call `PerformanceCounterCategory.Exists` (it enumerates every category — seconds); construct the
category and let `ReadCategory()` throw. Any exception → `IsAvailable = false` with the exception's first line as
the reason. Each read is timed with `Stopwatch`; `new SlowSampleGate(SlowThreshold, SlowSamplesToDisable)` decides
"too slow". Instances whose name has no parseable pid are ignored. Previous samples keyed by instance name;
instances that vanished are dropped.

### `SystemProcessSource.cs` (real; the only place `Process.GetProcesses()` is called for this feature)

`public sealed class SystemProcessSource : IProcessSource` — `Name => "System processes"`. `Sample()`:
`timestamp = DateTime.UtcNow; procs = Process.GetProcesses()` (let it throw). For each `Process`, inside one
`try { … } catch (Exception) { skip } finally { p.Dispose(); }` (the `ConflictingFanSoftware.RunningProcessNames`
discipline in `src/Stats.Core/Fans/ConflictingFanSoftware.cs`): read `Id`, `ProcessName`, skip when `Id` is 0 or
4 or `ProcessName` is `"Idle"` or `"System"` (ordinal-ignore-case), then `TotalProcessorTime`, `WorkingSet64`,
`PrivateMemorySize64` → `ProcessSample`. `Win32Exception` (access denied on protected processes, even elevated)
and `InvalidOperationException` (exited mid-read) are the routine cases the catch swallows — no tracing per
process. Then `gpu = _gpu.TryRead()` → `new ProcessSourceSnapshot(samples, timestamp, _gpu.IsAvailable, gpu)`.
`Reset()` → `_gpu.Reset()`. `Dispose()` → nothing to release (kept for symmetry with `ISensorReader`).

### `ProcessFormat.cs` (pure static, `Stats.Core.Processes`)

- `string Percent(float? v)` → `"—"` or `v.Value.ToString("F1", InvariantCulture) + " %"`.
- `string Bytes(long b)` → `b >= 1 GiB ? (b / 1 GiB).ToString("F1") + " GB" : (b / 1 MiB).ToString("F0") + " MB"`
  (1 GiB = 1 073 741 824, 1 MiB = 1 048 576; InvariantCulture).
- `string Megabytes(long b)` → `(b / 1 MiB).ToString("0.#", InvariantCulture)` (TSV).
- `string PercentTsv(float? v)` → `""` or `v.Value.ToString("0.#", InvariantCulture)` (TSV).

### `ProcessListViewModel` + `ProcessRowViewModel` (`src/Stats.Core/ViewModels/ProcessListViewModel.cs`)

```csharp
public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel(ProcessGroup group);
    public string Key { get; }                    // group.Name — the in-place update key
    public ProcessGroup Group { get; private set; }
    [ObservableProperty] private string _name;            // "chrome ×14" (PidCount > 1) or "explorer"
    [ObservableProperty] private string _nameToolTip;     // "chrome — 14 processes" / "explorer — 1 process"
    [ObservableProperty] private string _cpuText;         // ProcessFormat.Percent
    [ObservableProperty] private string _gpuText;         // ProcessFormat.Percent
    [ObservableProperty] private string _memoryText;      // ProcessFormat.Bytes(WorkingSetBytes)
    [ObservableProperty] private string _memoryToolTip;   // "Working set 3.9 GB · Private 3.1 GB"
    public void Update(ProcessGroup group);       // sets Group + every text (each setter no-ops when unchanged)
}

/// The Processes tab: top-N rows by the active column, updated in place from ProcessListSnapshots.
public sealed partial class ProcessListViewModel : ObservableObject
{
    public const int TopN = 12;
    public const string SamplingText = "Sampling processes…";
    public const string OffText = "Process sampling is off — turn it on under Settings → Monitoring → Processes.";

    public ObservableCollection<ProcessRowViewModel> Rows { get; }
    [ObservableProperty] private ProcessSortColumn _sortColumn = ProcessSortColumn.Cpu;  // OnSortColumnChanged: raise IsSortedBy*, Reapply()
    public bool IsSortedByCpu { get; } public bool IsSortedByMemory { get; } public bool IsSortedByGpu { get; }
    [RelayCommand] private void SortBy(ProcessSortColumn column);   // ignored for Gpu while !IsGpuAvailable
    [ObservableProperty] private bool _isGpuAvailable;     // GPU column visibility
    [ObservableProperty] private bool _isEnabled = true;   // mirrors AppSettings.ProcessSamplingEnabled (via SetEnabled)
    [ObservableProperty] private bool _isUnavailable;
    [ObservableProperty] private string _statusText = SamplingText;   // shown by the empty-state TextBlock (Rows.Count == 0)
    [ObservableProperty] private string _summaryText = "";           // "Showing 12 of 143 processes · updated 14:30:05"
    public bool HasSummary => SummaryText.Length > 0;                // raised with SummaryText
    [ObservableProperty] private string _copyError = "";             // same contract as PeaksViewModel.CopyError
    public bool HasCopyError => CopyError.Length > 0;
    public ProcessListSnapshot? Latest { get; }

    public void Apply(ProcessListSnapshot snapshot);   // UI thread only
    public void SetEnabled(bool enabled);
    public string ToTsv();
}
```

`Apply(snapshot)`: ignored while `!IsEnabled`. If `snapshot.IsUnavailable`: `Latest = snapshot`, `Rows.Clear()`,
`IsUnavailable = true`, `SummaryText = ""`, `StatusText = $"Process list unavailable — {reason}"`. Otherwise:
`Latest = snapshot; IsUnavailable = false; IsGpuAvailable = snapshot.GpuAvailable;` if `!IsGpuAvailable &&
SortColumn == Gpu` then `SortColumn = Cpu` (which re-enters `Reapply` — guard so rows are reconciled once);
`Reapply()`; `SummaryText = $"Showing {Rows.Count} of {snapshot.Groups.Count} processes · updated {snapshot.TimestampUtc.ToLocalTime():HH:mm:ss}"`;
`StatusText = Rows.Count == 0 ? "No processes readable" : SamplingText` (only visible when empty).

`Reapply()` (from `Apply` and `OnSortColumnChanged`): `target = Latest.Groups` ordered by the key
(`Cpu → CpuPercent ?? -1`, `Memory → WorkingSetBytes`, `Gpu → GpuPercent ?? -1`) **descending**, then `Name`
ordinal-ignore-case ascending, `Take(TopN)`. Reconcile `Rows` in place, keyed by `Key`: for index `i` over
`target`, if `Rows[i].Key == g.Name` → `Rows[i].Update(g)`; else if a row with that key exists at `j > i` →
`Rows.Move(j, i)` then `Update`; else `Rows.Insert(i, new ProcessRowViewModel(g))`. Afterwards remove every row
past `target.Count`. Rows are never rebuilt wholesale (no scroll reset, no flicker, containers preserved).

`SetEnabled(false)`: `IsEnabled = false; Latest = null; Rows.Clear(); SummaryText = ""; IsUnavailable = false;
StatusText = OffText`. `SetEnabled(true)`: `IsEnabled = true; StatusText = SamplingText` (the next `Apply`
fills rows).

`ToTsv()`: header `"Process\tProcesses\tCPU %\tGPU %\tWorking set MB\tPrivate MB"` then one line per row in
display order: `Group.Name`, `PidCount`, `ProcessFormat.PercentTsv(CpuPercent)`, `PercentTsv(GpuPercent)` (empty
when null — always the column), `Megabytes(WorkingSetBytes)`, `Megabytes(PrivateBytes)`; lines joined with
`'\n'` like `PeaksViewModel.ToTsv()` in `src/Stats.Core/ViewModels/PeaksViewModel.cs`.

### `PeaksViewModel` (`src/Stats.Core/ViewModels/PeaksViewModel.cs`)

Ctor becomes `PeaksViewModel(MetricStore store, AppSettings settings, AlertLogViewModel? alertLog = null, ProcessListViewModel? processes = null)`
and gains `public ProcessListViewModel Processes { get; }` = `processes ?? new ProcessListViewModel()` — the
exact `AlertLog` pattern (composition root passes its own instance; tests and other callers get a default).
Nothing else in the class changes.

## App (`Stats.App`)

### `src/Stats.App/Views/PeaksWindow.xaml`

- Add `xmlns:proc="clr-namespace:Stats.Core.Processes;assembly=Stats.Core"`.
- `<Window.Resources>`: one local style
  `<Style x:Key="ColumnHeaderButton" TargetType="Button" BasedOn="{StaticResource HeaderButton}">` with setters
  `Padding="4,1"`, `Margin="0"`, `MinHeight="0"`, `Background="Transparent"`,
  `Foreground="{DynamicResource TextSecondary}"`, `FontSize="{StaticResource FontSizeDense}"`,
  `HorizontalAlignment="Right"`. (`HeaderButton` lives in `src/Stats.App/Views/Controls.xaml` and already gives
  hover/pressed/keyboard-focus states and a nulled default focus visual; `Controls.xaml` is not touched.)
- Third `<TabItem Header="Processes">` after Alerts, root `<DockPanel Margin="0,8,0,0" DataContext="{Binding Processes}">`,
  copied from the Alerts tab's structure:
  - Header `DockPanel`: `<Button DockPanel.Dock="Right" Content="Copy" Click="CopyProcesses_Click" Style="{StaticResource HeaderButton}"/>`
    and `<TextBlock Text="Top processes" FontSize="{StaticResource FontSizeTitle}" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimary}"/>`.
  - `<TextBlock DockPanel.Dock="Top" Text="{Binding SummaryText}" Foreground="{DynamicResource TextSecondary}" FontSize="{StaticResource FontSizeDense}" Margin="0,0,0,6" Visibility="{Binding HasSummary, Converter={StaticResource BoolToVis}}"/>`.
  - `<TextBlock DockPanel.Dock="Top" Text="{Binding CopyError}" …CritBrush/FontSizeDense… Visibility="{Binding HasCopyError, Converter={StaticResource BoolToVis}}"/>` (verbatim from the Peaks tab).
  - `<Grid>` with the empty-state `TextBlock Text="{Binding StatusText}"` (same `Style`/`DataTrigger Rows.Count == 0`
    block as the Peaks tab, `TextWrapping="Wrap" MaxWidth="360"`) and the `ScrollViewer > DockPanel
    Grid.IsSharedSizeScope="True" Width="{Binding ViewportWidth, RelativeSource={RelativeSource AncestorType=ScrollViewer}}" MinWidth="428"`.
  - Header `Grid` (`Margin="6,0,6,4"`) columns: `Width="*" MinWidth="140"` (Process), `Width="Auto" MinWidth="72" SharedSizeGroup="ProcCpu"`,
    `Width="Auto" SharedSizeGroup="ProcGpu"` (**no MinWidth** — the GPU column collapses to 0 when hidden; the
    cells carry `MinWidth="72"` instead), `Width="Auto" MinWidth="72" SharedSizeGroup="ProcMem"`. Cells:
    `TextBlock "Process"` (TextSecondary, FontSizeDense) and three buttons:
    ```xml
    <Button Grid.Column="1" Style="{StaticResource ColumnHeaderButton}" Command="{Binding SortByCommand}"
            CommandParameter="{x:Static proc:ProcessSortColumn.Cpu}" ToolTip="Sort by CPU" AutomationProperties.Name="Sort by CPU">
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="▼" Margin="0,0,3,0" Foreground="{DynamicResource AccentBrush}"
                       Visibility="{Binding IsSortedByCpu, Converter={StaticResource BoolToVis}}"/>
            <TextBlock Text="CPU %"/>
        </StackPanel>
    </Button>
    ```
    likewise `Gpu` ("GPU %", `MinWidth="72"`, `Visibility="{Binding IsGpuAvailable, Converter={StaticResource BoolToVis}}"`)
    and `Memory` ("Memory"). The accent arrow is the only "active column" indicator.
  - Row `ItemsControl ItemsSource="{Binding Rows}"`, `Border Background="{DynamicResource TileBg}" CornerRadius="4" Padding="6,4" Margin="0,2"`
    with the same four columns: `TextBlock Text="{Binding Name}" ToolTip="{Binding NameToolTip}" TextTrimming="CharacterEllipsis"`
    (TextPrimary, FontSizeLabel); `CpuText` (TextPrimary, SemiBold, `TextAlignment="Right" Typography.NumeralAlignment="Tabular"`);
    `GpuText` (TextSecondary, right, tabular, `MinWidth="72"`,
    `Visibility="{Binding DataContext.IsGpuAvailable, RelativeSource={RelativeSource AncestorType=ItemsControl}, Converter={StaticResource BoolToVis}}"`);
    `MemoryText` (TextPrimary, right, tabular, `ToolTip="{Binding MemoryToolTip}"`). No severity brushes anywhere
    in this tab, so no `RaiseSeverityRefresh` hook is needed on theme change.
- The existing Peaks and Alerts tabs are byte-identical; the implicit `TabItem` style in
  `src/Stats.App/Views/AppStyles.xaml` gives the third tab the accent underline for free.

### `src/Stats.App/Views/PeaksWindow.xaml.cs`

Add `CopyProcesses_Click` beside `Copy_Click`:
```csharp
private void CopyProcesses_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is not PeaksViewModel vm) return;
    try { Clipboard.SetText(vm.Processes.ToTsv()); vm.Processes.CopyError = ""; }
    catch (Exception ex) { vm.Processes.CopyError = $"Copy failed: {ex.Message}"; }
}
```
`AllowClose`/`OnClosing` unchanged.

### `src/Stats.App/App.xaml.cs` (composition root)

- Fields (next to `_processScan`/`_fansVisible`, ~line 65): `private ProcessSampler? _processSampler;`
  `private ProcessListViewModel? _processListVm;` `private ProcessListSnapshot? _latestProcessSnapshot;`
  `private readonly RefreshCoalescer _processCoalescer = new();` (a second, independent latch — the sensor one is
  untouched; `RefreshCoalescer` in `src/Stats.Core/Refresh/RefreshCoalescer.cs` is per-producer by design).
- `ShowPeaks()` (~line 635): when creating the window, `_processListVm = new ProcessListViewModel();`
  `_processListVm.SetEnabled(_settings.ProcessSamplingEnabled);`
  `_peaksVm = new PeaksViewModel(_store, _settings, _alertLog, _processListVm);` and
  `_peaks.IsVisibleChanged += (_, _) => UpdateProcessSampling();`. After the existing `Activate()`, call
  `UpdateProcessSampling()` (covers the already-visible re-show, where `IsVisibleChanged` does not fire).
- New `private void UpdateProcessSampling()`: `bool want = _settings?.ProcessSamplingEnabled == true && _peaks is { IsVisible: true };`
  `if (want) EnsureProcessSampler().Resume(); else _processSampler?.Pause();`.
- New `private ProcessSampler EnsureProcessSampler()`: lazily `new ProcessSampler(new SystemProcessSource()) { Interval = ProcessSampler.IntervalFor(_settings!.PollIntervalSeconds) }`,
  subscribe once:
  ```csharp
  sampler.SampleAvailable += snapshot =>
  {
      Volatile.Write(ref _latestProcessSnapshot, snapshot);
      if (_processCoalescer.TryPost()) Dispatcher.BeginInvoke(RunProcessRefresh);
  };
  ```
  — the `_poller.SnapshotAvailable` shape at App.xaml.cs ~line 248. Started with `Start()` (which activates) —
  `Resume()` on a never-started sampler must call `Start()` itself, so `EnsureProcessSampler` does not start.
- New `private void RunProcessRefresh()`: `_processCoalescer.Take(); var s = Interlocked.Exchange(ref _latestProcessSnapshot, null); if (s is null || _processListVm is null) return; if (_peaks is { IsVisible: true }) _processListVm.Apply(s);`
  (the `RunCoalescedRefresh` discipline at ~line 535; `RunCoalescedRefresh` itself is not modified).
- `OnSettingsChanged` (~line 729): in `case SettingsChange.PollInterval:` append
  `if (_processSampler is not null) _processSampler.Interval = ProcessSampler.IntervalFor(_settings.PollIntervalSeconds);`;
  new `case SettingsChange.Processes: _processListVm?.SetEnabled(_settings.ProcessSamplingEnabled); UpdateProcessSampling(); break;`.
- `OnExit` (~line 273): directly after `_processScan?.Dispose();` add `_processSampler?.Dispose();` (Stop + join
  up to 2 s, then source dispose only if joined). The rest of the exit order — `_poller.Stop()` →
  `_fanController.RestoreAll()` → `_reader.Dispose()` if joined → `SaveSettings()` — is unchanged. `FatalCleanup`
  is **not** touched: the sampler holds no hardware or fan state and disposes every `Process` handle per tick.
- Rule 1 audit: the sampler never references `_reader`, `_poller`, `_fanController`, or any LHM type; its only
  shared touch points are the immutable snapshot field and the Dispatcher.

### `src/Stats.App/Views/DashboardWindow.xaml` (Settings flyout → Monitoring tab)

After the "History window" `StackPanel` (~line 572) and before "Thresholds (warn / crit)":
```xml
<TextBlock Text="Processes" Style="{StaticResource SettingsHeader}"/>
<CheckBox Content="Sample top processes while the Peaks window is open" IsChecked="{Binding ProcessSamplingEnabled}"/>
<TextBlock Margin="0,4,0,0" TextWrapping="Wrap" Foreground="{DynamicResource TextSecondary}" FontSize="11"
           Text="Lists the top 12 processes by CPU, GPU and memory in the Peaks window's Processes tab. Sampled every 2 s (or your poll interval, if longer), only while that window is open."/>
```

### `README.md`

Extend the "▤ Peaks" bullet (~line 177): a **Processes** tab lists the top 12 processes by CPU %, GPU % (when
the driver exposes the "GPU Engine" counters) and memory, grouped by name with a pid count, sortable by clicking
a column header, with **Copy** as TSV (includes both working set and private bytes); sampled every 2 s (or your
poll interval if longer) only while the window is open; Memory is the working set (resident RAM, including shared
pages, so it reads a little above Task Manager's private working set); because Stats runs elevated the list
covers every user's processes, and the copied TSV contains process names — mind where you paste it; the
Settings → Monitoring → Processes checkbox turns sampling off.

## Preview harness (`tools/Stats.UiPreview`)

- `Fixtures/FakeProcessSource.cs` (new): `public sealed class FakeProcessSource : IProcessSource` over a
  `Queue<ProcessSourceSnapshot>`; `Sample()` records `commands.Record($"process.sample (fixture tick {n})")` and
  dequeues (returns the last tick again when empty); `public string? ThrowOnSample { get; set; }` makes `Sample()`
  throw `InvalidOperationException(ThrowOnSample)`; `public bool GpuAvailable { get; set; } = true` — when false
  the returned tick is rewritten with `GpuAvailable = false, GpuPercentByPid = null`; `Reset()`/`Dispose()`
  record nothing and touch nothing. Never calls `Process`/`PerformanceCounter`.
- `Fixtures/ProcessFixtures.cs` (new): `public const int ProcessorCount = 16;` and `static List<ProcessSourceSnapshot> Normal()`,
  `Dense()` built by a local helper `Group(name, pidCount, cpuPercent, gpuPercent, workingSetBytes, privateBytes)`
  that expands to `pidCount` pids (sequential, starting at 1000, unique across the fixture), two ticks at
  `TimeSeries.FixedTimeUtc.AddSeconds(-2)` and `TimeSeries.FixedTimeUtc`, per-pid `TotalProcessorTime` = 0 on
  tick 1 and `TimeSpan.FromTicks((long)Math.Round(cpuPercent / 100.0 * 2 * ProcessorCount * TimeSpan.TicksPerSecond / pidCount))`
  on tick 2, memory split evenly, `GpuPercentByPid` on tick 2 only (`gpuPercent / pidCount` each) and `null` on
  tick 1, `GpuAvailable = true` on both. **Normal** (14 groups, so two fall off the top 12 by CPU):
  chrome ×14 18.4 % / 6.2 % / 3.9 GB ws / 3.1 GB priv; Code ×9 5.7 / 0.9 / 1.6 GB / 1.4 GB; MsMpEng 4.2 / 0 / 320 MB;
  steamwebhelper ×6 3.1 / 0.5 / 780 MB; dwm 2.4 / 4.8 / 210 MB; Discord ×5 2.2 / 1.8 / 560 MB; svchost ×71 1.9 / 0 / 640 MB;
  Teams ×3 1.4 / 0.4 / 610 MB; Spotify ×4 1.1 / 0.3 / 380 MB; Stats 0.8 / 1.1 / 142 MB; explorer 0.6 / 0 / 165 MB;
  audiodg 0.5 / 0 / 24 MB; SearchIndexer 0.3 / 0 / 95 MB; OneDrive 0.2 / 0 / 120 MB (private = 80 % of working
  set where not given). **Dense**: the same plus
  `SomeVeryLongVendorTelemetryBackgroundServiceHostProcessName64` ×128 at 7.9 % / 0 / 2.2 GB and
  `AnotherEquallyLongGameLauncherOverlayHelperProcessWithSuffix` ×2 at 0.9 % / 12.5 % / 900 MB.
- `Fixtures/ScenarioFixture.cs`: `public List<ProcessSourceSnapshot> ProcessTicks { get; init; } = new();`.
  `Fixtures/Scenarios.cs`: `Normal()` sets `ProcessTicks = ProcessFixtures.Normal()`, `Dense()` sets
  `ProcessFixtures.Dense()`; every other scenario leaves it empty.
- `PreviewComposition.cs`: new required init properties `FakeProcessSource ProcessSource` and
  `ProcessSampler ProcessSampler`; `Build()` creates `new FakeProcessSource(fixture.ProcessTicks, commands)`,
  `new ProcessSampler(processSource, processorCount: ProcessFixtures.ProcessorCount)` (**never `Start()`ed** —
  PREVIEW_HARNESS.md: "A fixture run must not leave a background monitoring process"), `new ProcessListViewModel()`,
  and `new PeaksViewModel(store, settings, alertLog, processes)`. `ApplySubstate` gains a `// ---- processes ----`
  block: `proc-sampling` (no-op — documentary, shows `SamplingText`), `proc-populated` (`ApplyProcessTicks`),
  `proc-sorted-memory` (`ApplyProcessTicks` then `c.Peaks.Processes.SortColumn = ProcessSortColumn.Memory`),
  `proc-sorted-gpu` (… `= ProcessSortColumn.Gpu`), `proc-no-gpu` (`c.ProcessSource.GpuAvailable = false` then
  `ApplyProcessTicks`), `proc-long-names` (`ApplyProcessTicks`; the dense scenario supplies the names),
  `proc-unavailable` (`c.ProcessSource.ThrowOnSample = "Access is denied (simulated)"` then `ApplyProcessTicks`),
  `proc-off` (`c.Peaks.Processes.SetEnabled(false)`). `ApplyProcessTicks(c)` = for each fixture tick
  `c.Peaks.Processes.Apply(c.ProcessSampler.SampleOnce())` — the real tracker/grouping/VM path, synchronously.
- `SubstateCatalog.cs`: `["processes"] = { "proc-sampling", "proc-populated", "proc-sorted-memory", "proc-sorted-gpu", "proc-no-gpu", "proc-long-names", "proc-unavailable", "proc-off" }`.
- `Views/CaptureHost.cs`: `BuildWindow` adds `"processes" => new PeaksWindow { DataContext = c.Peaks }`;
  `SelectAlertsTab(pw)` becomes `SelectPeaksTab(Window window, int index)` called with 1 for `alerts` and 2 for
  `processes` (guard `index < tabs.Items.Count`). `Views/Sidecar.cs` `AllSimulated` gains
  `new SimulatedService("IProcessSource", "FakeProcessSource — fixture process rows; no Process.GetProcesses(), no GPU Engine counters, sampler loop never started")`.
- `captures/baseline.json` — 11 new entries, all `"Method": "rtb"`, outputs under `artifacts/process-monitor/`:
  `normal/processes/proc-populated` Dark Amber 640×480 (`v14-processes-populated-dark-amber.png`) and Light
  (`…-light.png`); `proc-sorted-memory`, `proc-sorted-gpu`, `proc-no-gpu`, `proc-sampling`, `proc-unavailable`,
  `proc-off` on `normal` Dark Amber 640×480; `dense/processes/proc-long-names` Dark Amber 640×480;
  `normal/processes/proc-populated` Dark Amber **480×240** (`v14-processes-populated-narrow.png`);
  `normal/settings/category-monitoring` Dark Amber 1180×900 (`v14-settings-processes.png`).
- Nothing time-based is in flight: the tab has no animations, and the sampler loop never runs in the harness, so
  every capture is at rest.
- Isolation: `tests/Stats.UiPreview.Tests/IsolationMetadataTests.cs` `Forbidden` gains
  `("Stats.Core.Processes", "SystemProcessSource")` and `("Stats.Core.Processes", "GpuEngineCounters")`;
  `CompositionIsolationTests.Build_UsesFakeServices_NotProductionOnes` asserts `Assert.IsType<FakeProcessSource>(c.ProcessSource)`
  and `Assert.False(c.ProcessSampler.IsRunning)`; `AllowedCommandVerbs` gains `"process.sample"`.

**What the harness cannot show** (owner checklist): live sampling and the 2 s cadence, the first-sample warm-up
("—" → values), real GPU Engine counters on the owner's driver, access-denied skipping on real protected
processes, sampler pause/resume on window hide/show, idle CPU cost, exit with the window open, header-button
click/keyboard feel, and the clipboard.

## Non-goals

No dashboard tile or tray display for processes; no killing or reprioritising; no per-process FPS; no
per-process history/graphs; no per-process alerts/thresholds; no persisted sort; no user-tunable interval or
top-N; no Name-column sort; no per-tab gating; no rename of the Peaks window/tray labels; no changes to
`FanController`, `SensorPoller`, `RunCoalescedRefresh`, `FatalCleanup`, `Controls.xaml`, or the existing Peaks/
Alerts tabs; no new NuGet packages.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings (xUnit analyzers: constants in the
  `expected` slot).

### Tests — Core (`tests/Stats.Core.Tests`)

- `ProcessCpuTrackerTests`: `Apply_FirstSight_CpuIsNull`; `Apply_SecondSample_ComputesPercentOfAllCores`
  (16 cores, 2 s, Δ 3.2 s → 10 %); `Apply_DeltaClampedToHundred`; `Apply_ProcessorTimeWentBackwards_TreatsAsNewProcess`
  (null); `Apply_ExitedPid_IsForgotten_ReappearingPidStartsFresh`; `Apply_ZeroOrNegativeElapsed_CpuIsNull`;
  `Reset_ClearsHistory_NextApplyIsFirstSight`; `Ctor_RejectsNonPositiveProcessorCount`.
- `ProcessGroupingTests`: `Group_AggregatesByNameIgnoringCase_WithPidCountAndFirstSpelling`;
  `Group_CpuIsNullOnlyWhenEveryPidIsNull_ElseSumsKnown`; `Group_GpuIsNullWhenNoGpuData`;
  `Group_GpuSumsAcrossPids_MissingPidsCountAsZero`; `Group_SumsWorkingSetAndPrivateBytes`;
  `Group_ClampsCpuAndGpuSumsToHundred`.
- `ProcessSamplerTests` (nested `FakeSource : IProcessSource` with a snapshot queue, `ThrowOnSample`,
  `SampleCount`, `ResetCount`, `Disposed`): `SampleOnce_RaisesGroupedSnapshot_WithGpuAvailability`;
  `SampleOnce_SourceThrows_RaisesUnavailableWithFirstLineReason`; `SampleOnce_AfterFailureThenSuccess_RecoversToRows`;
  `OneSubscriberThrowing_OthersStillInvoked`; `StartStop_SamplesRepeatedly_ThenStops` (30 ms interval, the
  `SensorPollerTests` timing idiom); `Stop_ReturnsTrueWhenLoopJoined`; `Pause_ParksLoop_NoSamplesWhileParked`;
  `Resume_ResetsSourceAndTracker_FirstSnapshotAfterResumeHasNullCpu`; `Resume_WhileActive_IsNoOp_DoesNotReset`;
  `Resume_OnNeverStartedSampler_Starts`; `IntervalFor_FloorsAtTwoSeconds` (Theory 0.5→2, 1→2, 2→2, 3.5→3.5, 5→5);
  `Dispose_DisposesSource_WhenJoined`; `IsRunning_IsActive_ReflectStartPauseResumeStop`.
- `SlowSampleGateTests`: `Record_ThreeConsecutiveSlow_Trips`; `Record_FastResetsTheStreak`;
  `Record_OnceTripped_StaysTripped`; `Record_ExactlyThreshold_IsNotSlow`.
- `GpuEngineCountersTests`: `TryParsePid_ParsesLeadingPid` (the documented instance string → 1234);
  `TryParsePid_RejectsMalformed` (Theory: "", "pid_", "pid_x_luid", "luid_0x1"); `TryRead_TwiceNeverThrows_AndIsAvailableIsConsistentWithResult`
  (real machine: after two reads, `IsAvailable` ⇒ second result non-null with every value in 0..100; else null and
  `UnavailableReason` non-empty).
- `SystemProcessSourceTests` (real, like `PerfCounterSensorReaderTests`): `Sample_IncludesCurrentProcess_WithNonNegativeValues`;
  `Sample_ExcludesIdleAndSystem`; `Sample_PidsAreUnique`; `Sample_TimestampIsRecentUtc`; `Sample_Twice_DoesNotThrow`.
- `ProcessFormatTests`: `Percent_OneDecimalWithSpaceAndSign_DashWhenNull`; `Bytes_MbBelowOneGib_GbAbove`
  (845 MB, 1023 MB, 1.0 GB, 3.9 GB); `Megabytes_TsvUsesInvariantDot`; `PercentTsv_EmptyWhenNull`.
- `ProcessListViewModelTests` (a `Snap(params ProcessGroup[])` helper): `Apply_ShowsTopTwelveByCpu_Descending_TieBreakByName`;
  `Apply_UpdatesRowsInPlace_KeepsInstancesAndMovesReordered` (same `ProcessRowViewModel` reference before/after,
  index changed); `Apply_RemovesRowsThatDropOut`; `SortColumn_Memory_RecutsAndReordersFromLatest_WithoutNewSnapshot`;
  `SortColumn_Gpu_SortsByGpu`; `Apply_GpuUnavailable_HidesColumn_AndFallsBackFromGpuSortToCpu`;
  `SortBy_Gpu_IgnoredWhileGpuUnavailable`; `Row_NameShowsPidCountSuffixOnlyWhenMoreThanOne`;
  `Row_Texts_FormatPercentAndBytes_DashWhenNull`; `Row_MemoryToolTip_ShowsWorkingSetAndPrivate`;
  `Apply_Unavailable_ClearsRows_SetsStatusWithReason_IsUnavailable`; `Apply_AfterUnavailable_Recovers`;
  `StatusText_BeforeFirstSample_IsSamplingText`; `SetEnabled_False_ClearsRows_ShowsOffText_AndIgnoresApply`;
  `SetEnabled_True_RestoresSamplingText`; `SummaryText_ShowsShownOfTotalAndLocalTime` (expected built with
  `ToLocalTime()` like `PeaksViewModelTests`); `ToTsv_HeaderAndRows_InvariantCulture_GpuEmptyWhenUnavailable`;
  `ToTsv_NoRows_HeaderOnly`; `CopyError_SetNonEmpty_SetsHasCopyErrorTrue`; `SortByCommand_SetsSortColumn_AndIsSortedByFlags`.
- `PeaksViewModelTests`: `Processes_DefaultsToFreshInstance_WhenNotInjected`; `Processes_UsesInjectedInstance`.
- `SettingsServiceTests`: `Load_MissingFile_ProcessSamplingEnabledDefaultsToTrue`;
  `SaveThenLoad_ProcessSamplingEnabled_RoundTrips` (false survives); `Load_PreV110File_ProcessSamplingEnabledDefaultsToTrue`
  (the `Load_V1File_GetsDefaultsForNewFields` JSON literal).
- `SettingsViewModelTests`: `Ctor_LoadsProcessSamplingEnabled`; `ProcessSamplingEnabled_WritesThroughAndRaisesProcesses`
  (`Assert.Equal(new[] { SettingsChange.Processes }, changes)`, one save); `SettingsChange_ProcessesIsTheLastMember`
  (`Assert.Equal(SettingsChange.Processes, Enum.GetValues<SettingsChange>().Last())`).

### Tests — harness (`tests/Stats.UiPreview.Tests`)

- `ProcessSubstateTests` (new; `GraphEffectsSubstateTests` scaffold): `SubstateCatalog_AcceptsProcSubstates_ForProcessesView`
  (Theory over all eight); `SubstateCatalog_RejectsProcSubstates_ForPeaksAndAlerts`;
  `ProcPopulated_ShowsTwelveRows_SortedByCpuDescending_GpuColumnVisible`; `ProcPopulated_FirstRowIsChromeTimesFourteen_WithExactTexts`
  ("chrome ×14", "18.4 %", "6.2 %", "3.9 GB"); `ProcPopulated_SummaryCountsFourteenGroups`;
  `ProcSortedMemory_TopRowIsChrome_AndOrderIsByWorkingSet`; `ProcSortedGpu_TopRowIsChrome_SecondIsDwm`;
  `ProcNoGpu_HidesGpuColumn_AndSortStaysCpu`; `ProcLongNames_DenseHasARowNameOverSixtyCharacters`;
  `ProcSampling_HasNoRows_AndSamplingStatus`; `ProcUnavailable_SetsUnavailableStatusWithSimulatedReason`;
  `ProcOff_ShowsOffText_AndNoRows`; `Build_NeverStartsTheSampler_ForAnySubstate`;
  `FixtureProcessTicks_HaveUniquePids_NoIdleOrSystem_TwoTicksTwoSecondsApart` (normal, dense);
  `EveryProcSubstate_RecordsOnlyProcessSampleCommands` (command log verbs ⊆ allowed).
- `CompositionIsolationTests`: `Build_UsesFakeServices_NotProductionOnes` gains the `FakeProcessSource`/`IsRunning`
  assertions; `AllowedCommandVerbs` gains `process.sample`. `IsolationMetadataTests.Forbidden` gains the two real
  types (so a harness that ever references `SystemProcessSource`/`GpuEngineCounters` fails the build's tests).

### Captures

The 11 `baseline.json` entries above, run with `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`
(or the single-entry commands the sidecars record), inspected and listed in `docs/process-monitor/EVIDENCE.md`:
populated Dark Amber and Light (12 rows, GPU column, accent arrow on CPU %, summary line), sorted-memory and
sorted-gpu (arrow moved, order changed), no-gpu (three columns, header widths unchanged), long-names (ellipsis
in the Process column, numeric columns intact), sampling/unavailable/off (each empty-state message, no header
Grid overlap), narrow 480×240 (horizontal scrollbar, header buttons intact), and the Settings Monitoring tab
showing the new checkbox and caption. Existing `v10-peaks-*`/`v10-alerts-*` captures must be pixel-identical to
before (the tabs are untouched).

### Owner checklist (live app, Start-menu launch — rule 7/8)

1. Open Peaks → Processes: rows with memory appear immediately; CPU % / GPU % read "—" and fill in ~2 s later;
   the summary line updates every 2 s.
2. Numbers are plausible against Task Manager → Details (CPU %) and Performance → GPU (GPU %) on the owner's
   NVIDIA driver; the GPU column is present at all (if not, the log's `[Stats.ProcessSampler]`/reason text in the
   tab tells why).
3. Close Peaks (hide) → in Task Manager Stats' own CPU falls back to the pre-feature idle (~1 %); reopen →
   sampling resumes with a fresh "—" for one tick, then live values (no average over the hidden period).
4. Settings → Monitoring → Processes off: the tab shows the off message and nothing samples; on: resumes.
5. Poll interval 5 s → the summary "updated" time steps every 5 s; 0.5 s → still every 2 s.
6. Click GPU %, Memory, CPU % headers: accent arrow moves, rows reorder without flicker, scroll position holds;
   Tab reaches the three header buttons (accent focus border), Enter/Space sorts.
7. Copy → paste into a spreadsheet: 6 columns, 12 data rows, dots as decimal separators.
8. `%LOCALAPPDATA%\Stats\logs`: no per-process spam from protected processes (csrss, MsMpEng…); at most one
   `[Stats.ProcessSampler]` line per failure episode/recovery.
9. Window at 480×240: horizontal scrollbar appears, header buttons still clickable, no clipped Copy button.
10. Exit (tray → Exit) with Peaks open: prompt clean exit (sampler joins within 2 s), fans restored as before.
11. Light theme: header buttons and the accent arrow legible; long names ellipsise with the tooltip showing the
    full name and pid count.
