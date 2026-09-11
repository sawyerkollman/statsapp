# T3 — tiles and core matrix — report

Branch `ui-polish`, base `99effbc`. Build: `dotnet build --nologo` → 0 warnings, 0 errors. Tests:
`dotnet test --nologo` → 684 (Stats.Core.Tests) + 172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings.
Not committed/pushed per instructions.

## Files changed

- `src/Stats.Core/Metrics/ValueFormatter.cs` — added `FormatParts(MetricDefinition, float?) → (string Value, string Unit)`; `Format` now composes from `FormatParts` so the two can never drift.
- `src/Stats.Core/ViewModels/MetricTileViewModel.cs` — two new `[ObservableProperty]` strings `ValueText`/`UnitText`, set from `ValueFormatter.FormatParts` in `Refresh()` alongside the unchanged `CurrentText`.
- `src/Stats.App/Converters/TileSizeToLengthConverter.cs` — S 150×70→160×80, M 215×120→224×144, L 440×160→460×192.
- `src/Stats.App/Views/TileTemplates.xaml` — rewritten: `TileBorder` now uses `RadiusPanel`/Padding 12; new `TileUnit` and `TileOptionalText` styles; `TileName`/`TileFoot` moved onto `FontSizeLabel`/`FontSizeDense` tokens; `TileValue` moved onto `FontSizeValue` + `Typography.NumeralAlignment="Tabular"`; `TileMenuButtonStyle` content is now an `Icon.More` `Path` (via `ContentTemplate`, not a shared instance) with an explicit `AutomationProperties.Name`; new `TileGaugeArcBox` style (56/88 arc box by `Size`); every template's row 1 is a `Grid` with a reserved `24`-wide last column for the menu button; every kind rebuilt per the shared content order (below).
- `src/Stats.App/Views/CoreMatrixView.xaml` — outer panel `CornerRadius="{StaticResource RadiusPanel}"`, `Padding="12"`, label at `FontSizeLabel`; cell grown 56×50→60×54; Index/Clock/Temp text raised from 9 to `FontSizeDense` (11); `LoadText` (13, unchanged), `HeatToBrush`/`HeatToForeground`/`Severity` bindings untouched.
- `tests/Stats.Core.Tests/ValueFormatterTests.cs` — added `FormatParts` coverage: B/s at 512/12,400/12,400,000, a °C F1 value, a unit-less definition, null→dash/empty-unit, and a `Format` composes-from-`FormatParts` check.
- `tests/Stats.Core.Tests/MetricTileViewModelTests.cs` — **new**. `ValueText`/`UnitText`/`CurrentText` agreement for a plain temperature reading, a B/s auto-scaled reading, and the missing-value case.

No changes to `DashboardWindow.xaml(.cs)` or `DashboardViewModel.cs` (owned by the concurrent agent).

## Sizes

Converter: S 160×80, M 224×144, L 460×192 (L = 224×2 + 12 gap = 460, matches DESIGN §2 exactly). No deviation from the DESIGN targets.

## Per-template layout summary

Every template's header row is a `Grid` with columns `*` (label) [+ `Auto` for the sparkline's period tag] + a fixed `24`-wide last column holding the (initially collapsed) `TileMenuButton`. Because that column has a fixed width rather than `Auto`, showing the button never shifts the label or reflows the row.

- **Compact (S)**: label row + one value row (`ValueText` at `FontSizeValueCompact` + `UnitText` + severity glyph). No chart/footer.
- **Sparkline**: header (label, `HistoryWindowTag` "2m", reserved menu column) → value row → optional `LimitText` → `Sparkline` control filling remaining height → optional `MinMaxText` footer pinned to the bottom edge.
- **Gauge**: header → `Grid` with the arc (`TileGaugeArcBox`, 56 units at M / 88 at L) in column 0 and, in column 1, the value at the **standard** `FontSizeValue` (28) now **outside** the arc (previously 13pt wrapped text inside it), `UnitText`, severity glyph, optional `LimitText`, optional "max …" line → optional `MinMaxText` footer.
- **Bar**: header → value row → track row (`LevelBar`, "0" always shown + optional `MaxText`) → optional `MinMaxText` footer.
- **Value**: header → prominent value row (fills remaining vertical space, centered) → optional `LimitText` → optional `MinMaxText` footer.

Empty optional rows (`LimitText`/`MinMaxText`/`MaxText` when `==""`) collapse via the shared `TileOptionalText` style's `DataTrigger` on `Text=""` (bound to `RelativeSource Self`); the one formatted case (Gauge's "max {0}" line, which is never itself `""`) gets an inline override style that triggers directly off the raw `MaxText` VM property instead. `LimitText` moved from `AccentBrush`/11 to `TextSecondary`/`FontSizeDense`, matching DESIGN §2's "accent reserved for controls/focus/traces."

The severity glyph stays the existing VM-supplied text glyph (▲/‼) — not replaced with `Icon.Warn`/`Icon.Crit` paths, since doing so would add code without changing behavior or fixing a defect (spec allowed this as optional, "only if not more code").

## Visible-tile count

`normal` scenario, 1180×720, Dark Amber: **8 fully visible medium tiles** (4 in the CPU row: Tctl/Tide, CPU Total, CPU Clock, Package Power; 3 in the GPU row: GPU Core, GPU Load, GPU Power; 1 in Memory: Memory). Identical to the stated baseline of 8 — **no reduction**, so no DESIGN §8 explanation is required.

## Tests added

- `ValueFormatterTests`: `FormatParts_Throughput_AutoScalesValueAndUnit` (512→"512"/"B/s", 12,400→"12.4"/"KB/s", 12,400,000→"12.4"/"MB/s"), `FormatParts_Temperature_SplitsValueAndUnit`, `FormatParts_UnitlessDefinition_ReturnsEmptyUnit`, `FormatParts_Null_ReturnsDashAndEmptyUnit`, `Format_ComposesFromFormatParts_SoTheyCannotDrift`.
- `MetricTileViewModelTests` (new file): `Refresh_ValueTextAndUnitText_AgreeWithCurrentText`, `Refresh_Throughput_ValueTextAndUnitText_AutoScale`, `Refresh_MissingValue_ValueTextIsDash_UnitTextEmpty`.
- All pre-existing `ValueFormatterTests`/`ViewModelTests` tile tests pass unchanged (`CurrentText` semantics untouched).

## Captures (`artifacts/ui-polish/after-t3/`), all sidecars show `"Warnings": []`

| Capture | Requested | Actual |
|---|---|---|
| `gallery-dark-amber` | 1180×1000, Dark Amber | 1166×993 |
| `gallery-light` | 1180×1000, Light | 1166×993 |
| `normal-dark-amber` | 1180×720, Dark Amber | 1166×713 |
| `dense-dark-amber` | 1180×720, Dark Amber | 1166×713 |
| `dense-dark-amber-scale09` | 1180×720, Dark Amber, ui-scale 0.9 | 1166×713 |
| `thresholds` | 1180×720, Dark Amber | 1166×713 |
| `missing` | 1180×720, Dark Amber | 1166×713 |
| `normal-tile-menu` | 1180×720, Dark Amber, `--substate tile-menu` | 1166×713 |

(The requested→actual width/height gap is the harness's own window-chrome/DPI accounting, unrelated to this task — consistent with the T2 report's captures.)

Visual inspection of every PNG:

- No clipping of value/unit/glyph/footer at any observed size for long readings: "4850 MHz" (Sparkline M), "1.187 V" (Value M), "162.4 W" / "84.0 A" (Gauge/Bar M/L), "100 %" with the Crit "‼" glyph (thresholds capture, inverted-FPS case: "12 fps ‼" and "45 fps ▲" also render correctly — inverted-threshold severity is unaffected by the template change).
- Long labels ellipsize correctly with tooltip still bound ("GPU Hot Spot Temperature Sen…", "Core Voltage (SVI3…").
- The menu button (now the `Icon.More` three-dot glyph) never overlaps label/value text in any capture, including the open-menu (`tile-menu`) capture, and its reserved 24-unit column keeps header rows uniform whether or not it's visible.
- Gauge value is legible at the standard 28pt size outside the arc (Gauge M/L in `gallery-dark-amber`/`gallery-light`).
- Empty optional rows (no `LimitText`/no `MaxText` line) leave no gap — confirmed by comparing tiles with and without a configured limit/max side by side in the gallery capture (e.g., "Value L" 66.0 °C has no blank line between the value and the min/avg/max footer).
- Light theme (`gallery-light`) renders with correct contrast; no leftover dark-theme artifacts.
- Missing-value tiles (`missing` capture) show a bare em dash with no unit suffix (`UnitText=""`), never "0" or a stale value.
- Core matrix (`dense-dark-amber`) cells are readable at the new 11px floor for index/clock/temp, `HeatToBrush` backgrounds and per-core severity temp color unchanged, panel padding/radius match the token contract, and cells were not inflated to tile size.

## Not verified

- **Negative temperatures**: the harness's `dense` fixture defines a `-4.0 °C` "Chipset Temperature" tile (`Tile("mobo.temp.chipset", ...)` in `Scenarios.cs`), but it's defined only in `Definitions` — it's never added to that scenario's `DashboardMetrics` list, so it never actually renders on the dashboard in any scenario. I did not find another fixture with a negative reading. Not independently screenshot-verified; by inspection, `ValueFormatter.FormatParts`/`Format` apply `float.ToString(def.Format)` with no sign-stripping, and the templates apply no special-casing based on sign, so a negative value renders through the identical code path as every other value already confirmed unclipped at similar or greater string length (e.g., "1.187", "4850").
- **Hover/keyboard-focus states**: the harness captures are static; the `TileMenuButtonStyle`'s hover/focus-triggered visibility (and `HeaderButton`'s own hover/pressed template states, owned by T2) were reviewed by template inspection only, consistent with the T2 report's same caveat. The `tile-menu` substate capture confirms the button becomes visible and its click opens the context menu correctly, but not via an actual mouse-hover screenshot.
- No dedicated unit test exists for `TileSizeToLengthConverter` (none existed before this task either); its new dimensions were verified via the capture pixel sizes and by code inspection.
