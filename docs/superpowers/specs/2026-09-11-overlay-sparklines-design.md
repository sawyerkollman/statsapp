# Overlay sparklines and status line — design

Date: 2026-09-12. Base: `feature/v1.10` @ `4323b0b` (master + dashboard layout modes + graph effects — see
`docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md` and
`docs/superpowers/specs/2026-09-11-graph-effects-design.md`). Branch `feature/overlay-sparklines`, PR to
`feature/v1.10`. Lifts the graph-effects spec's "no changes to the overlay" non-goal; everything else in both base
specs stands.

Owner ask, paraphrased: the always-on-top overlay is text only. Put a compact per-metric sparkline beside each
value (horizontal) / below it (vertical), reusing the dashboard's `Sparkline`/`CurveRenderer`/`GraphStyle`/fixed
`SampleAxis`, and add an optional one-line status strip (fan control state / game mode / PresentMon reason) at the
overlay's edge.

## Goal

1. **Sparklines.** Every overlay tile gets a 56 px-wide sparkline exactly one value-line tall, drawn by the existing
   `Stats.App/Controls/Sparkline.cs` from the data the tile already carries (`MetricTileViewModel.HistoryValues` +
   `HistorySampleCapacity` — the same ring buffer and history window the dashboard tiles show). Horizontal overlay:
   the sparkline sits to the right of the value on the same line. Vertical overlay: it sits under the value. It
   inherits Smooth lines / Glow and motion effects from `GraphStyle` exactly like a dashboard sparkline (no third
   gate), uses the value text's own pinned overlay brush for its stroke/fill/glow, draws no min/max guides, and has
   no hover, tooltip or crosshair (the overlay is usually click-through).
2. **Status line.** An optional single strip at the bottom edge *inside* the overlay panel, e.g.
   `Fans: Balanced · Game mode: gaming (Balanced since 14:30) · PresentMon: access denied`. Each source contributes
   a segment only while it has something to say (fan control on, game mode enabled, PresentMon unavailable), so
   with nothing to say the strip collapses even when the setting is on. Faults (fan write failed / source
   unavailable, PresentMon reason) switch the strip to the warn style with the `Icon.Warn` glyph so severity never
   relies on colour alone (`docs/ui-polish/DESIGN.md`).
3. **Off path unchanged.** With `OverlayGraphs = None` and `OverlayStatusLine = false` the overlay renders
   pixel-identically to today (same visual tree measurements, same brushes), and layout, click-through, move mode,
   opacity, orientation and hotkey behaviour are untouched in every mode.

## Owner decisions (already made — recorded, not re-litigated)

1. New setting `OverlayGraphs`, enum `{ None, Sparkline }`, default `Sparkline` — append-only, with a lenient string
   converter like `DashboardLayoutMode` — toggled from Settings → Overlay; and `OverlayStatusLine` (`bool`, default
   `false`).
2. Sparkline size follows `OverlayFontScale`: width 56 px × the text line height at scale 1.0 (the panel's
   `LayoutTransform` scales it with the text). Glow/pulse follow `GraphStyle` exactly like dashboard sparklines. No
   hover/tooltip in the overlay.
3. The overlay's existing layout, click-through, move mode, opacity, orientation and hotkey behaviour are unchanged
   when `OverlayGraphs = None` and `OverlayStatusLine = false` (byte-identical XAML path).

## Owner decisions assumed (made here because the brief left them open)

1. **Stroke colour** = the value's own `Severity` brush through `SeverityToBrush` with `ConverterParameter=Overlay`
   (pinned `OverlayTextPrimary` at Normal, `OverlayWarn`/`OverlayCrit` otherwise — never the theme-tinted
   `AccentBrush`, which can go dark under the Light preset or a custom accent). The line, fill gradient, glow and
   pulse all derive from that one brush (`CurveRenderer.FillBrushFor/GlowPenFor/PulsePenFor` take the stroke), so
   the overlay gains no new colour — consistent with "changing overlay colours" being a non-goal.
2. **Sparkline box**: `Width="56"`, `Height` bound to the value `TextBlock`'s `ActualHeight` (≈ 21 px for 16 px
   Segoe UI at scale 1.0) — the literal "text line height". Same box in both orientations; in vertical mode it is
   left-aligned under the value. Margins: 6 px to the left of it (horizontal), 2 px above it (vertical).
3. **No hover** is achieved with `IsHitTestVisible="False"` on the overlay's `Sparkline` instance (plus
   `ShowGuides="False"`); no new `Sparkline` DP. The window's `MouseLeftButtonDown → DragMove` still fires because
   the hit lands on the panel `Border` behind it.
4. **Effects policy**: no overlay-specific gate. `GraphStyle.SmoothLines/Effects/Motion` apply as-is (owner
   decision 2), so the 500 ms last-value pulse does run inside the layered (`AllowsTransparency`) window once per
   poll tick per metric. The existing "Glow and motion effects" switch (Settings → Appearance → Graphs) and the OS
   animation preference are the only off switches; "CPU at idle with the overlay visible, effects on vs off" goes
   on the owner checklist. Nothing runs between ticks.
5. **Strip content is attention-per-source**, in the brief's fixed order **Fans · Game mode · PresentMon**, joined
   with `" · "`: a source is omitted when it is off/unavailable/healthy respectively (fan control off or no
   controllable channels; game mode disabled; PresentMon available or not active). Nothing → empty → strip
   collapsed.
6. **Strip wording**: `Fans: <ActiveProfile or Custom>` with the suffix ` — write failed` (any channel
   `WriteFailed`) else ` — source unavailable` (any channel `SourceUnavailable`), matching the Fans window's
   vocabulary (`FansViewModel.ActiveProfileName`, `FanChannelViewModel.StatusText`); the game-mode segment is
   `GameModeSwitcher.StatusText` verbatim; the PresentMon segment is `App.FrameStatus()` verbatim minus one
   trailing period.
7. **Strip styling**: informational = `OverlayTextSecondary`, 10 px (the overlay's own label size), no glyph;
   warning = `OverlayWarn` text + `Icon.Warn` glyph (12×12, `OverlayWarn`). Bottom edge inside the `Border` in both
   orientations; wraps at the tiles' width and never widens the panel (it only adds height).
8. **Composition lives in Core** (`OverlayStatusComposer`, pure, unit-tested); `App.xaml.cs` only gathers inputs on
   the UI thread and pushes the result into `OverlayViewModel.SetStatus`.
9. **Settings surface**: two CheckBoxes under the existing Settings → Overlay tab after "Click-through" —
   "Sparklines beside each value" and "Status line (fan control, game mode, FPS source)". The enum is mirrored by a
   bool on `SettingsViewModel` exactly like `OverlayIsVertical` mirrors `OverlayOrientation`; both reuse
   `SettingsChange.Overlay` (App already calls `ApplyLayout()` there) — no new `SettingsChange` member.
10. **Fan-conflict warnings are not in the strip**: `App._processNames` is refreshed only while the Fans window is
    visible (`RefreshProcessNames`), so the strip would show stale data.
11. **Push cadence**: the status is composed once per coalesced refresh while the overlay is visible, once when it
    becomes visible, and immediately after `SettingsChange.Overlay`. `--interactive` harness mode pushes no status.
12. **Harness**: five new overlay substates (below); status is injected through the real composer with fixture
    inputs, so `PreviewComposition` does not need to expose its `GameModeSwitcher`.

## Settings (rule 3: every field defaulted, sanitized, old files load)

- New `src/Stats.Core/Settings/OverlayGraphs.cs`:
  - `public enum OverlayGraphs { None, Sparkline }` — append-only (doc comment says so, like `DashboardLayoutMode`).
  - `public sealed class OverlayGraphsConverter : JsonConverter<OverlayGraphs>` — a verbatim sibling of
    `DashboardLayoutModeConverter` (`src/Stats.Core/Settings/DashboardLayoutMode.cs`): `Read` skips a stray
    `StartObject`/`StartArray`, returns the member for a case-insensitive string match, and **falls back to
    `OverlayGraphs.Sparkline`** (the default, *not* `default(enum)`) for anything else; `Write` emits the member
    name. Same rationale: the global `JsonStringEnumConverter` in `SettingsService` throws on an unknown token and
    `SettingsService.Load` would then discard the *whole* file.
- `AppSettings` gains a new block after `// ---- graph effects ----`:
  ```csharp
  // ---- overlay sparklines ----
  [JsonConverter(typeof(OverlayGraphsConverter))]
  public OverlayGraphs OverlayGraphs { get; set; } = OverlayGraphs.Sparkline;
  public bool OverlayStatusLine { get; set; }
  ```
- `SettingsService.Normalize`: **no change** (a bool and a converter-guarded enum need no clamp). A missing property
  never reaches the converter, so a v1.9.x `settings.json` loads with `Sparkline`/`false`.
- `SettingsViewModel` (`src/Stats.Core/ViewModels/SettingsViewModel.cs`): two observables seeded in the constructor
  before `_loaded = true`:
  `[ObservableProperty] private bool _overlaySparklines;` (= `settings.OverlayGraphs == OverlayGraphs.Sparkline`)
  and `[ObservableProperty] private bool _overlayStatusLine;` (= `settings.OverlayStatusLine`), with
  ```csharp
  partial void OnOverlaySparklinesChanged(bool value)
  { if (!_loaded) return; _s.OverlayGraphs = value ? OverlayGraphs.Sparkline : OverlayGraphs.None; Raise(SettingsChange.Overlay); }
  partial void OnOverlayStatusLineChanged(bool value)
  { if (!_loaded) return; _s.OverlayStatusLine = value; Raise(SettingsChange.Overlay); }
  ```
  `SettingsChange` is unchanged.
- `DashboardWindow.xaml`, Settings → Overlay `TabItem` (currently lines ~681–716): directly after the Click-through
  `CheckBox` (`IsChecked="{Binding OverlayClickThrough}"`), add
  `<CheckBox Content="Sparklines beside each value" Margin="0,4" IsChecked="{Binding OverlaySparklines}"/>` and
  `<CheckBox Content="Status line (fan control, game mode, FPS source)" Margin="0,4" IsChecked="{Binding OverlayStatusLine}"/>`
  (same implicit CheckBox style as "Smooth lines"/"Glow and motion effects" in the Appearance tab). Nothing else in
  the tab moves.

## Core (`Stats.Core`, WPF-free, testable)

- New `src/Stats.Core/ViewModels/OverlayStatusComposer.cs`:
  ```csharp
  /// One composed overlay status strip: the text and whether any segment is a fault (warn styling + glyph).
  public sealed record OverlayStatus(string Text, bool IsWarning)
  {
      public static readonly OverlayStatus Empty = new("", false);
  }

  public static class OverlayStatusComposer
  {
      public const string Separator = " · ";
      public const string FanPrefix = "Fans: ";
      public const string CustomProfile = "Custom";
      public const string WriteFailedSuffix = " — write failed";
      public const string SourceUnavailableSuffix = " — source unavailable";

      /// Pure. Segments in fixed order Fans · Game mode · PresentMon; a segment is omitted when its source has
      /// nothing to say; an empty result is OverlayStatus.Empty.
      public static OverlayStatus Compose(
          bool fanControlEnabled, string? activeFanProfile, IReadOnlyList<FanChannelStatus> fanChannelStatuses,
          string? gameModeStatus,
          string? frameReason);
  }
  ```
  Rules:
  - **Fan segment** only when `fanControlEnabled && fanChannelStatuses.Count > 0`: `FanPrefix + (activeFanProfile
    null/whitespace ? CustomProfile : activeFanProfile.Trim())`, then `WriteFailedSuffix` if any status is
    `FanChannelStatus.WriteFailed`, else `SourceUnavailableSuffix` if any is `SourceUnavailable`; either suffix sets
    `IsWarning`. `Idle`/`Active`/`WaitingForSource` add nothing.
  - **Game segment** = `gameModeStatus.Trim()` when non-empty (the caller passes `null` when
    `AppSettings.GameModeEnabled` is false; `GameModeSwitcher.StatusText` already reads "Game mode: …").
  - **PresentMon segment** = `frameReason.Trim().TrimEnd('.')` when non-empty; sets `IsWarning`.
  - Join the present segments with `Separator`; no segments → `OverlayStatus.Empty`.
- `OverlayViewModel` (`src/Stats.Core/ViewModels/OverlayViewModel.cs`) additions:
  ```csharp
  /// OverlayGraphs == Sparkline; re-read by ApplyLayout(). Drives the Sparkline's Visibility in OverlayWindow.
  [ObservableProperty] private bool _showSparklines;
  /// AppSettings.OverlayStatusLine; re-read by ApplyLayout().
  [NotifyPropertyChangedFor(nameof(HasStatus))]
  [ObservableProperty] private bool _showStatusLine;
  /// Composed strip text (OverlayStatusComposer), pushed by the composition root; "" = nothing to show.
  [NotifyPropertyChangedFor(nameof(HasStatus))]
  [ObservableProperty] private string _statusText = "";
  [ObservableProperty] private bool _statusIsWarning;
  /// The strip's Visibility: on in settings AND something to say.
  public bool HasStatus => ShowStatusLine && StatusText.Length > 0;
  /// Called by App (UI thread) once per refresh while the overlay is visible; null = Empty. The toolkit-generated
  /// setters compare with EqualityComparer<T>.Default, so a per-tick call that changes nothing raises nothing.
  public void SetStatus(OverlayStatus? status)
  { status ??= OverlayStatus.Empty; StatusText = status.Text; StatusIsWarning = status.IsWarning; }
  ```
  `ApplyLayout()` additionally sets `ShowSparklines = _settings.OverlayGraphs == OverlayGraphs.Sparkline;` and
  `ShowStatusLine = _settings.OverlayStatusLine;` (the constructor already calls `ApplyLayout()`, so both are
  seeded). `Rebuild()`, `RefreshAll()`, `RaiseSeverityRefresh()` are unchanged — the status survives a `Rebuild()`.
- `MetricTileViewModel`, `MetricHistory`, `SampleAxis`, `CurveSmoothing`: **no change** — `HistoryValues`,
  `HistorySampleCapacity`, `Severity` and `CurrentText` are already filled by `Refresh(ThresholdIndex?)` for overlay
  tiles.

## App (`Stats.App`)

### `Views/OverlayWindow.xaml`

Add `xmlns:controls="clr-namespace:Stats.App.Controls"` (as in `TileTemplates.xaml`). The root `Grid` and the
move-mode `Rectangle` are unchanged. Inside the `Border` (`Background="#E01B1B1C" CornerRadius="8" Padding="12,8"`,
`LayoutTransform` bound to `FontScale` — all unchanged), the `ItemsControl` is wrapped in a vertical `StackPanel`
that holds the tiles host and, below it, the strip:

```xml
<StackPanel>
    <ItemsControl x:Name="TilesHost" ItemsSource="{Binding Tiles}">
        <ItemsControl.ItemsPanel> <!-- unchanged: StackPanel, Orientation via OverlayOrientation converter --> </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <StackPanel Margin="10,2">
                    <TextBlock Text="{Binding DisplayName}" .../>            <!-- unchanged -->
                    <!-- value row: [value][sparkline] when the overlay is horizontal, [value] over [sparkline] when vertical -->
                    <StackPanel Orientation="{Binding DataContext.Orientation, RelativeSource={RelativeSource AncestorType=ItemsControl}, Converter={StaticResource OverlayOrientation}}">
                        <TextBlock x:Name="ValueText" Text="{Binding CurrentText}" .../> <!-- unchanged attributes -->
                        <controls:Sparkline Width="56" Height="{Binding ActualHeight, ElementName=ValueText}"
                                            Values="{Binding HistoryValues}" Capacity="{Binding HistorySampleCapacity}"
                                            Stroke="{Binding Severity, Converter={StaticResource SeverityToBrush}, ConverterParameter=Overlay}"
                                            ShowGuides="False" IsHitTestVisible="False"
                                            Visibility="{Binding DataContext.ShowSparklines, RelativeSource={RelativeSource AncestorType=ItemsControl}, Converter={StaticResource BoolToVis}}">
                            <controls:Sparkline.Style>
                                <Style TargetType="controls:Sparkline">
                                    <Setter Property="Margin" Value="6,0,0,0"/>
                                    <Setter Property="VerticalAlignment" Value="Center"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding DataContext.Orientation, RelativeSource={RelativeSource AncestorType=ItemsControl}}" Value="Vertical">
                                            <Setter Property="Margin" Value="0,2,0,0"/>
                                            <Setter Property="HorizontalAlignment" Value="Left"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </controls:Sparkline.Style>
                        </controls:Sparkline>
                    </StackPanel>
                </StackPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
    <!-- status strip: bottom edge in both orientations, inside the Border so FontScale and the move-mode outline cover it -->
    <Grid Margin="10,4,10,0" MaxWidth="{Binding ActualWidth, ElementName=TilesHost}"
          Visibility="{Binding HasStatus, Converter={StaticResource BoolToVis}}">
        <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Path Style="{StaticResource IconGlyph}" Data="{StaticResource Icon.Warn}" Width="12" Height="12"
              Fill="{StaticResource OverlayWarn}" Margin="0,0,4,0" VerticalAlignment="Center"
              Visibility="{Binding StatusIsWarning, Converter={StaticResource BoolToVis}}"/>
        <TextBlock Grid.Column="1" Text="{Binding StatusText}" FontSize="10" TextWrapping="Wrap" VerticalAlignment="Center">
            <TextBlock.Style>
                <Style TargetType="TextBlock">
                    <Setter Property="Foreground" Value="{StaticResource OverlayTextSecondary}"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding StatusIsWarning}" Value="True">
                            <Setter Property="Foreground" Value="{StaticResource OverlayWarn}"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </TextBlock.Style>
        </TextBlock>
    </Grid>
</StackPanel>
```

Why each piece:

- `Sparkline` has no `MeasureOverride`, so without explicit `Width`/`Height` it measures 0×0 inside the
  `SizeToContent` window and draws nothing; `Width="56"` + `Height` bound to `ValueText.ActualHeight` is owner
  decision 2 literally (the value line is 16 px SemiBold → ≈ 21 px). The `LayoutTransform` on the `Border` scales
  the box and its pens (1.5 px line, 4.5 px glow) with the text at `OverlayFontScale` 0.8–1.6.
- The value row reuses the **same** `OverlayOrientation` converter binding as the `ItemsPanel`: Horizontal overlay
  → horizontal row (sparkline beside the value); Vertical overlay → vertical row (sparkline below). The `DataTrigger`
  swaps only margin/alignment (in a vertical `StackPanel` an element with an explicit `Width` would otherwise
  centre).
- **Off path**: when `ShowSparklines` is false the sparkline is `Collapsed`; a `StackPanel` with a single visible
  `TextBlock` measures exactly that `TextBlock`, and left-aligned text renders identically whether the block is
  stretched (old) or shrink-wrapped (new) — so the tile's measured size and pixels are unchanged. When `HasStatus` is
  false the strip is `Collapsed` and the outer `StackPanel` measures exactly the `ItemsControl`. Both facts are
  checked by the `sparklines-off` capture (below).
- `ShowGuides="False"`: the guide/hover pens derive from `ThemeManager.Get("TextPrimary")`, which is near-black
  under the Light preset — wrong on the fixed-dark panel. `IsHitTestVisible="False"`: `OnMouseMove` never runs (no
  `ToolTip`, no crosshair); with click-through off, a press over the sparkline hits the `Border` and the window's
  `DragMove` handler still works; with click-through on (`WS_EX_TRANSPARENT`) nothing reaches the window anyway.
- `Stroke` uses the same `Severity` binding as the value's `Foreground`, so the existing
  `OverlayViewModel.RaiseSeverityRefresh()` call after `ThemeManager.Apply` re-runs it (the overlay brushes are
  pinned, so this is only for consistency). `Unit` is not bound (tooltip only).
- The strip's `MaxWidth` follows the tiles host's `ActualWidth` (a one-way binding to a read-only DP that settles in
  the same layout cycle), so the strip **never widens the panel** — a long line wraps at the tiles' width
  (horizontal: rarely; vertical: into a few lines under the column). `SizeToContent` therefore only ever gains
  height from the strip, and only when the text changes (state transitions, not per tick). An empty overlay (no
  metrics) gives `MaxWidth` 0 and hides the strip, which is fine.
- `Icon.Warn` (`Views/Icons.xaml`) and the `IconGlyph` style (`Views/Controls.xaml`) are app-level resources merged
  by `App.xaml`; the local `Fill` overrides the style's theme fill. Colours are the pinned overlay keys only
  (`OverlayTextSecondary`, `OverlayWarn`); nothing theme-replaced is used on the panel (DESIGN.md).
- `OverlayWindow.xaml.cs`, `Sparkline.cs`, `CurveRenderer.cs`, `GraphStyle.cs`, `SampleAxis.cs`, `Theme.xaml`,
  `Icons.xaml`, and every converter are **unchanged**.

### `App.xaml.cs`

New private method (UI thread only; reads published state only — rule 1; never calls a `FanController` setter,
`SetMode`, `ApplyProfile` or `Enabled`'s setter — rule 6):

```csharp
/// Composes the overlay status strip from already-published state and pushes it (no-op when unchanged).
private void PushOverlayStatus()
{
    if (_overlayVm is null || _settings is null || !_settings.OverlayStatusLine) return;
    bool fanEnabled = _fanController is { } fc && fc.Enabled;                 // getter only: brief lock on AppSettings.SyncRoot
    string? profile = fanEnabled ? _fanController!.ActiveProfile : null;
    IReadOnlyList<FanChannelStatus> statuses = fanEnabled
        ? _fanController!.Views().Select(v => v.Status).ToList()               // same UI-thread call FansViewModel.Refresh makes
        : Array.Empty<FanChannelStatus>();
    string? game = _settings.GameModeEnabled ? _gameMode?.StatusText : null;   // volatile string composed on the poll thread
    _overlayVm.SetStatus(OverlayStatusComposer.Compose(fanEnabled, profile, statuses, game, FrameStatus()));
}
```

Call sites (three, all existing seams):
1. `RunCoalescedRefresh()` (~line 547): `if (_overlay is { IsVisible: true }) { _overlayVm?.RefreshAll(); PushOverlayStatus(); }`.
2. The `_overlay.IsVisibleChanged` catch-up handler (~line 264): also call `PushOverlayStatus()` when
   `e.NewValue is true`, so a freshly shown overlay is not stale for a whole poll interval.
3. `OnSettingsChanged` `case SettingsChange.Overlay` (~line 750): after `_overlayVm?.ApplyLayout();` call
   `PushOverlayStatus();` so toggling the checkbox shows/hides the line immediately.

Cost when off: `PushOverlayStatus` returns at its first line; nothing is allocated. When on: one `Views()` list per
tick while the overlay is visible and fan control is enabled (the Fans window already does this every tick when
open). No new subscriptions to `SensorPoller.SnapshotAvailable`; no new cross-thread state — `FrameStatus()` is the
existing unsynchronised read.

`SetupTray`, `Enter/ExitMoveMode`, `ToggleOverlay`, `ApplyFrameTracing`, `UpdateTrayTooltip` are unchanged.

### `README.md`

Extend the "▣ Overlay" bullet (~line 183) with one sentence each for the sparkline (Settings → Overlay → "Sparklines
beside each value", on by default; beside the value horizontally, below it vertically; same history window and
Smooth lines / Glow and motion effects as the dashboard) and the status line ("Status line", off by default; active
fan profile with write-failed / source-unavailable faults, game mode, and why FPS is unavailable; only appears when
one of those has something to say).

## Preview harness (`tools/Stats.UiPreview`)

New overlay substates (append to `SubstateCatalog.ByView["overlay"]`, combinable with the existing ones via `+`):

| Substate | `PreviewComposition` effect |
| --- | --- |
| `sparklines` | `c.Settings.OverlayGraphs = OverlayGraphs.Sparkline; c.Overlay.ApplyLayout();` — documentary (the default), like `graphs-effects`. |
| `sparklines-off` | `c.Settings.OverlayGraphs = OverlayGraphs.None; c.Overlay.ApplyLayout();` — the byte-identical off path. |
| `sparklines-warmup` | No `ApplySubstate` work; `Build()` trims the applied ticks to the most recent 25 % exactly like `graphs-warmup`/`detail-warmup` (extend that one condition), so the right-anchored `SampleAxis` is visible in the overlay. |
| `status-line` | `c.Settings.OverlayStatusLine = true; c.Overlay.ApplyLayout(); c.Overlay.SetStatus(OverlayStatusComposer.Compose(true, "Balanced", new[] { FanChannelStatus.Active, FanChannelStatus.Idle }, "Game mode: gaming (Balanced since 14:30)", null));` → `Fans: Balanced · Game mode: gaming (Balanced since 14:30)`, informational. |
| `status-line-warn` | Same flag; `Compose(true, null, new[] { FanChannelStatus.WriteFailed, FanChannelStatus.Active }, "Game mode: desktop", "PresentMon: access denied (simulated).")` → `Fans: Custom — write failed · Game mode: desktop · PresentMon: access denied (simulated)`, warning. |

Fixture strings are in the real producers' formats (the game-mode text is what `GameModeSwitcher.UpdateStatus`
emits; the PresentMon text is what `App.FrameStatus()` would hand over), injected the way `ScenarioFixture.GameStatus`
already injects a PresentMon reason for the dashboard — no real hardware, no `GameModeSwitcher` exposure, no
timezone-dependent `HH:mm`.

`captures/baseline.json` gains these entries (all `"Method": "rtb"`; outputs under `artifacts/overlay-sparklines/`,
which is git-ignored like every other `artifacts/` folder — the committed evidence is `docs/overlay-sparklines/EVIDENCE.md`):

| Scenario | View | Substate | Theme | Size | Output |
| --- | --- | --- | --- | --- | --- |
| normal | overlay | `sparklines` | Dark Amber | 400×200 | `v13-overlay-sparklines-dark-amber.png` |
| normal | overlay | `sparklines` | Light | 400×200 | `v13-overlay-sparklines-light.png` (pinned colours survive the Light preset) |
| normal | overlay | `sparklines-off` | Dark Amber | 400×200 | `v13-overlay-sparklines-off-dark-amber.png` (off path) |
| normal | overlay | `vertical+sparklines` | Dark Amber | 200×400 | `v13-overlay-vertical-sparklines-dark-amber.png` |
| thresholds | overlay | `sparklines` | Dark Amber | 400×200 | `v13-overlay-sparklines-severity-dark-amber.png` (Tctl Warn, GPU Crit, FPS Warn strokes) |
| normal | overlay | `sparklines-warmup` | Dark Amber | 400×200 | `v13-overlay-sparklines-warmup-dark-amber.png` |
| normal | overlay | `status-line` | Dark Amber | 400×200 | `v13-overlay-status-line-dark-amber.png` |
| normal | overlay | `status-line-warn` | Dark Amber | 400×200 | `v13-overlay-status-line-warn-dark-amber.png` |
| normal | overlay | `status-line-warn` | Light | 400×200 | `v13-overlay-status-line-warn-light.png` |
| normal | overlay | `vertical+status-line-warn` | Dark Amber | 200×400 | `v13-overlay-vertical-status-line-warn-dark-amber.png` (wrapped strip) |
| settings | settings | `category-overlay` | Dark Amber | 1180×900 | `v13-settings-overlay-dark-amber.png` (the two new checkboxes) |

Captures are at rest by construction: `CaptureHost.Run` binds the window with `GraphEffects=false`, settles, then
applies the fixture's real `GraphStyle` — so no pulse is in flight for the overlay's new `Sparkline` instances either.
Every sidecar's `Warnings` (the `BindingErrorListener`) must be empty — that is the only automated check on the
XAML bindings (`ElementName=ValueText`, `ElementName=TilesHost`, the `RelativeSource` paths).

**What the harness can show**: sparkline presence/size/placement per orientation, stroke = value colour at Normal/
Warn/Crit, fill + glow with effects on, the fixed axis during warm-up, the off path's pixel identity, the strip's
text/glyph/colour/wrapping and that it does not widen the panel, both themes, the Settings checkboxes.

**What it cannot show** (owner checklist): the layered window on screen (`Window.Opacity` 0.3/1.0 legibility),
click-through, `DragMove`, the hotkey, move mode over a real desktop, the pulse/motion (captured at rest by design;
`--interactive` shows live pulses but pushes no status and is not evidence), `SizeToContent` behaviour over time
(jitter), real PresentMon/fan hardware strings, tray interaction, CPU cost.

## Non-goals

Per-metric graph toggles; a separate overlay history window or buffer (it shows the same ring buffer as the
dashboard); overlay bars/gauges; changing overlay colours (the sparkline borrows the value's brush); hover, tooltip
or crosshair on the overlay; a user-tunable sparkline size or sample window; a third, overlay-specific effects gate;
fan-conflict detection in the strip; a new `SettingsChange` member; changes to `Sparkline.cs`, `CurveRenderer.cs`,
`GraphStyle.cs`, `SampleAxis.cs`, `MetricTileViewModel.cs`, `Theme.xaml` or any converter; tray changes; new NuGet
packages (H.NotifyIcon stays 2.3.2).

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings (both `tests/Stats.Core.Tests` and
  `tests/Stats.UiPreview.Tests`; xUnit analyzers: constants in the `expected` slot).
- **Core tests** (`tests/Stats.Core.Tests`):
  - New `OverlayStatusComposerTests`: `Compose_NothingActive_ReturnsEmpty` (all off/null → `OverlayStatus.Empty`,
    `Text == ""`, `!IsWarning`); `Compose_FanEnabledWithProfile_ShowsFansProfile` → `"Fans: Balanced"`;
    `Compose_FanEnabledNoProfile_ShowsCustom` → `"Fans: Custom"`; `Compose_FanDisabled_OmitsFanSegment` (even with a
    `WriteFailed` status, and `!IsWarning`); `Compose_FanEnabledNoChannels_OmitsFanSegment`;
    `Compose_WriteFailed_AppendsSuffixAndWarns` → `"Fans: Balanced — write failed"`, `IsWarning`;
    `Compose_SourceUnavailable_AppendsSuffixAndWarns`; `Compose_WriteFailedWinsOverSourceUnavailable`;
    `Compose_GameMode_PassedThroughTrimmed`; `Compose_FrameReason_TrimsTrailingPeriodAndWarns`
    (`"PresentMon: access denied."` → `"PresentMon: access denied"`, `IsWarning`);
    `Compose_JoinsPresentSegmentsInFixedOrder` → `"Fans: Balanced · Game mode: desktop · PresentMon: access denied"`;
    `Compose_WhitespaceSegments_AreOmitted` (`"  "` game/frame strings).
  - `ViewModelTests` (`---- overlay ----`): `Overlay_ApplyLayout_ReadsSparklineAndStatusLineFlags`
    (`OverlayGraphs.None`/`OverlayStatusLine=true` → `ShowSparklines` false, `ShowStatusLine` true; flip both in
    settings, `ApplyLayout()`, flags follow); `Overlay_SetStatus_SameValue_RaisesNothing` (second identical
    `SetStatus` raises no `PropertyChanged`; a changed text raises `StatusText` and `HasStatus`);
    `Overlay_HasStatus_RequiresFlagAndText` (text without the flag → false; flag without text → false; both → true;
    `SetStatus(null)` → false); `Overlay_Rebuild_KeepsStatus`.
  - `SettingsViewModelTests`: `OverlayExtras_WriteThroughAndRaiseOverlay` (`OverlaySparklines=false` →
    `s.OverlayGraphs == None`; `OverlayStatusLine=true` → `s.OverlayStatusLine`; exactly 2 `SettingsChange.Overlay`
    raises and 2 saves); `Ctor_SeedsOverlaySparklinesFromEnum_WithoutRaising` (`OverlayGraphs.None` → `false`, no
    `Changed`). The existing `Overlay_PropsWriteThroughAndRaise` (4 raises) is left as is.
  - `SettingsServiceTests`: extend `Load_V1File_GetsDefaultsForNewFields` with `OverlayGraphs == Sparkline` and
    `OverlayStatusLine == false`; new theory `Load_BadOverlayGraphs_FallsBackToSparkline_KeepingOtherFields` over
    the JSON values `"Bars"`, `7`, `null`, `{}`, `[]` (each file also sets `"PollIntervalSeconds": 2.0` and
    `"OverlayStatusLine": true`, which must survive); `SaveThenLoad_RoundTripsOverlayGraphs_AsMemberName` (saved
    `None` → file text contains `"OverlayGraphs": "None"` → loads `None`; `OverlayStatusLine` round-trips).
- **Harness tests** (`tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs`, same shape as
  `GraphEffectsSubstateTests`): `SubstateCatalog_AcceptsTheNewOverlaySubstates` (theory over the five names) and
  `SubstateCatalog_RejectsThemForDashboard`; `NoSubstate_DefaultsSparklinesOnAndStatusLineOff`;
  `SparklinesOff_SetsNoneAndClearsVmFlag`; `StatusLine_ComposesInformationalText` (exact text, `!StatusIsWarning`,
  `HasStatus`); `StatusLineWarn_ComposesWarningText` (exact text, `StatusIsWarning`);
  `SparklinesWarmup_TrimsOverlayHistoryToOneQuarter` (15 of 60 for the first overlay metric);
  `VerticalPlusStatusLine_AppliesBoth`.
- **Captures**: the eleven `baseline.json` entries above render with empty sidecar `Warnings`; the reviewer (plan
  Task 4) captures the plain `--view overlay` on `feature/v1.10` (a worktree) and confirms
  `v13-overlay-sparklines-off-dark-amber.png` is pixel-identical to it; `docs/overlay-sparklines/EVIDENCE.md` lists
  every capture with its launch command and what it demonstrates.
- **Owner checklist** (interactive, cannot be automated from this shell — rules 7/8):
  - Sparklines beside values (horizontal) / below (vertical) on the real overlay over a game; stroke matches the
    value colour; Warn/Crit tint appears with the value's tint.
  - Pulse fires once per poll tick with "Glow and motion effects" on; off gives a static line; Task Manager CPU with
    the overlay visible and 3 metrics — effects on vs off — within ~1 % of each other and of v1.9.2.
  - `OverlayFontScale` 0.8 and 1.6: the sparkline scales with the text; line/glow thickness acceptable at 1.6.
  - Opacity 0.3 and 1.0: sparkline and strip stay legible over a light and a dark desktop.
  - Click-through on: no tooltip, the mouse passes through the sparkline area; click-through off: dragging by the
    sparkline area moves the overlay; move mode's dashed outline frames the strip; Esc exits.
  - Hotkey toggle unaffected; a freshly shown overlay carries a current status line (not a stale one).
  - Status accuracy: "Fans: …" matches the Fans window's active profile / Custom; disabling fan control removes the
    segment within one poll; game-mode transitions match the Fans window's Game mode status; with FPS selected and
    PresentMon failing (launch from the Start menu — rule 7) the PresentMon segment matches the dashboard's Game
    group status line.
  - Write-failed drill (other fan software holding the header): the strip shows "— write failed" with the glyph and
    the fans stay in the fail-safe Auto — the strip reports, never acts (rule 6).
  - The panel's width never changes when the strip appears or its text changes; only its height does.
  - Settings → Overlay: both checkboxes apply live and persist across restart; a v1.9.x `settings.json` loads with
    sparklines on and the strip off; with both off the overlay looks exactly as v1.9.2.
