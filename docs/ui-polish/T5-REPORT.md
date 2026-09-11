# T5 — settings organization — report

Branch `ui-polish`, base `4395d10`. Build: `dotnet build --nologo` → 0 warnings, 0 errors. Tests:
`dotnet test --nologo` → 687 (Stats.Core.Tests) + 172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings.
Not committed/pushed per instructions.

## Files changed

- `src/Stats.App/Views/DashboardWindow.xaml` — **only** the content inside `<TabItem Header="Settings">`
  (flyout chrome, Metrics tab, header, sections, notices untouched). The single long `StackPanel` is
  replaced with a nested `TabControl` (`x:Name="SettingsCategoryTabs"`) whose five `TabItem`s are
  Appearance / Monitoring / Alerts / Overlay / System, in that order. `DataContext="{Binding SettingsPanel}"`
  is set once, on the nested `TabControl` itself — not per category — so there is still exactly one
  `SettingsViewModel` and no duplicated controls. Each category body is its own
  `ScrollViewer > StackPanel` containing the existing controls moved verbatim. A new static header row
  (`SettingsLabel` "Warn" / "Crit", aligned to the grid's existing 110/46/66/16/66/* columns) was added
  above the thresholds `ItemsControl` in Monitoring per DESIGN.md §5 — no VM change.
- `src/Stats.App/Views/DashboardWindow.xaml.cs` — **no changes**. `HotkeyBox_PreviewKeyDown`,
  `HotkeyClear_Click` read `sender`/`Vm.SettingsPanel` directly (not the visual tree), and
  `FlyoutTabs.ItemContainerGenerator.ContainerFromIndex(vm.FlyoutTabIndex)` in
  `PickerFlyout_IsVisibleChanged` still addresses the outer (Metrics/Settings) `TabControl`, which is
  unaffected by nesting a second `TabControl` inside the Settings `TabItem`'s content.
- `tools/Stats.UiPreview/SubstateCatalog.cs` — added `category-appearance`, `category-monitoring`,
  `category-alerts`, `category-overlay`, `category-system` to the `settings` view's allowed substates.
- `tools/Stats.UiPreview/Views/CaptureHost.cs` — `category-<name>` substates are filtered out of
  `buildSubstates` (same treatment as `theme-cycle`) so they never reach
  `PreviewComposition.ApplySubstate`'s default-throw; a new `SelectSettingsCategory` helper finds
  `SettingsCategoryTabs` by `Name` via `VisualTreeUtil.FirstDescendant` and sets `SelectedIndex` from a
  fixed `appearance/monitoring/alerts/overlay/system` order array (throws a clear error for an unknown
  category name, same pattern as `SubstateCatalog.Validate`). Runs in `ApplyVisualSubstates`, so it composes
  with any other `+`-joined substate (e.g. `invalid-threshold+category-monitoring`) — `CaptureSpec` already
  supported `+`-joining before this task.
- `docs/ui-polish/T5-REPORT.md` — this report.

No edits to `Stats.Core`, `DashboardViewModel.cs`, `SettingsViewModel.cs`, `TileTemplates.xaml`,
`CoreMatrixView.xaml`, or any T2-owned resource dictionary. No new view-model classes, no Save/Cancel model,
no test additions (no new navigation *logic* was introduced — category selection is nested `TabControl`
built-in `SelectedIndex` state, nothing to unit-test beyond what WPF itself guarantees).

## Selector choice and narrow-width result

Built the nested `TabControl` first (the design's stated default option) and verified wrapping at
860×600 / ui-scale 1.3 before considering the `ComboBox` fallback. **Result: no fallback needed.** At
860×600/1.3×, all five headers (Appearance, Monitoring, Alerts, Overlay, System) render on a single
`TabPanel` row with no clipping or truncation — see `settings-narrow-appearance.png` and
`settings-narrow-system.png`. The flyout's own `MaxWidth` binding (T4's `ElementName=ScaledRoot` clamp) and
short category-header text keep total header width well under the available flyout width even at the
narrowest required size, so the wrapping behavior was never exercised at the required test point — headers
simply fit. The `ComboBox`-selector option (and the code to build it) was not written, per "verify... if
they clip, fall back... do not build both."

## Binding parity audit (26 rows, from T4-REPORT.md's inventory)

Verified by extracting and diffing the binding/command/click-bearing lines of the old flat list against the
new categorized tree (`git diff` + a normalized line-set `comm` on the two blocks, ignoring pure
structural/wrapper lines). **All 26 rows' binding/command/click text is byte-for-byte unchanged** — the only
non-structural diffs were (a) four section-heading `TextBlock`s (Alerts, Hardware, Overlay, Theme — each now
the first heading in its category) gained the same `Margin="0,0,0,6"` the old list's very first heading
("Polling") already had, for consistent top-spacing under each category's tab strip, and (b) the new static
Warn/Crit header row (no binding, see above). Neither touches any of the 26 rows' `{Binding …}` / `Command=`
/ `Click=` text.

| # | Section header | Binding / Command / Click | Category | Binding text unchanged |
|---|---|---|---|---|
| 1 | Polling | `PollIntervalSeconds` (Slider + label) | Monitoring | Yes |
| 2 | Dashboard (UI scale) | `DashboardUiScale` (Slider + label) | Appearance | Yes |
| 3 | History window | `HistoryWindowMinutes` (4× `Equals` RadioButton) | Monitoring | Yes |
| 4 | Thresholds (warn/crit) | `ThresholdRuleItems` (`GroupName`, `Unit`, `WarnText`, `CritText`, `DirectionText`, `Error`) | Monitoring | Yes |
| 5 | Thresholds (add row) | `HasAddableRulePairs`, `AddableRulePairs`, `SelectedAddablePair`, `AddRuleCommand` | Monitoring | Yes |
| 6 | Limits | `LimitItems` (`ValueText`, `IsInvalid`, `Label`) | Monitoring | Yes |
| 7 | Overlay (orientation) | `OverlayIsVertical` (2× RadioButton) | Overlay | Yes |
| 8 | Overlay (font scale) | `OverlayFontScale` (Slider + label) | Overlay | Yes |
| 9 | Overlay (opacity) | `OverlayOpacity` (Slider + label) | Overlay | Yes |
| 10 | Overlay (click-through) | `OverlayClickThrough` (CheckBox) | Overlay | Yes |
| 11 | Overlay (hotkey) | `OverlayHotkey` (TextBox, OneWay) + `HotkeyClear_Click` + `HotkeyBox_PreviewKeyDown` | Overlay | Yes |
| 12 | Overlay (status) | `HotkeyStatus` (TextBlock) | Overlay | Yes |
| 13 | Overlay (reset) | `ResetOverlayPositionCommand` (Button) | Overlay | Yes |
| 14 | Dashboard (core matrix) | `ShowCoreMatrix` (CheckBox) | Appearance | Yes |
| 15 | Tray | `TrayMetricOptions` / `SelectedTrayMetric` (ComboBox) | Monitoring | Yes |
| 16 | Alerts (enabled) | `AlertsEnabled` (CheckBox) | Alerts | Yes |
| 17 | Alerts (hold time) | `AlertHoldSeconds` (Slider + label) | Alerts | Yes |
| 18 | Alerts (sound) | `AlertSoundEnabled` (CheckBox) | Alerts | Yes |
| 19 | Theme (preset) | `ThemePresetNames` / `SelectedThemePreset` (ComboBox) | Appearance | Yes |
| 20 | Theme (accent swatches) | `AccentSwatches` + `SetAccentCommand` | Appearance | Yes |
| 21 | Theme (accent hex/reset) | `ResetAccentCommand`, `AccentHex`, `IsAccentInvalid` | Appearance | Yes |
| 22 | Hardware | `ReadMotherboardAndCoolers`, `RestartNowCommand`, `HardwareStatus`, `RestartError` | System | Yes |
| 23 | Startup | `StartupEnabled`, `StartupBusy`, `StartupError` | System | Yes |
| 24 | Updates | `CheckForUpdatesAutomatically` (CheckBox) | System | Yes |
| 25 | Diagnostics | `OpenLogFolderCommand`, `DiagnosticsError` | System | Yes |
| 26 | About | `AppVersionDisplay`, `IsDevBuild`, `CheckForUpdatesCommand`, `UpdateCheckBusy`, `UpdateCheckResult`, `UpdateCheckFailed` | System | Yes |

All 26 rows have a home; nothing dropped, nothing duplicated. Category order within each tab follows the
task's specified order (e.g. Appearance = Theme → UI scale → core matrix, not document order), per the
"Category contents" list in the task brief.

## Captures (`artifacts/ui-polish/after-t5/`), all 14 sidecars show `"Warnings": []`

| Capture | Command | Notes |
|---|---|---|
| `settings-category-appearance.png` | `--width 1180 --height 720 --substate category-appearance` | Theme/accent/UI scale/core matrix all visible, accent underline on Appearance tab |
| `settings-category-monitoring.png` | same, `category-monitoring` | Polling/History/6 threshold rows+add-row/(Limits/Tray below fold, scrollable) |
| `settings-category-alerts.png` | same, `category-alerts` | All 3 alert controls |
| `settings-category-overlay.png` | same, `category-overlay` | Orientation/font/opacity/click-through/hotkey `Ctrl+Shift+O`/reset |
| `settings-category-system.png` | same, `category-system` | Hardware/Startup/Updates/Diagnostics/About, version `v0.0.0-preview` |
| `settings-narrow-appearance.png` | `--width 860 --height 600 --ui-scale 1.3 --substate category-appearance` | All 5 headers fit one row, no clipping |
| `settings-narrow-system.png` | same, `category-system` | All 5 headers fit one row, no clipping |
| `settings-category-monitoring-light.png` | `--theme Light --substate category-monitoring` | Light palette, no stale dark colors |
| `settings-invalid-threshold.png` | `--substate invalid-threshold+category-monitoring` | CPU % Warn field red border + "Warn must be a number" |
| `settings-invalid-limit.png` | `--substate invalid-limit+category-monitoring --height 900` | Package Power field red border (height raised to clear the scroll fold — see note below) |
| `settings-invalid-hotkey.png` | `--substate invalid-hotkey+category-overlay` | Hotkey shows `Q`, "Invalid hotkey" warning text |
| `settings-restart-required.png` | `--substate restart-required+category-system` | "Restart Stats to apply" next to Restart now |
| `settings-startup-error.png` | `--substate startup-error+category-system` | "Could not check startup status…" in red |
| `settings-update-error.png` | `--substate update-error+category-system` | About section shows "Update check failed…" in red |

All 14 read back with no clipped controls, no missing-resource fallback boxes, and correct
theme/category/error placement. `+`-joined combos work exactly as `CaptureSpec.SubstateList`'s existing
`Split('+', …)` already implied.

**Deviation:** `settings-invalid-limit.png` used `--height 900` instead of the base `720` — at 720 the
Limits section (below Thresholds + Add-rule in Monitoring) sits past the `ScrollViewer`'s fold, so the
invalid `Package Power` field wasn't visible in a static, non-scrolling capture. This mirrors T2's own
precedent for the same substate under the old flat layout (`height=893`). The real flyout at 720 is still
fully usable — the field is present and reachable by scrolling; only the *screenshot* needed the extra
height to show it without a scroll action, which the harness cannot simulate mid-capture.

## Pending owner checks

- **Keyboard tab navigation between categories** — arrow-key / Ctrl+Tab navigation across the nested
  `TabControl`'s `TabItem`s uses WPF's stock `TabControl` keyboard handling (unchanged, no custom
  `KeyDown` intercepts it). Not exercised by static capture; needs an interactive check on real hardware
  per `CLAUDE.md` rule 7/8.
- **Focus-loss commit of a pending edit when switching categories** — every threshold/limit `TextBox` uses
  `UpdateSourceTrigger=LostFocus`. Clicking another category tab moves keyboard focus off the `TextBox`,
  which WPF resolves as a `LostFocus` before the new tab's content becomes the active element, so a pending
  edit commits correctly (same mechanism DESIGN.md's "avoid resetting values... when navigating categories"
  requires, and the same trigger already in use before this task — T5 did not change any
  `UpdateSourceTrigger`). Confirmed by code inspection, not by an interactive test — static captures cannot
  drive a real focus-change-mid-edit sequence.
- **Restart-flag / update-progress live behavior** — `restart-required`, `startup-error`, and `update-error`
  substates all render correctly as one-shot static states (see captures above); the *live* transition (e.g.
  a real hardware-restart flow, or `UpdateCheckBusy` flipping mid-check) is static-capture-only and needs an
  owner check on real hardware, same limitation already recorded in T2/T4's reports.
