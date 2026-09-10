# T1 report — preview harness and baseline capture

Owner: Claude Sonnet implementer (Agent tool). Branch `ui-polish`, built on top of T0's `b8b9c44`. Status: implementation complete,
runtime-validated on this Windows machine (build/tests/capture all actually executed, not simulated).

## 1. Files added / changed

**New — `tools/Stats.UiPreview/` (preview executable, WPF, net8.0-windows, own `asInvoker` manifest by omission):**
- `Stats.UiPreview.csproj`, `GlobalUsings.cs`, `Program.cs` (CLI entry point: single capture / `--batch` / `--interactive`)
- `PreviewApp.cs` (the preview's own `Application` subclass; merges Theme/Controls/TileTemplates/AppStyles by pack URI)
- `CaptureSpec.cs`, `SubstateCatalog.cs`, `GitInfo.cs`, `Native.cs` (DWM frame-bounds + cursor + compositor-flush P/Invoke), `DispatcherUtil.cs`, `BindingErrorListener.cs`
- `PreviewComposition.cs` (pure, non-UI: builds real Core view models over fake services; owns every substate that doesn't need a visual tree)
- `Fixtures/`: `ScenarioFixture.cs`, `Scenarios.cs` (8 scenarios: normal, dense, thresholds, missing, empty, fans, settings, gallery), `TimeSeries.cs` (fixed time/seed/step + `SnapshotBuilder`), `FakeSensorReader.cs`, `FakeFanControlBackend.cs`, `CommandLog.cs`
- `Views/`: `CaptureHost.cs` (window construction + visual-tree substates + capture orchestration), `Screenshot.cs` (screen/rtb capture), `Sidecar.cs`, `VisualTreeUtil.cs`
- `captures/baseline.json` (55-entry batch manifest for the before gallery)

**New — `tests/Stats.UiPreview.Tests/`:**
- `Stats.UiPreview.Tests.csproj`, `IsolationMetadataTests.cs`, `CompositionIsolationTests.cs`, `FixtureValidityTests.cs`, `FanCommandRecordingTests.cs`

**Changed (parent-approved, per the work order):**
- `src/Stats.App/Stats.App.csproj` — added `<InternalsVisibleTo Include="Stats.UiPreview" />` and `Stats.UiPreview.Tests` so the preview can reach internal `x:Name` fields (`PickerFlyout`, `WarnBox`, `CritBox`, `ErrorText`, `LowerIsWorseCheck`, `PromptText`, `Input`, etc.) and `DarkTitleBar`.
- `Stats.sln` — added both projects under new `tools`/`tests` solution folders.
- `.gitignore` — added `artifacts/`.
- Deleted the stray empty `tools/Stats.UiPreview/Artifacts/` folder before creating the project there.

No other production file was touched. `App.xaml.cs`/`App.xaml` are untouched and never constructed.

## 2. Real vs mocked, per view

All 10 views are the actual `Stats.App` `Window`/`Dialog` classes and actual `Stats.Core.ViewModels` types — nothing
is re-implemented. What's swapped for fakes is listed once here since it's identical for every view:

| Real (production) | Fake / never touched |
| --- | --- |
| `DashboardWindow`, `FansWindow`, `PeaksWindow`, `MetricDetailWindow`, `OverlayWindow`, `ThresholdDialog`, `InputDialog` (actual XAML+code-behind) | `Stats.App.App` — never constructed; `App.xaml` startup URI never loaded |
| `DashboardViewModel`, `SettingsViewModel`, `OverlayViewModel`, `PeaksViewModel`, `AlertLogViewModel`, `FansViewModel`, `MetricDetailViewModel`, `CoreMatrixViewModel` (real, unmodified) | `ISensorReader` → `FakeSensorReader` (Discover returns fixture defs; no LHM/PawnIO/perf-counter) |
| `MetricStore`/`MetricHistory` fed via `store.Apply(SensorSnapshot)` with fixture ticks | `FrameRateReader`/PresentMon — never referenced |
| `FanController` (real, unmodified — same class production uses) | `IFanControlBackend` → `FakeFanControlBackend` (records `SetPercent`/`SetAuto` to `CommandLog`, can inject a failing channel) |
| `ThemeManager.Apply` (real; same brush-replacement mechanism production uses) | `IFanArmedMarker` → `NullFanArmedMarker` (never `FileFanArmedMarker`, no disk marker) |
| `ThresholdEvaluator`/`ThresholdIndex`/`ThresholdDefaults`/`ValueFormatter`/`HistoryCapacity` (real) | `StartupTaskService`/`UpdateService`/`GlobalHotkey`/`RollingTraceLog`/`H.NotifyIcon` tray — never constructed |
| `SettingsService` pointed at a fresh `%TEMP%\Stats.UiPreview\<run-id>\...` root (real class, fake path) | `SettingsViewModel.OpenLogFolderRequested`/`StartupToggleRequested`/`CheckForUpdatesRequested`/`RestartRequested`/`OverlayPositionResetRequested` handled in-process by recording lambdas in `PreviewComposition.Build` — no shell, schtasks, or network call |
| `DarkTitleBar.Apply` (real DWM call — harmless, applies dark title bar) | `ConflictingFanSoftware.Match(...)` used with a hand-picked fixture process-name list; `RunningProcessNames()` (the real process-table scan) is never called — asserted by test |

Isolation is proven at the assembly-metadata level (`IsolationMetadataTests`), not just by source review: the built
`Stats.UiPreview.dll`'s `TypeReference`/`MemberReference` tables are scanned and asserted to contain none of
`Stats.App.App`, `LhmSensorReader`, `PerfCounterSensorReader`, `CompositeSensorReader`, `FrameRateReader`,
`PresentMonProcess`, `PresentMonLocator`, `StartupTaskService`, `UpdateService`, `GlobalHotkey`, `RollingTraceLog`,
`FileFanArmedMarker`, `H.NotifyIcon.TaskbarIcon`, and separately that `ConflictingFanSoftware.RunningProcessNames`
is never a call target anywhere in the assembly.

## 3. Exact commands

Single capture (matches PREVIEW_HARNESS.md's example exactly):
```
dotnet run --project tools/Stats.UiPreview -- --scenario normal --view dashboard --theme "Dark Amber" --width 1180 --height 720 --ui-scale 1.0 --output artifacts/ui-polish/before/normal-dashboard.png
```

Batch (the actual baseline gallery deliverable):
```
dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json
```

Interactive (fake reader ticking seeded data every second; no capture, keeps window(s) open until closed):
```
dotnet run --project tools/Stats.UiPreview -- --interactive --scenario normal --view dashboard --theme "Dark Amber" --width 900 --height 600
```
Verified: started, applied theme, ticked for 8s with no exception, terminated externally (it blocks in
`Application.Run` until the window closes — that is the intended behavior, not a hang).

Tests:
```
dotnet test tests/Stats.UiPreview.Tests --nologo
```

Full repo gates (both run from the repo root, exactly as CLAUDE.md specifies):
```
dotnet build --nologo
dotnet test --nologo
```

All commands above were actually executed on this machine, not simulated.

## 4. Baseline gallery — captured PNGs

`dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json` run from the
repo root produced **55 PNG + 55 JSON sidecars** under `artifacts/ui-polish/before/` (not committed — evidence
attached separately per the work order; `artifacts/` is git-ignored). **Zero sidecars recorded any binding/resource
warning** (`Warnings: []` in all 55). Full per-file scenario/substate/theme/size table:

| File | Scenario | Substate | Theme | Logical size | UI scale |
| --- | --- | --- | --- | --- | --- |
| v1-dashboard-normal | normal | — | Dark Amber | 1180×720 | 1.0 |
| v2-dashboard-dense | dense | — | Dark Amber | 1180×720 | 1.0 |
| v3-dashboard-narrow-scale13 | normal | — | Dark Amber | 860×600 | 1.3 |
| v3-picker-narrow-scale13 | normal | — | Dark Amber | 860×600 | 1.3 |
| v3-settings-narrow-scale13 | normal | — | Dark Amber | 860×600 | 1.3 |
| v5-dashboard-dense-scale09 | dense | — | Dark Amber | 1180×720 | 0.9 |
| v6-theme-cycle-0..5 (6 files) | normal | theme-cycle | Dark Amber→Blue→Green→Purple→Light→Amber+custom accent | 1180×720 | 1.0 |
| v6-tile-context-menu | normal | tile-menu | Dark Amber | 1180×720 | 1.0 |
| v6-theme-dropdown | normal | theme-dropdown | Dark Amber | 1180×900 | 1.0 |
| v7-tile-gallery-dark-amber / -light | gallery | — | Dark Amber / Light | 1180×1000 | 1.0 |
| v8-dashboard-missing | missing | — | Dark Amber | 1180×720 | 1.0 |
| v8-dashboard-empty | empty | — | Dark Amber | 1180×720 | 1.0 |
| v8-dashboard-collapsed-all | normal | collapsed-all | Dark Amber | 1180×720 | 1.0 |
| v9-fans-off / -on / -manual / -curve / -pump / -modified / -no-channels / -conflict / -recovery / -write-failed | fans | off / on / on+manual / on+curve / on+pump / on+modified / no-channels / on+conflict / recovery / on+write-failed | Dark Amber | 760×640 | 1.0 |
| v9-fans-on-narrow | fans | on | Dark Amber | 560×360 | 1.0 |
| v9-fans-on-light | fans | on | Light | 760×640 | 1.0 |
| v10-peaks-populated / -empty / -long-names | normal/empty/dense | populated/empty/long-names | Dark Amber | 640×480 | 1.0 |
| v10-peaks-populated-narrow | normal | populated | Dark Amber | 480×240 | 1.0 |
| v10-alerts-empty / -ongoing | normal | empty/ongoing | Dark Amber | 640×480 | 1.0 |
| v11-details-gap | missing | gap | Dark Amber | 640×420 | 1.0 |
| v11-details-long-unit | normal | long-unit | Dark Amber | 640×420 | 1.0 |
| v11-details-thresholds-dark / -light | thresholds | thresholds | Dark Amber / Light | 640×420 | 1.0 |
| v11-threshold-dialog-valid-dark / -invalid / -lower-is-worse / -valid-light | normal | valid/invalid/lower-is-worse/valid | Dark Amber ×3, Light ×1 | 380×~200-260 | 1.0 |
| v11-input-dialog | normal | — | Dark Amber | 380×160 | 1.0 |
| v11-settings-invalid-threshold / -invalid-hotkey / -invalid-limit | normal | invalid-threshold/invalid-hotkey/invalid-limit | Dark Amber | 1180×900 | 1.0 |
| v12-overlay-move-mode / -light-parent / -opacity-min / -opacity-max / -long-value / -vertical | normal | move-mode/light-parent/opacity-min/opacity-max/long-value/vertical | Dark Amber (Light for light-parent) | 400×200 (200×400 vertical) | 1.0 |

**V4 (150% actual Windows DPI) was skipped, as instructed** — this machine's Windows DPI is 96 (100%) on both
monitors (confirmed via `VisualTreeHelper.GetDpi`, recorded in every sidecar as `DpiX`/`DpiY`: 96); there is no
150%-scaled display available to test against, and PREVIEW_HARNESS.md explicitly forbids faking a DPI pass by
scaling image dimensions. This remains a **blocking visual gate, pending Windows validation on a 150%-DPI display**.

## 5. V1 / V2 fully-visible medium tile counts (1180×720, Dark Amber)

- **V1 (`normal`)**: **8** fully visible Medium tiles — CPU (Tctl/Tdie, CPU Total, CPU Clock, Package Power = 4) +
  GPU (GPU Core, GPU Load, GPU Power = 3) + Memory (1). The Storage tile ("SSD 1TB · Active Time") sits right at
  the bottom edge and is clipped/only mostly visible; Network and the Game group are entirely below the fold.
- **V2 (`dense`)**: the dense fixture deliberately cycles every tile through S/M/L/kind combinations (per
  PREVIEW_HARNESS.md's "40 mixed tiles, all S/M/L and kinds") rather than being all-Medium, so a raw "visible
  Medium tile" count is a narrower fixture-specific number: of the 8 Medium-sized tiles the dense fixture defines,
  **3** are fully visible within 1180×720 (`Package Power (PPT)`, `GPU Core`, `GPU Memory Controller Load`); a
  fourth (`GPU Fan 1`) is clipped at the bottom edge. The 16-core matrix itself (not counted as "tiles") is fully
  visible at the top. Counting methodology: visual inspection of `v1-dashboard-normal.png` /
  `v2-dashboard-dense.png` against the known per-tile target dimensions (S 160×80, M 224×144, L 460×192 per
  DESIGN.md §2) — no automated pixel-boundary tool was built for this, so treat these as a solid baseline
  reference, not a machine-verified count.

## 6. Binding/resource errors

**None observed.** `PresentationTraceSources.DataBindingSource`/`ResourceDictionarySource` were wired to a
`TraceListener` (`SourceLevels.Error`) for every one of the 55 baseline captures plus every ad-hoc capture run
during development (theme-dropdown/tile-menu debugging, all 8 scenarios, all 10 views, every listed substate) —
every sidecar's `Warnings` array is empty. Since T1 makes no styling changes, this is expected and establishes the
*current* (pre-T2) baseline is clean; T2+ should watch for this list becoming non-empty as a regression signal.

## 7. Limitations

- **Popup/DWM-composited capture timing**: `--method screen` needed `DwmFlush()` (twice, with a 120 ms real sleep
  between) added to `Screenshot.CaptureScreen` — without it, a freshly-shown window's first `CopyFromScreen` can
  capture a stale (blank/white) compositor frame even though WPF's own Visual tree already rendered correctly (the
  `--method rtb` capture of the same window was correct immediately). This is now handled inside the harness, not
  a remaining limitation, but is worth knowing if a future capture ever looks blank.
- **`ContextMenu`/`ComboBox` popups can render outside the captured window rect.** `--method screen` crops to the
  main window's own DWM extended-frame-bounds rect; PREVIEW_HARNESS.md's own capture-behavior section already
  flags this ("a RenderTargetBitmap... does not include every popup HWND" / "verify popup surfaces independently
  when the capture method excludes their windows"). Two concrete things the harness does about it: (1) a
  `ContextMenu`'s default `Placement` is `MousePoint`, not relative to `PlacementTarget` — the harness moves the
  real OS cursor onto the target tile first (`Native.MoveCursorTo`) so the resulting menu opens inside the window;
  (2) the theme-preset `ComboBox` sits inside a `ScrollViewer`, so the harness scrolls it into view with headroom
  before opening the dropdown. Even so, a popup opened very close to a short window's edge can still legitimately
  extend past that window's screen rect (exactly as it would for a real user) — `v6-theme-dropdown.png` uses a
  taller-than-default window (900 instead of 720) specifically to give the dropdown room; a caller requesting a
  popup substate on an unusually short window should expect the same real-world clipping.
- **V4 (150% DPI)**: not run — no such display on this machine (see §4).
- **Tile-gallery "kinds"**: `TileKind` currently has 4 concrete rendered kinds (`Sparkline`, `Gauge`, `Bar`,
  `Value`; `Auto` is a resolution mode, not a fifth visual kind) — DESIGN.md §4's table additionally lists a
  "Compact" kind that does not exist in the current (pre-T2/T3) codebase. The `gallery` scenario captures every
  *current* kind × S/M/L (12 tiles) plus long-label/unavailable/limit/inverted-threshold edge cases (4 tiles) = 16
  tiles total, which is everything T1's job (capturing the *current* runtime UI) can capture; "Compact" will need
  its own gallery entry once T3 introduces it.
- **Interactive mode** is lightly exercised (started, ran 8s, no exception) rather than watched end-to-end for a
  full minute — it is explicitly a secondary/optional feature per the work order ("Realtime preview playback can
  exist separately") and does not affect any capture.
- **`git diff`-based dirty-diff identity** only reflects changes to already-tracked files (git's own semantics);
  new untracked files (like this entire branch's work) are not included in that hash. This matches a literal
  reading of PREVIEW_HARNESS.md's "sha256 of `git diff` output" instruction; flagging it here in case the parent
  wants tracked+untracked coverage for a future harness version.
- **Fan `conflict` substate** sets `FansViewModel.ConflictText` directly to the same string
  `FansViewModel.Refresh()` would have derived from a matching process name, rather than driving the real
  `Func<IEnumerable<string>>` the view model was constructed with (that Func is fixed at construction time in
  `PreviewComposition.Build` and always returns empty, matching "no real process-table scan" isolation) — the
  displayed text is byte-for-byte what production would show, just set through a public property instead of the
  Refresh() code path, since a constructor-injected delegate can't be swapped after the fact.

## 8. Open questions for the parent

1. **V1/V2 tile counts above are manual/visual, not tool-measured.** If T2/T3 want a precise, repeatable
   before/after tile-count diff (per DESIGN.md §8's "explain a reduction over 20%"), a small pixel/bounds-based
   counter could be added to the harness — flagging this now rather than building it speculatively.
2. **`--method screen` DPI**: this machine is 96 DPI everywhere, so `DpiX`/`DpiY` in every sidecar read 96 — the
   harness reports whatever `VisualTreeHelper.GetDpi` returns, so it will read correctly on a scaled display, but
   that path is unverified since no such display exists here (see V4 above).
3. Confirm the **fan-substate combination naming** (`on+curve`, `on+write-failed`, etc., `+`-joined per
   PREVIEW_HARNESS.md) reads clearly for T6's use, since T6 owns `FansWindow.xaml` polish and will likely want to
   add narrower substates of its own (e.g. an explicit "modified+curve" combo) — the substate catalog
   (`SubstateCatalog.cs`) is a simple allow-list per view and easy to extend.
4. Whether the parent wants the baseline gallery **re-run and diffed automatically** at T7/T8 checkpoints, or
   whether manual re-invocation of the same `--batch` command per checkpoint is sufficient (it reproduces
   byte-identical fixture data every time; only the *rendered pixels* would change if T2+ style changes land).
