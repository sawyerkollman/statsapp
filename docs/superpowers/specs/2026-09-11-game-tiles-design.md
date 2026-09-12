# Game-group tile kinds: frame-time histogram and FPS summary — design

Date: 2026-09-11. Base: `feature/v1.10` @ `4323b0b` (master v1.9.2 + dashboard layout modes + graph effects — see
`docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md` and
`docs/superpowers/specs/2026-09-11-graph-effects-design.md`; this feature builds on both). Branch `feature/game-tiles`,
PR to `feature/v1.10`.

Owner ask, paraphrased: two new tile kinds aimed at the Game group (PresentMon FPS / 1% low / frame time) — a
**Histogram** tile (distribution of a metric's samples over the history window, auto-ranged bins, p99 marker;
generic over any metric, offered by default for frame time) and an **FpsSummary** tile (average FPS large, 1% low
and frame time small, a compact bar showing the 1%-low/average ratio; bound to the FPS metric, pulling its
sibling Game metrics from the store).

## Goal

| Kind | What it shows | Where it comes from |
| --- | --- | --- |
| **Histogram** | 12 equal-width bars over the finite samples in the tile's history buffer (`min..max` of the samples, NaN gaps excluded), a thin vertical percentile marker with a text label ("p99 17.6 ms"), the `min`/`max` range labels under the bars, and the usual name / window tag / current value / severity glyph chrome. | The same `MetricHistory` buffer the sparkline draws (`MetricTileViewModel.HistoryValues`) — so for `fps.frametime` it is a distribution of **per-poll averages** (one sample per tick, see `FrameStatsAggregator.Snapshot`), *not* of individual frames. Labels never imply per-frame precision. |
| **FpsSummary** | Average FPS at the standard reading size, a small row "1% low 92 fps · 6.9 ms", and a compact `LevelBar` whose fill is `1% low ÷ average` (0..1) with a "64% of avg" caption. Missing siblings read "—". | The bound metric's history plus the two sibling Game-group histories resolved from the `MetricStore` by group + unit + role (never by hard-coded id). |

Both render with the existing tile chrome (`TileBorder`, `TileName`, `TileValue`, `TileUnit`, `TileFoot`,
`TileSeverityGlyph`, `TileMenuButtonStyle`) at M and L; S falls back to the existing `TileCompact` (name + value)
exactly as every other kind does. Everything the user could already do stays byte-identical: `Auto` still resolves
to Sparkline/Gauge exactly as today, no existing template, control, setting, or menu string changes.

## Owner decisions

Already made — recorded here, not re-litigated:

1. **`TileKind` gains `Histogram` and `FpsSummary`, appended after `Value`** (rule 2 style: never reorder,
   serialized by name via `SettingsService.Options`' global `JsonStringEnumConverter`).
   *Verification of "unknown stored kinds already fall back":* only half true today. `TileTemplateSelector.SelectTemplate`
   (`src/Stats.App/Views/TileTemplateSelector.cs`) has `_ => Sparkline`, so an unhandled *in-memory* kind renders as
   a sparkline. But an unknown *stored* string (`"Kind": "Histogram"` read by a build that lacks the member) makes
   the global `JsonStringEnumConverter` throw inside `JsonSerializer.Deserialize<AppSettings>`, and
   `SettingsService.Load` (`src/Stats.Core/Settings/SettingsService.cs` L39–46) catches that by replacing the
   **entire** settings object with defaults. The Settings section below closes the forward-compat half with a
   lenient converter; the backward (1.9.x) half cannot be closed retroactively and is documented as a hazard.
2. **Histogram maths live in `Stats.Core`** — a static, tested `HistogramBinning` helper working on the same buffer
   the sparkline uses; NaN gaps excluded; **12 bins**; the **p99 marker is a thin vertical line with a label**.
3. **FpsSummary resolves siblings by `MetricGroup.Game` membership and unit/role via `MetricDefinition`**, never by
   `FrameMetrics.FpsId/LowId/FrameTimeId`; a missing sibling's slot reads **"—"**.
4. **Both kinds use the existing tile chrome, honour `GraphStyle` for glow/effects, and exist at all three sizes**
   with a sensible degradation at S.

### Owner decisions assumed

Decided here because the owner did not; each is a one-line change to revert.

- **A. `Auto` is unchanged.** `MetricTileViewModel.ResolveKind(TileKind.Auto, …)` keeps returning Sparkline for
  `fps.frametime` and `fps.avg`. "Offered by default for frame time" is delivered by the kind menu (Histogram is
  offered on every tile), the `game` harness scenario, and the README — not by silently re-templating existing
  users' tiles on upgrade, which would break the "existing paths byte-identical" bar.
- **B. Menu gating.** "Histogram" is offered on every metric. "FPS summary" is offered only when the tile's
  definition has the `Fps` role (below). A pref that says `FpsSummary` on any other metric (hand-edited file, a
  future metric whose role changed) resolves to `Sparkline` in `ResolveKind`; the pref itself is left untouched.
- **C. Role discrimination.** Within `MetricGroup.Game`: unit `"ms"` → frame time; unit `"fps"` whose
  `DisplayName` contains `"1%"` (ordinal) → 1% low; any other `"fps"` → average FPS. `DisplayName` is the only
  `MetricDefinition` field other than `Id` that separates `fps.avg` from `fps.low1`; a test pins it to
  `FrameMetrics.Definitions`. User renames go to `TilePref.Name`, never to `DisplayName`, so they cannot break it.
- **D. Marker percentile follows the metric's direction.** p99 for every metric whose governing threshold rule is
  not `LowerIsWorse` (frame time, temperatures, loads); **p1** (labelled "p1") when it is (`fps.avg`, `fps.low1`,
  `gallery.inverted`) — the interesting tail of an FPS distribution is the low end. Nearest-rank, the same formula
  `FrameStatsAggregator.Snapshot` uses for the 1% low. To revert to "always p99", delete the `LowerIsWorse` branch
  in `MetricTileViewModel.RefreshHistogram`.
- **E. Sibling plumbing = optional `MetricStore` constructor argument** on `MetricTileViewModel`
  (`store = null` keeps every existing call site source-compatible). Only `DashboardViewModel.RebuildSections`
  passes it; `OverlayViewModel.Rebuild` does not, so overlay tiles never compute histogram/summary outputs and
  their per-tick cost is unchanged. Siblings are resolved once in the constructor.
- **F. Histogram template has no `MinMaxText` footer** (the bars *are* the distribution; session min/avg/max stay
  in Details), so the bars get ~45 px at M. FpsSummary has no history-window tag (it shows current values).
- **G. No new user settings** (no bin-count/range option; both base specs chose the same), no `SettingsChange`
  member, no composition-root change.
- **H. No animation in the new control** (bar heights and the marker jump to their new values). Glow/gradient
  effects are static, so `GraphStyle.Motion` is irrelevant and harness captures are always at rest.
- **I. No minimum-sample gate.** Bars draw from the first finite sample (one sample = one full-height bar), like
  the sparkline; an all-gap buffer draws only the baseline.
- **J. S size = `TileCompact`** for both kinds (no compact variants); the selector's `Size == S → Compact`
  short-circuit is untouched.
- **K. Ratio bar colour = the 1% low's own severity** (`fps.low1` has its own 30/15 `LowerIsWorse` override in
  `ThresholdDefaults.Overrides`), so the bar goes amber/red when stutter is bad even while the average is fine.

## Settings (rule 3: every field defaulted, sanitized, old files load)

- `TileKind` (`src/Stats.Core/Settings/TilePref.cs`) becomes `{ Auto, Sparkline, Gauge, Bar, Value, Histogram,
  FpsSummary }` — Histogram = 5, FpsSummary = 6, pinned by a test.
- **New `TileKindConverter : JsonConverter<TileKind>`** in `src/Stats.Core/Settings/TilePref.cs`, a copy of
  `DashboardLayoutModeConverter` (`src/Stats.Core/Settings/DashboardLayoutMode.cs`) with `TileKind`/`TileKind.Auto`
  substituted: an unrecognized string, number, null, or container deserializes to `Auto` (skipping a container) instead
  of throwing; `Write` emits the member name, identical to the global converter. Applied as
  `[JsonConverter(typeof(TileKindConverter))]` on `TilePref.Kind`. Existing files (`"Kind": "Gauge"`) round-trip
  unchanged; a future kind, or a hand-edited typo, no longer wipes the whole settings file on *this* build and later.
- No new `AppSettings` fields, nothing new in `SettingsService.Normalize`.
- **Downgrade hazard (cannot be fixed retroactively, must be in the release notes and owner checklist):** a
  v1.10 `settings.json` containing `"Kind": "Histogram"` or `"FpsSummary"` opened by an already-installed 1.9.x
  throws in that build's global `JsonStringEnumConverter`; its `SettingsService.Load` catch-all replaces **all**
  settings with defaults and the next save overwrites the file. `TileKindConverter` protects only builds that ship
  it. Mitigation: release note "back up `%AppData%\Stats\settings.json` before downgrading".

## Core (`Stats.Core`, WPF-free, testable)

### `src/Stats.Core/Metrics/HistogramBinning.cs` (new, static)

```csharp
public static class HistogramBinning
{
    public const int DefaultBinCount = 12;

    /// Clears `counts`, then counts every finite sample into counts.Length equal-width bins over [min, max]
    /// (min/max = finite min/max of `samples`). Returns the finite sample count. NaN samples are skipped.
    /// No finite sample → returns 0, min = max = NaN, counts all zero. Degenerate range (max − min < 1e-6f)
    /// → every finite sample lands in counts[counts.Length / 2], min and max both report the value.
    /// Otherwise bin = min(counts.Length − 1, (int)((v − min) / (max − min) * counts.Length)), so v == max
    /// falls in the last bin. counts.Length < 1 → ArgumentException.
    public static int Bin(ReadOnlySpan<float> samples, Span<int> counts, out float min, out float max);

    /// Nearest-rank percentile of the finite samples: copies them into scratch[0..n), sorts that slice
    /// ascending, returns scratch[clamp(ceil(p · n) − 1, 0, n − 1)] — the same rank rule as
    /// FrameStatsAggregator.Snapshot's 1% low. Returns NaN when n == 0. p outside [0, 1] or
    /// scratch.Length < samples.Length → ArgumentException. Allocation-free when the caller reuses scratch.
    public static float Percentile(ReadOnlySpan<float> samples, double p, Span<float> scratch);

    /// Position of value within [min, max] as 0..1 (clamped); 0.5 when the range is degenerate (max − min < 1e-6f);
    /// NaN when value is NaN.
    public static double Fraction(float value, float min, float max);
}
```

Modelled on `SampleAxis`/`CurveSmoothing`: no LINQ, no allocation, pure functions over spans.

### `src/Stats.Core/Frames/GameMetricRoles.cs` (new)

```csharp
public enum GameMetricRole { None, Fps, LowFps, FrameTime }

public static class GameMetricRoles
{
    /// def.Group != MetricGroup.Game → None; Unit == "ms" → FrameTime;
    /// Unit == "fps" → DisplayName.Contains("1%", StringComparison.Ordinal) ? LowFps : Fps; otherwise None.
    public static GameMetricRole RoleOf(MetricDefinition def);

    /// First definition (in enumeration order) whose RoleOf(...) == role, or null.
    public static MetricDefinition? Find(IEnumerable<MetricDefinition> definitions, GameMetricRole role);
}
```

Applied to `FrameMetrics.Definitions` (`src/Stats.Core/Frames/FrameMetrics.cs`: `("fps.avg","FPS",Game,"fps")`,
`("fps.low1","1% Low FPS",Game,"fps")`, `("fps.frametime","Frame Time",Game,"ms")`) this yields exactly one of
`Fps`, `LowFps`, `FrameTime` — pinned by a test so a rename of "1% Low FPS" fails loudly.

### `src/Stats.Core/ViewModels/MetricTileViewModel.cs`

- Constructor becomes `MetricTileViewModel(MetricDefinition definition, MetricHistory history, AppSettings settings,
  MetricStore? store = null)`. When `store` is non-null and `GameMetricRoles.RoleOf(definition) == Fps`, the
  constructor resolves `_lowDef = GameMetricRoles.Find(store.Definitions, LowFps)` and
  `_frameTimeDef = Find(…, FrameTime)` and their histories via `store.TryGet(def.Id, out …)` (each may stay null).
  `store == null` (overlay, existing tests) leaves every new output at its default forever.
- New read-only property `GameMetricRole GameRole { get; }` (= `RoleOf(Definition)`, computed once) — the tile
  menu uses it for gating.
- New observable outputs (all `[ObservableProperty]`, defaults shown):

  | Property | Type | Default | Meaning |
  | --- | --- | --- | --- |
  | `HistogramBins` | `int[]` | `Array.Empty<int>()` | 12 counts, lowest-value bin first; bound as `IReadOnlyList<int>`. |
  | `HistogramMinText` / `HistogramMaxText` | `string` | `""` | `ValueFormatter.Format(Definition, min/max)`; `""` when no finite sample. |
  | `HistogramMarkerFraction` | `double` | `double.NaN` | `HistogramBinning.Fraction(percentile, min, max)`; NaN = no marker. |
  | `HistogramMarkerText` | `string` | `""` | `"p99 17.6 ms"` / `"p1 58 fps"` (`$"p{N} {ValueFormatter.Format(Definition, value)}"`); `""` when no data. |
  | `FpsLowText` | `string` | `"—"` | `ValueFormatter.Format(_lowDef, _lowHistory.Current)` → e.g. `"92 fps"`; `"—"` when the sibling definition or history is missing (`Format` already returns "—" for a null/NaN current). |
  | `FpsFrameTimeText` | `string` | `"—"` | Same for the frame-time sibling → `"6.9 ms"`. |
  | `FpsLowSeverity` | `Severity` | `Normal` | `thresholds.Evaluate(_lowDef, low)` (or `ThresholdEvaluator.Evaluate(_lowDef, low, _settings)` when no index) — the 1% low's own rule; `Normal` when no sibling. |
  | `FpsLowRatio` | `float` | `0f` | `Math.Clamp(low / avg, 0f, 1f)` when both `avg` (bound `Current`) `> 0` and `low` are finite, else `0f`. |
  | `FpsRatioText` | `string` | `""` | `string.Create(InvariantCulture, $"{FpsLowRatio * 100:F0}% of avg")` under the same condition, else `""`. |

- `Refresh(ThresholdIndex? thresholds = null)`: after the existing `HistoryValues = NextHistoryBuffer()` line, add
  `if (_store is not null) { if (Kind == TileKind.Histogram) RefreshHistogram(thresholds); else if (Kind == TileKind.FpsSummary) RefreshFpsSummary(thresholds); }`.
  Nothing else in `Refresh` moves, so every other kind's outputs are computed in the same order with the same values.
  - `RefreshHistogram`: bins go into one of two preallocated `int[DefaultBinCount]` buffers alternated exactly like
    `NextHistoryBuffer` (so `HistogramBins` always changes reference, zero steady-state allocation); percentile
    scratch is a `float[]` field reallocated only when `HistoryValues.Length` changes; `p = rule?.LowerIsWorse == true ? 0.01 : 0.99`
    where `rule = thresholds?.RuleFor(Definition) ?? ThresholdEvaluator.RuleFor(Definition, _settings)`. All-gap
    buffer → bins zero, texts `""`, fraction NaN.
  - `RefreshFpsSummary`: the table above; no allocation beyond the formatted strings.
- `ResolveKind(TileKind preferred, float? explicitMax)`: new first line
  `if (preferred == TileKind.FpsSummary) return GameRole == GameMetricRole.Fps ? TileKind.FpsSummary : TileKind.Sparkline;`
  then the existing body verbatim (`Histogram` passes through the existing `preferred != Auto` return; `Auto`
  rules are untouched).
- `AutomationLabel`: unchanged for other kinds; Histogram appends `", {HistogramMarkerText}"` when non-empty;
  FpsSummary appends `", 1% low {FpsLowText}, frame time {FpsFrameTimeText}"` and `", {FpsRatioText}"` when non-empty.
- `RaiseSeverityRefresh()` additionally raises `PropertyChanged(nameof(FpsLowSeverity))` so the ratio bar's
  `SeverityToBrush` binding re-runs after a live theme switch (same reason as `Severity`).

### `src/Stats.Core/ViewModels/DashboardViewModel.cs`

`RebuildSections` L429: `new MetricTileViewModel(defsById[id], _store[id], _settings, _store)`. Nothing else —
`SetTileKind` → `AfterPrefChange()` → `RebuildSections()` + save already rebuilds containers on a kind change.
`OverlayViewModel.Rebuild` is not touched.

## App (`Stats.App`)

### `src/Stats.App/Controls/HistogramBars.cs` (new `FrameworkElement`)

Same skeleton as `LevelBar`/`Sparkline`: DPs registered with `FrameworkPropertyMetadataOptions.AffectsRender`;
`Loaded` unsubscribes-then-subscribes `ThemeManager.Changed` and `GraphStyle.Changed`, `Unloaded` unsubscribes;
no `Effect`/`BlurEffect`; no animation; no timer.

| DP | Type | Default | Use |
| --- | --- | --- | --- |
| `Bins` | `IReadOnlyList<int>?` | `null` | Bar heights (array DPs cannot be bound in DataTemplates — MC4102). |
| `MarkerFraction` | `double` | `double.NaN` | Marker x as a fraction of width; NaN or outside [0, 1] = no marker. |
| `MarkerLabel` | `string` | `""` | Text drawn next to the marker. |
| `Stroke` | `Brush` | `Brushes.Orange` | Bar and marker colour (bound through `SeverityToBrush`, parameter `accent`). |
| `Track` | `Brush` | same default as `LevelBar.Track` | 1 px baseline under the bars. |

`OnRender` (w × h; return when either ≤ 0):
1. Baseline: `DrawLine(new Pen(Track, 1), (0, h − 0.5), (w, h − 0.5))` (pen cached, rebuilt when `Track` changes).
2. Bars: `n = Bins.Count`; gap 2 px; `barW = (w − 2·(n − 1)) / n`; `maxCount = max(Bins)`; a bar of count `c` is a
   `Rect(x, top, barW, c / maxCount · (h − 13))` with its bottom at `h − 1` (12 px + 1 px reserved at the top for
   the label row). All bars go into one frozen `StreamGeometry`, cached and rebuilt only when the `Bins` reference,
   `w` or `h` change (Sparkline's S1/S5 cache pattern). Fill = `CurveRenderer.BarBrushFor(Stroke, GraphStyle.Effects)`
   (cached on `(Stroke, Effects)`). With `Effects`, also a 1 px highlight line along each bar's top edge in
   `Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)` (LevelBar's highlight), inset 1 px on each side, skipped for bars
   under 3 px tall. `maxCount == 0` or `Bins` null/empty → baseline only.
3. Marker: `x = MarkerFraction · w`; with `Effects` a first pass `DrawLine(CurveRenderer.GlowPenFor(Stroke), …)`,
   then a 1 px pen of `Stroke`'s colour at alpha `0xC0` from `y = labelBottom` (12 when a label is drawn, else 0) to
   `h − 1`. Pens cached on `(Stroke, Effects)`.
4. Label: `FormattedText(MarkerLabel, InvariantCulture, LeftToRight, new Typeface("Segoe UI"), 10, _labelBrush,
   VisualTreeHelper.GetDpi(this).PixelsPerDip)` (HistoryChart's `DrawYLabel` pattern) at `y = 0`; placed to the
   left of the marker (`x − 3 − ft.Width`) when `MarkerFraction > 0.5`, else to the right (`x + 3`), then clamped
   into `[0, w − ft.Width]`. `_labelBrush` = frozen `SolidColorBrush(ThemeManager.Get("TextSecondary"))`, rebuilt in
   the `ThemeManager.Changed` handler (which also `InvalidateVisual`s). Built per render — one render per poll tick.

No hover, no tooltip (non-goal), so no transparent hit-test rectangle is needed.

### `src/Stats.App/Controls/CurveRenderer.cs`

Add `internal static Brush BarBrushFor(Brush stroke, bool effects)`: colour `c` from a `SolidColorBrush` (else
`Colors.Orange`); plain → frozen `SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B))`; effects → frozen vertical
`LinearGradientBrush` from `0xE6` alpha at the top to `0x99` at the bottom (angle 90), same shape as
`FillBrushFor`. Nothing existing in the file changes.

### `src/Stats.App/Views/TileTemplates.xaml`

Two new DataTemplates, inserted after `TileValueTemplate`, plus two attributes on the selector resource.

- `<DataTemplate x:Key="TileHistogram">`: `Border Style=TileBorder` → `DockPanel`:
  - Top header Grid with columns `*`, `Auto`, `24`: `TileName` (DisplayName, tooltip), `HistoryWindowTag` in
    `TileFoot` (`Margin="4,0"`), `Button x:Name="TileMenuButton" Style="{StaticResource TileMenuButtonStyle}"` —
    verbatim from `TileSparkline`.
  - Bottom range row: `DockPanel Margin="0,2,0,0"` with `TextBlock DockPanel.Dock="Right" Text="{Binding HistogramMaxText}"
    Style="{StaticResource TileFoot}"` and `TextBlock Text="{Binding HistogramMinText}" Style="{StaticResource TileFoot}"`
    (`TileFoot`, not `TileOptionalText`, so the row keeps its height while the texts are "" and the bars do not
    jump when the first sample arrives).
  - Top value row: the standard `StackPanel Orientation="Horizontal" Margin="0,4,0,0"` with `ValueText`
    (`TileValue`), `UnitText` (`TileUnit`), `TileSeverityGlyph` — verbatim from `TileSparkline`.
  - Fill: `<controls:HistogramBars Bins="{Binding HistogramBins}" MarkerFraction="{Binding HistogramMarkerFraction}"
    MarkerLabel="{Binding HistogramMarkerText}" Stroke="{Binding Severity, Converter={StaticResource SeverityToBrush},
    ConverterParameter=accent}" Track="{DynamicResource GaugeTrack}" Margin="0,4,0,0"/>`.
- `<DataTemplate x:Key="TileFpsSummary">`: `Border Style=TileBorder` → `DockPanel`:
  - Top header Grid with columns `*`, `24`: `TileName` + `TileMenuButton` — verbatim from `TileValueTemplate`.
  - Bottom ratio row: `DockPanel Margin="0,4,0,0"`: `TextBlock DockPanel.Dock="Right" Text="{Binding FpsRatioText}"
    Style="{StaticResource TileOptionalText}" Margin="8,0,0,0"` and `<controls:LevelBar Height="6" VerticalAlignment="Center"
    Fraction="{Binding FpsLowRatio}" Fill="{Binding FpsLowSeverity, Converter={StaticResource SeverityToBrush},
    ConverterParameter=accent}" Track="{DynamicResource GaugeTrack}"/>` (same control and track as `TileBar`).
  - Bottom small row: `StackPanel Orientation="Horizontal"`: `TextBlock Text="1% low" Style="{StaticResource TileFoot}"`,
    `TextBlock Text="{Binding FpsLowText}" Style="{StaticResource TileFoot}" Foreground="{Binding FpsLowSeverity,
    Converter={StaticResource SeverityToBrush}}" Margin="4,0,0,0"`, `TextBlock Text="·" Style="{StaticResource TileFoot}"
    Margin="6,0"`, `TextBlock Text="{Binding FpsFrameTimeText}" Style="{StaticResource TileFoot}"
    Foreground="{DynamicResource TextPrimary}" ToolTip="Frame time (average over the last poll)"`.
  - Fill: the standard value row (`ValueText`/`UnitText`/`TileSeverityGlyph`) with `VerticalAlignment="Center"`.
- `<views:TileTemplateSelector x:Key="TileSelector" … Histogram="{StaticResource TileHistogram}"
  FpsSummary="{StaticResource TileFpsSummary}"/>`.

`TileTemplates.xaml` is merged by `App.xaml` and both dashboard `ItemsControl`s (`DashboardWindow.xaml` L209 Auto
sections, L302 Free/Snap canvas) use `ItemTemplateSelector="{StaticResource TileSelector}"`, so the new templates
appear in all three layout modes with the fixed `TileDimensions` (M inner ≈ 200×120 → ~15 px bars at M, ~34 px at L).

### `src/Stats.App/Views/TileTemplateSelector.cs`

Add `public DataTemplate? Histogram { get; set; }` and `public DataTemplate? FpsSummary { get; set; }`; in
`SelectTemplate` add `TileKind.Histogram => Histogram,` and `TileKind.FpsSummary => FpsSummary,` before the
existing `_ => Sparkline`. The `Size == S → Compact` line stays first.

### `src/Stats.App/Views/DashboardWindow.xaml.cs` — `OpenTileMenu` (~L390)

- Replace the hard-coded `new[] { Auto, Sparkline, Gauge, Bar, Value }` with a private static
  `TileKind[] MenuKinds = { Auto, Sparkline, Gauge, Bar, Value, Histogram, FpsSummary }` iterated with
  `if (k == TileKind.FpsSummary && tile.GameRole != GameMetricRole.Fps) continue;`.
- `Header = KindHeader(k)` where `private static string KindHeader(TileKind k) => k switch { TileKind.FpsSummary => "FPS summary", _ => k.ToString() }`
  — existing headers stay the identical strings ("Auto", "Sparkline", "Gauge", "Bar", "Value"; "Histogram" reads
  fine as-is). `IsChecked = CurrentPrefKind(id) == k` and the `SetTileKindEditCommand`/`TileKindEdit` execution are
  unchanged. "Set gauge/bar max…", "Details…" etc. untouched.

### `README.md`

"Tiles" paragraph (~L142): kind list becomes "Sparkline/Gauge/Bar/Value/Histogram, plus FPS summary on the FPS
tile". New bullet after the FPS-counter bullet (~L21): what the FPS summary shows (average large, 1% low and frame
time small, the 1%-low/average bar), what the Histogram shows (12 bins over the history window, p99 marker — p1 for
lower-is-worse metrics), and the honesty note that frame-time bins are per-poll averages, not per-frame samples.

## Preview harness

Fixtures are the only automated visual evidence (CLAUDE.md rules 7–8: PresentMon cannot run and the app cannot
launch from this shell). Captures use `--method rtb` at rest; the new control has no animation, and `CaptureHost`'s
two-phase `GraphStyle.Apply` already keeps the sparkline pulse out of the first bind.

- **New scenario `game`** in `tools/Stats.UiPreview/Fixtures/Scenarios.cs` (appended to `Scenarios.Names`, which
  `Program.cs` L88, `FixtureValidityTests.ScenarioNames` and `CompositionIsolationTests.ScenarioNames` all read, so
  every scenario invariant covers it automatically): definitions `cpu.temp.tctl` (62 °C ± 1.5), `cpu.load.total`
  (24 % ± 3), `gpu.temp.core` (56 °C ± 1.5), `gpu.load.core` (42 % ± 4), plus `FrameMetrics.Definitions`. Series
  via `SnapshotBuilder.AddExact` for a **shaped** frame time: 60 ticks, `6.9 ± 0.4 ms` seeded noise except spikes at
  tick indices 9, 23, 24, 41, 52 = 14.2, 11.8, 17.6, 12.9, 15.3 ms, final tick exactly 6.9; `fps.avg` =
  `round(1000 / frameTime)` per tick with the final tick exactly 144; `fps.low1` = `Add(92, 3)`. Dashboard = the
  four hardware ids + the three Game ids; overlay = `FpsId`. `TilePrefs`: `FpsId → { Kind = FpsSummary, Size = M }`,
  `FrameTimeId → { Kind = Histogram, Size = M }`, `LowId` left Auto (a sparkline beside them for comparison). Two
  CPU + two GPU + three Game tiles keep the Game section above the fold at 1180×720. Uniform-noise series from
  `Add` are deliberately avoided for frame time — they give a flat, meaningless histogram.
- **Dashboard substates** (`PreviewComposition.ApplySubstate`, allow-listed in `SubstateCatalog.ByView["dashboard"]`):
  - `game-tiles`: adds `FrameMetrics.FrameTimeId` to `Settings.DashboardMetrics` if absent, sets
    `PrefFor(FpsId).Kind = FpsSummary` and `PrefFor(FrameTimeId).Kind = Histogram`, then `Dashboard.RebuildSections()`.
    Documentary on `game` (prefs already set); on `missing` it produces the all-gap state (both ids exist as
    definitions there but only `fps.avg` has a series, ending in a full gap); on `dense` a flat histogram.
  - `game-tiles-large` / `game-tiles-small`: `game-tiles`, then `PrefFor(FpsId).Size` and `PrefFor(FrameTimeId).Size`
    = `L` / `S`, `RebuildSections()`.
  - `game-summary-orphan`: for every id in `Settings.DashboardMetrics` whose definition has
    `GameMetricRoles.RoleOf(def) == Fps`, `PrefFor(id).Kind = FpsSummary`; `RebuildSections()`. On `gallery` that is
    `gallery.inverted` ("Simulated FPS", Game/fps, Warn at 28 on its 30/15 override, no siblings) → both small
    slots read "—", ratio 0, bar empty.
  - Existing `graphs-plain`, `graphs-warmup`, `layout-free`, `theme-cycle` combine with `game` unchanged.
- **`captures/baseline.json`**, new group under `artifacts/game-tiles/` (all `"Method": "rtb"`, 1180×720 unless noted):

  | # | Scenario | Substate | Theme | Output |
  | --- | --- | --- | --- | --- |
  | 1 | `game` | — | Dark Amber | `v14-dashboard-game-tiles-dark-amber.png` |
  | 2 | `game` | — | Light | `v14-dashboard-game-tiles-light.png` |
  | 3 | `game` | `game-tiles-large` | Dark Amber | `v14-dashboard-game-tiles-large-dark-amber.png` (1180×800 — two L tiles + one M wrap to a second Game row) |
  | 4 | `game` | `game-tiles-small` | Dark Amber | `v14-dashboard-game-tiles-small-dark-amber.png` |
  | 5 | `game` | `graphs-plain` | Dark Amber | `v14-dashboard-game-tiles-plain-dark-amber.png` |
  | 6 | `game` | `graphs-warmup` | Dark Amber | `v14-dashboard-game-tiles-warmup-dark-amber.png` (15 samples, includes spike at tick 52) |
  | 7 | `gallery` | `game-summary-orphan` | Dark Amber | `v14-dashboard-game-summary-orphan-dark-amber.png` |
  | 8 | `missing` | `game-tiles` | Dark Amber | `v14-dashboard-game-tiles-gap-dark-amber.png` |
  | 9 | `game` | `layout-free` | Dark Amber | `v14-dashboard-game-tiles-layout-free-dark-amber.png` |
  | 10 | `game` | `theme-cycle` | (fans out to 6) | `v14-dashboard-game-tiles-theme-cycle.png` — proves the control-drawn marker/label re-colour on `ThemeManager.Changed` |

- **What the harness can show:** both templates at M/L/S in all three layout modes and both themes; effects on vs
  off; the shaped histogram with its p99 marker and range labels; the "—"/empty degradation; the all-gap state; the
  warm-up state; theme re-colouring of control-drawn text.
- **What it cannot show:** the "Tile kind" submenu with the new entries (`--method rtb` renders the window visual
  only; the `tile-menu` substate opens the context menu but not the submenu, and `--method screen` returned blank
  frames in this shell per `docs/graph-effects/EVIDENCE.md`); live PresentMon values or a real stutter; the
  1.9.x downgrade behaviour; idle CPU cost. All four go on the owner checklist. Composition-only xunit tests
  (`tests/Stats.UiPreview.Tests/GameTilesSubstateTests.cs`, pattern of `GraphEffectsSubstateTests`) cover the
  substates' VM/settings effects headlessly.

## Non-goals

No new PresentMon fields, no change to FPS capture or `FrameStatsAggregator`, no per-process FPS, no histogram
hover/tooltips beyond the static marker label, no Details-window histogram, no user-tunable bin count or range, no
compact (S) variants, no animation of bar heights, no overlay or tray changes, no `SettingsChange` member, no
composition-root change, no new NuGet packages, nothing under `Fans/` (rule 6).

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings (xUnit analyzers: constants in the
  `expected` slot).
- **Tests — `tests/Stats.Core.Tests`:**
  - `HistogramBinningTests`: `Bin_TwelveBins_CountsSumToFiniteCount`; `Bin_IgnoresNaN` (finite count and sum exclude
    NaN); `Bin_MaxValueLandsInLastBin_MinInFirst`; `Bin_DegenerateRange_AllSamplesInMiddleBin_MinEqualsMax`;
    `Bin_AllNaN_ReturnsZero_NaNRange_ZeroCounts`; `Bin_EmptyCounts_Throws`;
    `Percentile_NearestRank_MatchesFrameStatsAggregatorRule` (1..100 → p99 = 99, p1 = 1, p0.5 = 50);
    `Percentile_SingleSample_ReturnsIt`; `Percentile_IgnoresNaN`; `Percentile_NoFinite_ReturnsNaN`;
    `Percentile_ScratchTooSmall_Throws`; `Fraction_MinIsZero_MaxIsOne_Clamped_DegenerateIsHalf_NaNIsNaN`.
  - `GameMetricRolesTests`: `RoleOf_FrameMetricsDefinitions_YieldsExactlyOneOfEachRole`;
    `RoleOf_NonGameGroup_IsNone` (a Cpu/"fps" def); `RoleOf_GameFpsWithoutOnePercent_IsFps` ("Simulated FPS");
    `RoleOf_GameOtherUnit_IsNone`; `Find_ReturnsFirstMatch_OrNull`.
  - `TileKindTests`: `TileKind_ExistingOrdinalsStable_NewMembersAppended` (Auto 0 … Value 4, Histogram 5, FpsSummary 6).
  - `SettingsServiceTests` (append): `SaveThenLoad_TileKind_HistogramAndFpsSummary_RoundTrip_AsString`
    (asserts `"Kind": "Histogram"` in the file); `Load_UnknownTileKind_FallsBackToAuto_WithoutWipingOtherSettings`
    (a file with `"Kind": "Donut"` plus `PollIntervalSeconds: 2.5` loads with Auto and 2.5);
    `Load_NullOrNonStringTileKind_FallsBackToAuto` (`null`, `7`, `{}`).
  - `GameTileViewModelTests` (new): `Histogram_Refresh_ComputesBins_Range_MarkerFraction_MarkerText` (a known
    12-bin case, p99 label formatted with the definition's unit/format); `Histogram_BinsReferenceAlternates_EveryRefresh`;
    `Histogram_LowerIsWorseRule_UsesP1_AndLabelsIt`; `Histogram_AllGaps_EmptyOutputs`;
    `Histogram_OnSparklineKind_OutputsStayDefault`; `Histogram_WithoutStore_OutputsStayDefault` (overlay path);
    `FpsSummary_ResolvesSiblingsByRole_FormatsLowAndFrameTime_RatioAndSeverity` (store built from
    `FrameMetrics.Definitions`, avg 96 / low 61 / ft 10.4 → `"61 fps"`, `"10.4 ms"`, ratio 0.635 ± 0.001, `"64% of avg"`,
    `FpsLowSeverity == Normal`; low 12 → Crit on the 30/15 override); `FpsSummary_MissingSiblings_ReadDash_RatioZero`
    (a lone Game/"fps" def); `FpsSummary_SiblingGap_ReadsDash` (sibling exists, current null);
    `FpsSummary_PrefOnNonFpsRole_ResolvesToSparkline` (`fps.low1`, `cpu.temp`); `FpsSummary_AutomationLabel_IncludesLowFrameTimeAndRatio`;
    `RaiseSeverityRefresh_AlsoRaisesFpsLowSeverity`; `GameRole_ComputedFromDefinition`.
  - `ViewModelTests` (append): `Tile_Kind_Auto_UnchangedForGameMetrics` (Auto on `fps.frametime` and `fps.avg` →
    Sparkline).
  - `DashboardViewModelTests` (append): `SetTileKind_Histogram_RebuildsTileAsHistogram_AndSavesOnce`;
    `RebuildSections_PassesStore_SoFpsSummaryResolvesSiblings` (store with `FrameMetrics.Definitions`, pref FpsSummary
    on `fps.avg` → tile `Kind == FpsSummary` and `FpsLowText != "—"` after a snapshot).
- **Tests — `tests/Stats.UiPreview.Tests`:** `GameTilesSubstateTests`: `SubstateCatalog_AcceptsGameSubstates_ForDashboard`;
  `SubstateCatalog_RejectsGameSubstates_ForOtherViews`; `GameScenario_PresetsFpsSummaryAndHistogramPrefs`;
  `GameScenario_FrameTimeSeriesHasSpikes_AndP99AboveBaseline` (the built tile VM's `HistogramMarkerText` starts
  with "p99" and max text > min text); `GameTilesSubstate_OnMissing_AddsFrameTime_AndShowsDashes`;
  `GameTilesLargeAndSmall_SetSizes`; `GameSummaryOrphan_OnGallery_SetsFpsSummary_WithDashSlots`;
  `GameScenario_GameSectionIsLast_AndHasThreeTiles`. The existing `FixtureValidityTests`/`CompositionIsolationTests`
  theories cover `game` via `Scenarios.Names`.
- **Captures:** the ten `baseline.json` entries above written with 0 failures and empty sidecar `Warnings`;
  results and the "could not show" list recorded in `docs/game-tiles/EVIDENCE.md` (format of
  `docs/graph-effects/EVIDENCE.md`).
- **Owner checklist** (needs the real app, launched from the Start menu so PresentMon can run):
  1. With a game in the foreground, the FPS summary's average / 1% low / frame time agree with the three separate
     tiles; the bar and "% of avg" are plausible; the 1% low text and bar turn amber/red per its own 30/15 rule.
  2. The frame-time histogram's right tail grows and the p99 marker moves during a stutter; the FPS histogram
     shows a "p1" marker at the low end.
  3. "Tile kind" submenu: "Histogram" on every tile, "FPS summary" only on the FPS tile, check state follows the
     pref, switching back to Auto restores the previous look; the S size shows the compact value tile for both.
  4. Live theme switch (Settings → Appearance) re-colours the marker, label and range text; Light preset legible.
  5. "Glow and motion effects" off gives flat bars and a plain marker; on gives gradient/highlight/glow; idle CPU
     unchanged with a histogram and a summary on screen (Task Manager, ~1 % or less).
  6. Free/Snap: both tiles drag and nudge like the others.
  7. With PresentMon not running (no Game metric selected elsewhere, or the MSIX-shell case): summary reads "—"
     everywhere with an empty bar, histogram shows only its baseline, no binding errors in
     `%AppData%\Stats\logs`.
  8. Downgrade check (release note): a 1.9.x build opened on a settings.json that contains `Histogram`/`FpsSummary`
     resets **all** settings — back up `settings.json` before downgrading.
