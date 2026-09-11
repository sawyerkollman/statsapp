# T7 — secondary surfaces — report

Branch `ui-polish`, HEAD `247d4e1`. Build: `dotnet build --nologo` → 0 warnings, 0 errors. Tests:
`dotnet test --nologo` → 687 (Stats.Core.Tests) + 172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings.
Not committed/pushed per instructions.

## Files changed

- `src/Stats.App/Views/PeaksWindow.xaml`
- `src/Stats.App/Views/MetricDetailWindow.xaml`
- `src/Stats.App/Views/OverlayWindow.xaml`
- `src/Stats.App/Views/InputDialog.xaml`
- `src/Stats.App/Views/ThresholdDialog.xaml`

No code-behind changes were needed (`PeaksWindow.xaml.cs`, `ThresholdDialog.xaml.cs`, `InputDialog.xaml.cs`
untouched) — every change was achievable in markup. `Controls/HistoryChart.cs`, `Controls/Sparkline.cs`,
`DashboardWindow.xaml`, `TileTemplates.xaml`, `FansWindow.xaml(.cs)`, and all view models were not touched.

## Peaks / Alerts (`PeaksWindow.xaml`)

- Titles ("Session peaks" / "Alert log") → `FontSizeTitle` (20) SemiBold, was hardcoded 18 Bold.
- Toolbar buttons (Reset session / Copy / Clear) already used `HeaderButton`; spacing normalized to a
  consistent 8 units between the CheckBox/Copy/Reset session cluster (was 6/8 mixed).
- Column headers ("Metric"/"Now"/"Min"/"Avg"/"Max", "Time"/"Metric"/"Peak"/"Threshold"/"Duration") now use
  `FontSizeDense` (11, same value, now token-driven) instead of a hardcoded `FontSize="11"`.
- Numeric columns (Now/Min/Avg/Max, Peak/Threshold/Duration) are `TextAlignment="Right"` with
  `Typography.NumeralAlignment="Tabular"` added.
- Column widths: Metric is `Width="*" MinWidth="140"`; every numeric column is `Width="Auto" MinWidth="72"`.
  Header `Grid` and each row's `Grid` use identical literal `ColumnDefinition`s (no `SharedSizeGroup` needed —
  numeric cell content is short enough that both header and rows settle at the 72-unit floor, keeping columns
  visually aligned). Header + rows are wrapped together in one `ScrollViewer`
  (`HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"`) so a narrow window scrolls
  instead of crushing Metric — confirmed in the 480×240 capture (see below): a scrollbar appears (the vertical
  scrollbar's own width plus five 72-unit-minimum numeric columns exceeds the 480-wide viewport) and every
  metric name stays fully readable rather than being squeezed to a few pixels.
- Empty states: "No metrics selected yet — add some from the dashboard's Metrics panel" (Peaks) / "No alerts
  this session" (Alerts), `FontSizeBody` TextSecondary, centered, shown via a `DataTrigger` on `Rows.Count = 0`
  in the `TextBlock`'s own `Style` (default `Visibility="Collapsed"` in the style's base `Setter`, not as a
  local attribute — a local attribute would out-precedence the trigger, which is a bug I hit and fixed during
  this task; see "Bug found and fixed" below).
- Row content font size (Name/NowText/MinText/… and Time/Metric/Peak/…) changed from hardcoded `12` to the
  `FontSizeLabel` token (same value, now resource-driven).
- Byte-identical / untouched: `Copy_Click`, `ResetSessionCommand`, `ClearCommand`, `IncludeAll` binding,
  `CopyError`/`HasCopyError`, `MinAtText`/`MaxAtText` tooltips, `SeverityToBrush` bindings, alert row order and
  `DurationText` "ongoing" behavior, window size (640×480, min 480×240).

### Bug found and fixed

First pass set `Visibility="Collapsed"` as a local XAML attribute on the empty-state `TextBlock` alongside a
`Style` `DataTrigger` meant to flip it to `Visible` when `Rows.Count == 0`. Local property values always beat
style triggers in WPF precedence, so the message never appeared — confirmed by an early capture of
`peaks-empty`/`alerts-empty` showing a blank body under the header row. Fixed by moving the default
`Visibility="Collapsed"` into the `Style`'s own base `Setter` (before `Style.Triggers`) and removing the local
attribute; recaptured and confirmed the message now renders centered under the header.

## Details (`MetricDetailWindow.xaml`)

- Title → `FontSizeTitle` (20) SemiBold (was 18 Bold).
- Current reading → `FontSizeValue` (28) SemiBold in the severity brush (was 18 Bold). Per the task's explicit
  instruction, `CurrentText` is shown as-is (the VM only exposes the pre-formatted string; the unit is not
  split out).
- Summary line (`min … avg … max …` `Run`s) → `FontSizeLabel` (12, token instead of hardcoded) TextSecondary,
  with `Typography.NumeralAlignment="Tabular"` added on the containing `TextBlock` (inherits to the `Run`
  children).
- `HistoryChart` bindings (`Values`, `Unit`, `HoverTextProvider`, `SecondsPerSample`, `WarnValue`, `CritValue`,
  `TimeAxisLabels`, `YAxisLabels`, `Stroke`) are byte-for-byte unchanged. Window size unchanged (640×420, min
  480×300).

## Overlay (`OverlayWindow.xaml`)

Effectively a no-op as anticipated: added `Typography.NumeralAlignment="Tabular"` to the `CurrentText`
`TextBlock` only (the one change item 4 explicitly required regardless). Checked
`artifacts/ui-polish/after-t7/overlay-long-value.png` for blur — text renders crisp at this DPI, so the
optional `TextOptions.TextFormattingMode`/`TextRenderingMode` addition was skipped per the task's own
guidance. Verified via capture: `light-parent` still shows the fixed dark `#E01B1B1C` panel with pinned light
text regardless of the light theme; `move-mode` still shows the dashed `AccentBrush` outline; opacity handling
is untouched (not touched by this diff, no `opacity-min`/`opacity-max` capture regression expected since no
opacity-related markup changed). Pinned `OverlayTextPrimary/Secondary`/`OverlayWarn/Crit` resources and the
`IsMoveMode`/orientation/`FontScale` bindings are untouched.

## Dialogs (`InputDialog.xaml`, `ThresholdDialog.xaml`)

- Padding stayed at the existing `Margin="16"` on the root `StackPanel` (already matches the "consistent
  padding 16" requirement).
- Labels (`PromptText`, `WarnLabel`, `CritLabel`) → `FontSizeLabel` (12) TextSecondary via token instead of
  hardcoded `FontSize="12"`.
- `MetricNameText` → `FontSizeSection` (14) SemiBold via token (was hardcoded 14).
- `DirectionNote` → `FontSizeDense` (11) via token (was hardcoded 11), italic retained.
- Removed per-control `FontSize="13"` overrides on `Input`, `WarnBox`, `CritBox` — the implicit `TextBox`
  style already applies `FontSizeBody` (13), so the values were redundant.
- `ThresholdDialog`'s `ErrorText` now sits in a horizontal `StackPanel` with an `Icon.Warn` glyph
  (`IconGlyph` style, `Fill` overridden to `CritBrush`, 12×12) beside it; `ErrorText` itself is
  `FontSizeDense` (11) CritBrush. The whole row is collapsed when `ErrorText.Text == ""` via a `DataTrigger`
  on `{Binding Text, ElementName=ErrorText}` in the `StackPanel`'s own `Style` (no local `Visibility`
  attribute, learning applied from the Peaks bug above) — confirmed empty by default in the `valid`/
  `lower-is-worse` captures and visible with the icon in the `invalid` capture.
- Primary action OK → `PrimaryButton` in both dialogs; secondary Cancel/Clear stayed `HeaderButton`; 8-unit
  `Margin` added between buttons; `MinWidth` normalized to 72 everywhere (was 60/70 mixed).
- `IsDefault`/`IsCancel`, the `Loaded` focus-and-select-all handlers, `ThresholdDialog.Initialize`'s contract,
  and `Ok_Click`'s validate-and-keep-open-on-error behavior (via `ThresholdInput.TryParse`) are all untouched
  — no `.xaml.cs` edits were needed.

## Captures (`artifacts/ui-polish/after-t7/`), all 18 sidecars show `"Warnings": []`

| Capture | Dimensions | Notes |
|---|---|---|
| `peaks-populated` | 640×480 | Headers align with rows, numerics right-aligned, tabular figures |
| `peaks-populated-narrow` | 480×240 | Horizontal + vertical scrollbars appear; Metric names ("Tctl/Tdie", "CPU Total", "CPU Clock") fully readable, not crushed |
| `peaks-long-names` | 640×480 | Long metric names ellipsize with tooltip, numeric columns stay aligned |
| `peaks-empty` | 640×480 | Empty-state message centered under header (after bug fix) |
| `alerts-ongoing` | 640×480 | "ongoing" duration text intact, severity-colored Peak column |
| `alerts-empty` | 640×480 | Empty-state message centered under header (after bug fix) |
| `details-gap` | 640×420 | Title/value/summary typography updated, NaN-gap chart rendering unchanged |
| `details-thresholds-dark` / `-light` | 640×420 | Threshold guide lines, both themes |
| `details-long-unit` | 640×420 | Long unit value fits at 28pt without clipping |
| `threshold-dialog-valid-dark` / `-light` | 380×260 | Consistent padding/labels, `PrimaryButton` OK, error row collapsed |
| `threshold-dialog-invalid` | 380×260 | `Icon.Warn` glyph + `FontSizeDense` CritBrush error text visible |
| `threshold-dialog-lower-is-worse` | 380×260 | Checkbox path, `PrimaryButton` OK |
| `input-dialog` | 380×160 | `PrimaryButton` OK, `HeaderButton` Cancel, 8-unit gap, MinWidth 72 |
| `overlay-light-parent` | 400×200 | Fixed dark panel + pinned light text over a light theme host |
| `overlay-move-mode` | 400×200 | Dashed `AccentBrush` outline intact |
| `overlay-long-value` | 400×200 | Ellipsized `DisplayName`, tabular value, no visible blur |

Visual check performed by reading each PNG: headers align with rows in both tabs, numerics are right-aligned
and stable, Metric stays readable at 480 wide (scrolls instead of crushing), both empty states render, dialog
buttons/spacing/error treatment are consistent, `HistoryChart` guides/gaps/axes are visually unchanged from
the T1 baselines, and the overlay's pinned dark panel / light text / dashed move-mode outline are all intact.

## Pending owner checks (static-capture-only, needs the real running app)

- Peaks TSV clipboard `Copy_Click` — code path unchanged, but clipboard success/failure can't be exercised by
  a static screenshot.
- `HistoryChart` crosshair hover (value + time) — requires live mouse interaction, not available in the
  harness's static capture method.
- Overlay real hotkey toggle, click-through, and drag-to-move — the harness never constructs the global
  hotkey/tray/click-through services (see `SimulatedServices` in every sidecar JSON); `move-mode`'s dashed
  outline was confirmed visually, but actual mouse-drag repositioning needs the real app.
- `opacity-min`/`opacity-max` overlay substates were not recaptured for T7 (no overlay opacity markup was
  touched by this task, so no regression is expected, but they weren't in the required T7 capture list and
  were not re-verified here).
