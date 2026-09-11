# T4 — dashboard shell and picker — report

Branch: worktree based on `ui-polish` @ `99effbc` (fast-forwarded from a stale `master`-tip checkout before
starting). Build: `dotnet build --nologo` → 0 warnings, 0 errors. Tests: `dotnet test --nologo` →
677 (Stats.Core.Tests, +3 new `IsEmpty` tests) + 172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings.

## Files changed

- `src/Stats.App/Views/DashboardWindow.xaml` — header regrouped into a title + wrapping action area (View /
  Overlay·Fans·Peaks / Metrics·Settings), all four notice banners restyled to a consistent icon+text+actions
  layout, section headers/content re-spaced to the 16/24/8 inset rule with a warn glyph on the status line,
  empty state, flyout title row + close button + `MaxWidth` binding, Metrics tab search box placeholder/clear
  affordance + no-results panel. Settings `TabItem` content is byte-for-byte unchanged except it now sits
  inside the new title-row `DockPanel`/`TabControl` wrapper (the "flyout-chrome change" the task scoped in).
- `src/Stats.App/Views/DashboardWindow.xaml.cs` — `_pickerOpener`/`_pickerView` fields; `ViewButton_Click`
  (code-behind `ContextMenu` for Collapse/Expand all, same pattern as the existing tile menu builder);
  `HeaderOpener_Click` (records which header button opened the flyout); `PickerClose_Click`,
  `PickerFlyout_KeyDown` (Escape), `PickerFlyout_IsVisibleChanged` (focus-in); `PickerFilterClear_Click`;
  `UpdatePickerNoResults()` wired into the existing `DataContextChanged`/`PropertyChanged` picker-filter
  plumbing. Every existing drag/drop/context-menu/hotkey handler is untouched.
- `src/Stats.Core/ViewModels/DashboardViewModel.cs` — added `IsOverlayVisible` (`[ObservableProperty]`) and
  `IsEmpty` (computed `Sections.Count == 0`, raised via `OnPropertyChanged` at the end of `RebuildSections`).
  No other members touched.
- `src/Stats.App/App.xaml.cs` — one added line next to the existing `_overlay.IsVisibleChanged` subscription:
  `_overlay.IsVisibleChanged += (_, e) => _dashboardVm.IsOverlayVisible = e.NewValue is true;`
- `tests/Stats.Core.Tests/DashboardViewModelTests.cs` — three new tests: `IsEmpty_TrueWhenNoSectionsAndNoCoreMatrix`,
  `IsEmpty_FalseWhenAnySectionExists`, `IsEmpty_UpdatesAfterRemovingLastTile`.
- `artifacts/ui-polish/after-t4/*.png` (+`.json` sidecars) — 18 verification captures (13 requested + 5
  theme-cycle extra frames), all with `"Warnings": []`.
- `docs/ui-polish/T4-REPORT.md` — this report.

No edits to `TileTemplates.xaml`, `CoreMatrixView.xaml`, `TileSizeToLengthConverter.cs`,
`MetricTileViewModel.cs`, `ValueFormatter.cs`, or any T2-owned resource dictionary (`Theme.xaml`,
`Controls.xaml`, `Icons.xaml`, `AppStyles.xaml`) — all new visual state (`OverlayHeaderButton` style) is a
local `Window.Resources` entry in `DashboardWindow.xaml`, `BasedOn` the frozen `HeaderButton`.

## Header

Two `StackPanel` groups (`Margin="16,0,0,0"` between them) plus a standalone **View** button, all right-aligned
inside a `WrapPanel` so the action row wraps to a second line at narrow widths instead of truncating (title
stays docked left). Each button is `IconGlyph` Path + `FontSizeBody` label inside a horizontal `StackPanel`
(8-unit gap), `HeaderButton` style, explicit `AutomationProperties.Name`. The **View** button opens a
code-behind-built `ContextMenu` (`Placement=Bottom`) with Collapse all / Expand all wired directly to
`vm.CollapseAllCommand`/`ExpandAllCommand` — the same "build in code-behind, don't rely on ContextMenu
DataContext inheritance" pattern the existing tile context menu already uses. Overlay's on/off state
(`IsOverlayVisible`) drives a `DataTrigger` on a local `OverlayHeaderButton` style: a persistent 2-unit
`AccentBrush` bottom border on the existing neutral surface, plus a `ToolTip` that reads "Overlay on/off".
Window `MinWidth="860" MinHeight="600"` set as required; default size unchanged (1180×720).

## Sections

Header row unchanged in structure (chevron is the existing `StatsExpanderToggle`, still fully functional) but
restyled: count uses `FontSizeLabel`, the status line gets an `Icon.Warn` glyph (`WarnBrush` fill) before the
text at `FontSizeDense`. The status `StackPanel` lives in `Expander.Header`, not the collapsible content, so a
collapsed group still shows it — verified visually in `dashboard-collapsed-all.png` (no groups have status in
the `normal` fixture, but the structural placement is unchanged from before this task and was already
correct) and `dashboard-missing.png` (sensor-health/degraded banners, which are the analogous "status visible
regardless of section state" case, both render). Section separation is `Margin="0,24,0,0"` per `Expander`;
main content inset is `Margin="16,16,16,16"` on the sections `ItemsControl`; header-to-content gap is
`Margin="20,8,0,0"` on the content `StackPanel` — the 20-unit left value (not 16) is deliberate: it lines tile
content up with the header's group-name text, which itself sits past the `Expander`'s padding plus the
chevron column (see the inline XAML comment).

## Notices

All four banners now share one shape: a 16-unit icon on the left (`Icon.Warn` for degraded/sensor-health,
`Icon.Info` for the update and FPS-hint banners), body text at `FontSizeBody`, right-docked actions
(`HeaderButton`, "Update now" promoted to `PrimaryButton`), `Padding="12,8"`. Degraded/sensor-health keep
their fixed `#7A2D2D`/`#6B4E12` backgrounds and white text/icon (the icon's `Fill` is a local `"White"`
override on top of the shared `IconGlyph` style, which otherwise defaults to theme-tinted `TextPrimary`) —
per DESIGN.md, these two are deliberately not theme-tinted. The degraded banner's text is now two
`TextBlock`s: the original first sentence as an `FontSizeBody` summary, the PawnIO install instructions as a
second, always-visible `FontSizeDense` line (never hidden behind an expander — DESIGN.md explicitly forbids
hiding actionable text). Every existing binding (`UpdateAvailable`, `UpdateBusy`, `UpdateProgress`,
`UpdateReleasePageUrl`/hyperlink, `ReleasePageError`, `IsDegraded`, `SensorHealthWarningVisible`/
`SensorHealthNotice`, `ShowFpsHint`, and all five commands) is unchanged.

## Empty state

`IsEmpty` (new) hides the sections `ScrollViewer` (`InverseBoolToVis`) and shows a centered `StackPanel`:
"No metrics on the dashboard" (`GroupHeader` style → `FontSizeSection`), a one-line hint, and an **Open
Metrics** `PrimaryButton` bound to the existing `TogglePickerCommand`. No fabricated values anywhere — the
`empty` scenario capture shows only the banner/header/empty-state, no tiles. Verified interacting correctly
with other banners simultaneously (the FPS hint banner and the empty state both render together in
`dashboard-empty.png`, since the `empty` fixture also qualifies for the FPS hint).

## Flyout

`PickerFlyout` is `Width="480"`, `MaxWidth` bound via `ElementName=ScaledRoot` to the scaled content Grid's
`ActualWidth` (so it can't overflow the window at 860×600/scale 1.3 — verified in
`settings-narrow-scale13.png`/`picker-narrow-scale13.png`, both fully on-screen). A new title row
("Metrics & Settings" + a `Icon.Close` `HeaderButton`, `AutomationProperties.Name="Close"`) sits above the
existing `TabControl`, which still owns tab selection. The Settings `TabItem`'s own content is untouched.

**Escape/focus-return logic:** `PickerFlyout_KeyDown` is a bubbling `KeyDown` handler on the flyout `Border`.
It only acts when `e.Key == Escape` reaches it un-handled — an open `ComboBox` dropdown (or any other child
popup/editor) consumes Escape itself first, per WPF's built-in behavior, so this handler never fires while a
dropdown is open. When it does fire, it sets `IsPickerOpen = false` and focuses `_pickerOpener` (the header
button — Metrics or Settings — that most recently raised `HeaderOpener_Click`), falling back to `MetricsButton`
if the flyout was opened another way (currently only the empty-state's "Open Metrics" button, which uses
`TogglePickerCommand` directly with no `Click` handler). The explicit **Close** button goes through the same
fallback-focus code path (`PickerClose_Click`).

**Open-focus logic:** `PickerFlyout_IsVisibleChanged` fires when the flyout becomes visible and, one dispatcher
pass later (`DispatcherPriority.Loaded`, so the now-visible content actually exists to receive focus), focuses
`PickerFilterBox` if `FlyoutTabIndex == 0` (Metrics) or the selected tab's `TabItem` container via
`FlyoutTabs.ItemContainerGenerator.ContainerFromIndex` otherwise (Settings).

**What static capture could not exercise (pending owner checks on real hardware):**
- Actual keyboard-driven Escape/focus-return and open-focus behavior — the harness only captures static
  frames; no capture drives a real `KeyDown` or verifies `Keyboard.FocusedElement` after the fact. The logic
  was verified by code inspection and by the existing `HotkeyBox_PreviewKeyDown` Escape-handling precedent in
  the same file, not by an interactive test.
- Hover/pressed visuals on the new header buttons (same limitation the T2 report recorded for `HeaderButton`
  generally — no interactive/mouse-driven capture exists in the harness).
- `IsOverlayVisible`/the Overlay button's persistent accent-border indicator: the harness's `OverlayWindow`
  and `DashboardWindow` are built as two independent windows from two independent `DataContext`s
  (`PreviewComposition.Overlay` vs. `.Dashboard`) with no `IsVisibleChanged` wiring between them — that wiring
  only exists in the real `App.xaml.cs` composition root, which the preview harness never constructs. So no
  capture in this run shows the indicator in its "on" state; it was verified by reading the `DataTrigger` XAML
  and confirming the `[ObservableProperty] _isOverlayVisible` plumbing compiles and the one new `App.xaml.cs`
  line is wired to the same event as the existing overlay-visibility handler.

## Metrics tab

Search box gets a left-aligned `Icon.Search` glyph and a placeholder `TextBlock` ("Search sensors, names,
hardware") collapsed via a `DataTrigger` on `PickerFilter == ""`; a `Icon.Clear` `HeaderButton` at the right
edge (visible only when `PickerFilter` is non-empty) calls `PickerFilterClear_Click`, which sets
`vm.PickerFilter = ""` — same effect as typing it out, so search semantics (`PickerMatches`) are untouched. A
new no-results panel ("No sensors match" + "Clear search") sits behind the `ScrollViewer` in a `Grid` overlay;
`UpdatePickerNoResults()` toggles its `Visibility` from `ListCollectionView.IsEmpty`, called once right after
`PickerList.ItemsSource` is set and again on every `PickerFilter` change (same place the existing
`pickerView.Refresh()` call already lived). Column headers, the live "Now" column, and the group All/None
buttons (`PickerGroupAll_Click`/`PickerGroupNone_Click`, `Tag`-based scope, `SelectAllInGroup` unchanged) are
byte-for-byte the same as before.

## Settings binding inventory (for T5)

Every `{Binding …}` / `Command=` / `Click=` inside the Settings `TabItem`, in document order, mapped to its
DESIGN.md §5 category:

| Section header | Binding / Command / Click | DESIGN §5 category |
|---|---|---|
| Polling | `PollIntervalSeconds` (Slider + label) | Monitoring |
| Dashboard (1st) | `DashboardUiScale` (Slider + label) | Appearance |
| History window | `HistoryWindowMinutes` (4× `Equals` RadioButton) | Monitoring |
| Thresholds (warn / crit) | `ThresholdRuleItems` (ItemsControl: `GroupName`, `Unit`, `WarnText`, `CritText`, `DirectionText`, `Error`) | Monitoring |
| Thresholds (add row) | `HasAddableRulePairs`, `AddableRulePairs`, `SelectedAddablePair`, `AddRuleCommand` | Monitoring |
| Limits (for % of limit / gauges) | `LimitItems` (ItemsControl: `ValueText`, `IsInvalid`, `Label`) | Monitoring |
| Overlay | `OverlayIsVertical` (2× RadioButton) | Overlay |
| Overlay | `OverlayFontScale` (Slider + label) | Overlay |
| Overlay | `OverlayOpacity` (Slider + label) | Overlay |
| Overlay | `OverlayClickThrough` (CheckBox) | Overlay |
| Overlay | `OverlayHotkey` (TextBox, `Mode=OneWay`) + `HotkeyClear_Click` + `HotkeyBox_PreviewKeyDown` | Overlay |
| Overlay | `HotkeyStatus` (TextBlock) | Overlay |
| Overlay | `ResetOverlayPositionCommand` (Button) | Overlay |
| Dashboard (2nd) | `ShowCoreMatrix` (CheckBox) | Appearance |
| Tray | `TrayMetricOptions` / `SelectedTrayMetric` (ComboBox) | Monitoring |
| Alerts | `AlertsEnabled` (CheckBox) | Alerts |
| Alerts | `AlertHoldSeconds` (Slider + label) | Alerts |
| Alerts | `AlertSoundEnabled` (CheckBox) | Alerts |
| Theme | `ThemePresetNames` / `SelectedThemePreset` (ComboBox) | Appearance |
| Theme (accent) | `AccentSwatches` (ItemsControl) + `SetAccentCommand` (per-swatch Button) | Appearance |
| Theme (accent) | `ResetAccentCommand` (Button), `AccentHex` (TextBox), `IsAccentInvalid` (DataTrigger) | Appearance |
| Hardware | `ReadMotherboardAndCoolers` (CheckBox) | System |
| Hardware | `RestartNowCommand` (Button), `HardwareStatus`, `RestartError` | System |
| Startup | `StartupEnabled`, `StartupBusy` (CheckBox + IsEnabled), `StartupError` | System |
| Updates | `CheckForUpdatesAutomatically` (CheckBox) | System |
| Diagnostics | `OpenLogFolderCommand` (Button), `DiagnosticsError` | System |
| About | `AppVersionDisplay`, `IsDevBuild`, `CheckForUpdatesCommand`, `UpdateCheckBusy`, `UpdateCheckResult`, `UpdateCheckFailed` | System |

All 26 rows are covered by the five DESIGN.md §5 categories with no leftovers; the two "Dashboard" section
headers in the current flat list (`UI scale` and `Show CPU core matrix`) both map to categories other than a
literal "Dashboard" bucket (Appearance and Appearance respectively) since DESIGN.md's table has no Dashboard
category of its own — T5 should fold both into Appearance. This inventory is read-only (T4 did not restructure
the Settings tab); T5 owns the actual category extraction.

## Captures (`artifacts/ui-polish/after-t4/`), all 18 sidecars show `"Warnings": []`

| Capture | Dimensions | Notes |
|---|---|---|
| `dashboard-normal.png` | 1166×713 | Header groups, section spacing, tile alignment all verified by inspection |
| `dashboard-narrow-scale13.png` | 846×593 | 860×600 @ scale 1.3 — header fits one row, no clipping |
| `picker-narrow-scale13.png` | 846×593 | Flyout fully on-screen at narrow width + scale |
| `settings-narrow-scale13.png` | 846×593 | Flyout fully on-screen; Settings content unaffected |
| `dashboard-missing.png` | 1166×713 | Degraded (two-line) + sensor-health banners both render; missing values show em dash, never 0 |
| `dashboard-empty.png` | 1166×713 | Empty state centered, ScrollViewer hidden, FPS hint banner shown simultaneously, no fake values |
| `dashboard-collapsed-all.png` | 1166×713 | All 6 sections collapsed, counts visible, chevrons point right |
| `dashboard-update-offered.png` | 1166×713 | Icon+text+What's new+Later+Update now (PrimaryButton) all on one row |
| `dashboard-update-progress.png` | 1166×713 | Progress bar in place of What's new; Later/Update now disabled |
| `dashboard-fps-hint.png` | 1166×713 | Icon.Info + updated copy + Got it |
| `picker-no-results.png` | 1166×713 | No-results panel + Clear search shown, ScrollViewer content empty behind it |
| `picker-filtered.png` | 1166×713 | Filtered to "cpu", group headers/All/None/live Now column intact |
| `dashboard-theme-cycle-{0..5}` (Amber/Blue/Green/Purple/Light/custom-accent) | 1166×713 | Every frame fully repainted; header icons/text follow theme; no stale amber on Light/custom |

**Not exercised by static capture** (see Flyout section above for detail): keyboard Escape/focus-return and
open-focus behavior; hover/pressed states on the new header buttons; the Overlay button's on-state accent
indicator (harness has no cross-window `IsVisibleChanged` wiring between its independent Overlay/Dashboard
compositions). All three are implementation-complete and code-reviewed but need an owner check on real
hardware per `CLAUDE.md` rule 7/8 (this shell cannot launch the real app for an interactive smoke test).

## Commit

One commit in this worktree, message starting `feat(app): dashboard shell, notices, and picker polish (T4)`.
Not pushed. Commit hash: see the end of this session's tool output / `git log -1` in this worktree.
