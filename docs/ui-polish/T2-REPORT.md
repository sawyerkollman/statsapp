# T2 — shared visual foundations — report

Branch `ui-polish`, base `b595902`. Build: `dotnet build --nologo` → 0 warnings, 0 errors. Tests:
`dotnet test --nologo` → 674 (Stats.Core.Tests) + 172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings.
Not committed/pushed per instructions.

## Files changed

- `src/Stats.App/Views/Theme.xaml` — added `xmlns:sys`; added the 12 typography/shape token resources.
- `src/Stats.App/Views/Icons.xaml` — **new**. 16 frozen icon geometries + merge-order doc comment.
- `src/Stats.App/Views/Controls.xaml` — `IconGlyph` Path style; `StatsExpanderToggle` now draws
  `Icon.ChevronRight` (rotated on expand) instead of a hand-drawn stroke path; `HeaderButton` rebuilt on a
  hand-rolled `ControlTemplate` (new `HeaderButtonTemplate` key) with hover/pressed/focus/disabled states;
  new `PrimaryButton` style (`BasedOn HeaderButton`); header comment updated for the new merge order.
- `src/Stats.App/Views/AppStyles.xaml` — `GroupHeader`/`SettingsHeader` foreground → `TextPrimary`,
  font sizes → token resources; `SettingsLabel` font size → token; `TextBox` rebuilt on a hand-rolled
  template (Border + `PART_ContentHost`) with hover/focus/disabled states; `TabItem` gets a 2px accent
  bottom indicator on selection + `StatsFocusVisual`; `ToolTip` gets `BorderThickness`/`Padding`; header
  comment updated for the new merge order.
- `src/Stats.App/App.xaml` — merges `Icons.xaml` right after `Theme.xaml`, before `Controls.xaml`.
- `tools/Stats.UiPreview/PreviewApp.cs` — same merge addition; doc comment "four" → "five" dictionaries.
- `src/Stats.App/Helpers/ThemeManager.cs` — new `PaletteFor(string)` test-only accessor; three palette hex
  adjustments for contrast (below).
- `tests/Stats.UiPreview.Tests/PaletteContrastTests.cs` — **new**. WCAG contrast test.
- `artifacts/ui-polish/after-t2/*.png` (+ `.json` sidecars) — 16 verification captures.

No changes to `TileTemplates.xaml`, any window, or any view model (owned by later tasks).

## Frozen resource contract

### Typography/shape tokens (`Theme.xaml`, plain `sys:Double`/`CornerRadius`, not theme-tinted)

| Key | Value | Consumers |
|---|---|---|
| `FontSizeTitle` | 20 | T7 dialog/window titles |
| `FontSizeSection` | 14 | `GroupHeader` (this task); T3/T4 section headers |
| `FontSizeBody` | 13 | `HeaderButton`, `SettingsHeader`, `TabItem` (this task); T4/T5/T7 body text |
| `FontSizeLabel` | 12 | `SettingsLabel` (this task); T3 tile labels |
| `FontSizeDense` | 11 | T3 dense tile metadata |
| `FontSizeValue` | 28 | T3 main tile value |
| `FontSizeValueCompact` | 22 | T3 compact tile value |
| `FontSizeUnit` | 13 | T3 tile unit text |
| `RadiusPanel` | 8 | T3 tile/panel corners |
| `RadiusControl` | 4 | `HeaderButton`, `TextBox` (this task); T3/T4/T5/T6/T7 buttons/inputs |
| `ControlMinHeight` | 32 | `PrimaryButton` (this task); T4/T5/T7 standard controls |
| `ControlMinHeightDense` | 28 | `HeaderButton`, `TextBox` (this task); T4 dense toolbar row |

### Icon geometries (`Icons.xaml`, frozen `Geometry`, 0..16 box, `IconGlyph` Path style fills them)

| Key | Consumer |
|---|---|
| `Icon.Overlay` | T4 toolbar |
| `Icon.Fans` | T4 toolbar |
| `Icon.Peaks` | T4 toolbar |
| `Icon.Metrics` | T4 toolbar |
| `Icon.Settings` | T4 toolbar |
| `Icon.View` | T4 compact View menu |
| `Icon.ChevronDown` | T4 section headers; used directly by no control yet |
| `Icon.ChevronRight` | `StatsExpanderToggle` (this task, rotated for expanded state); T4 section headers, T6 sources expander |
| `Icon.Close` | T4 flyout close |
| `Icon.Search` | T4 picker search box |
| `Icon.Clear` | T4 picker search clear action |
| `Icon.More` | T3 tile menu "⋯" |
| `Icon.Warn` | severity glyph consumers (T3/T4/T6/T7) |
| `Icon.Crit` | severity glyph consumers (T3/T4/T6/T7) |
| `Icon.Info` | T4/T6 notices |
| `Icon.Identify` | T6 fan card Identify |

Plus `IconGlyph` (Controls.xaml, `TargetType="Path"`): Width/Height 16, Stretch Uniform, Fill
`DynamicResource TextPrimary`, VerticalAlignment Center, `IsHitTestVisible="False"`.

All 16 geometries are eagerly parsed when `Icons.xaml` is merged at process start (not lazily per-use), so
every capture in this run — which all completed with 0 warnings and no exceptions — is itself a
verification that all 16 path strings parse correctly.

### Contract additions to `ThemeManager.cs`

`public static IReadOnlyDictionary<string, string> PaletteFor(string presetName)` — test-only accessor
returning the same hex table `Apply` reads from. Everything else in the class stays private/unchanged in
shape (only the constant/dictionary values below changed).

## HeaderButton / PrimaryButton / TextBox / TabItem state matrix

| Control | Hover | Pressed | Focus (keyboard) | Disabled | Selected/checked |
|---|---|---|---|---|---|
| `HeaderButton` | Background→`ControlBg`, Border→`BorderDim` | Background→`GaugeTrack`, Border→`AccentBrush` | Border→`AccentBrush` (via `IsKeyboardFocused`, independent of hover; system `FocusVisualStyle` nulled out) | Opacity 0.5 | n/a |
| `PrimaryButton` (`BasedOn HeaderButton`) | inherited (see above) | inherited | inherited | inherited | Base border is always `AccentBrush` (not Transparent), 32px min-height |
| `TextBox` | Border→`TextSecondary` | n/a | Border→`AccentBrush` (`IsKeyboardFocusWithin`) | Opacity 0.5 | n/a |
| `TabItem` | unchanged (no hover state defined, matches prior behavior) | n/a | `StatsFocusVisual` dashed accent outline (was the invisible system default) | n/a | Fill `TileBg`, text `TextPrimary`, **new** 2px `AccentBrush` bottom indicator on the neutral `Bd` surface |

Per-usage invalid-input `DataTrigger` overrides (`BorderBrush="CritBrush"` in `DashboardWindow.xaml`'s
`BasedOn="{StaticResource {x:Type TextBox}}"` derivations) still work: they set the `TextBox.BorderBrush`
dependency property, which the new template's Border picks up via `TemplateBinding` whenever a template
trigger isn't itself overriding it. Verified in the `invalid-limit`/`invalid-threshold` captures (both show
a clearly red `CritBrush` border with no mouse/keyboard focus present to mask it).

## Contrast table (WCAG 2.x, computed from `ThemeManager.PaletteFor`, all measured — full dump)

Targets: small essential text / severity glyphs ≥ 4.5:1; accent control boundary/focus ≥ 3:1. Custom
accents are out of scope per the task spec.

| Preset | Pair | Ratio | Target | Pass |
|---|---|---|---|---|
| Dark Amber | TextPrimary/WindowBg | 15.10 | 4.5 | ✓ |
| Dark Amber | TextPrimary/TileBg | 13.41 | 4.5 | ✓ |
| Dark Amber | TextPrimary/FlyoutBg | 12.37 | 4.5 | ✓ |
| Dark Amber | TextPrimary/ControlBg | 11.52 | 4.5 | ✓ |
| Dark Amber | TextSecondary/WindowBg | 6.14 | 4.5 | ✓ |
| Dark Amber | TextSecondary/TileBg | 5.45 | 4.5 | ✓ |
| Dark Amber | TextSecondary/FlyoutBg | 5.03 | 4.5 | ✓ |
| Dark Amber | TextSecondary/ControlBg | 4.68 | 4.5 | ✓ |
| Dark Amber | WarnBrush/TileBg | 6.99 | 4.5 | ✓ |
| Dark Amber | WarnBrush/WindowBg | 7.87 | 4.5 | ✓ |
| Dark Amber | CritBrush/TileBg | 4.93 | 4.5 | ✓ (fixed, was 4.18 — see below) |
| Dark Amber | CritBrush/WindowBg | 5.56 | 4.5 | ✓ |
| Dark Amber | AccentBrush/WindowBg | 6.59 | 3.0 | ✓ |
| Dark Amber | AccentBrush/TileBg | 5.85 | 3.0 | ✓ |
| Dark Amber | AccentBrush/FlyoutBg | 5.40 | 3.0 | ✓ |
| Dark Amber | AccentBrush/ControlBg | 5.03 | 3.0 | ✓ |
| Dark Blue | TextPrimary/* | 15.10 / 13.41 / 12.37 / 11.52 | 4.5 | ✓ (shared dark neutrals) |
| Dark Blue | TextSecondary/* | 6.14 / 5.45 / 5.03 / 4.68 | 4.5 | ✓ |
| Dark Blue | WarnBrush/TileBg, WindowBg | 6.99, 7.87 | 4.5 | ✓ |
| Dark Blue | CritBrush/TileBg, WindowBg | 4.93, 5.56 | 4.5 | ✓ (fixed) |
| Dark Blue | AccentBrush/WindowBg,TileBg,FlyoutBg,ControlBg | 5.95, 5.28, 4.87, 4.54 | 3.0 | ✓ |
| Dark Green | TextPrimary/*, TextSecondary/* | (same as above) | 4.5 | ✓ |
| Dark Green | WarnBrush/CritBrush | (same as above) | 4.5 | ✓ (fixed) |
| Dark Green | AccentBrush/WindowBg,TileBg,FlyoutBg,ControlBg | 7.44, 6.61, 6.10, 5.68 | 3.0 | ✓ |
| Dark Purple | TextPrimary/*, TextSecondary/* | (same as above) | 4.5 | ✓ |
| Dark Purple | WarnBrush/CritBrush | (same as above) | 4.5 | ✓ (fixed) |
| Dark Purple | AccentBrush/WindowBg,TileBg,FlyoutBg,ControlBg | 5.28, 4.69, 4.32, 4.02 | 3.0 | ✓ |
| Light | TextPrimary/WindowBg,TileBg,FlyoutBg,ControlBg | 14.86, 16.61, 15.94, 13.97 | 4.5 | ✓ |
| Light | TextSecondary/WindowBg,TileBg,FlyoutBg,ControlBg | 4.79, 5.36, 5.14, 4.51 | 4.5 | ✓ |
| Light | WarnBrush/TileBg, WindowBg | 5.86, 5.24 | 4.5 | ✓ |
| Light | CritBrush/TileBg, WindowBg | 5.93, 5.31 | 4.5 | ✓ (fixed, WindowBg was 4.30) |
| Light | AccentBrush/WindowBg,TileBg,FlyoutBg,ControlBg | 3.83, 4.28, 4.11, 3.60 | 3.0 | ✓ (fixed, was 2.76/-/-/2.60) |

All 172 assertions (4 presets × 16 dark-shared cases collapsed above for brevity + Light's 16 = the full
`SmallTextCases`/`ControlBoundaryCases` matrix) pass. Raw per-case output was captured via a temporary
diagnostic test run, then removed; the shipped test file only contains the two `[Theory]` assertions.

### Palette adjustments (ThemeManager.cs, "do NOT change the dark surface neutrals" honored — only
AccentBrush/CritBrush touched, never WindowBg/TileBg/FlyoutBg/ControlBg)

| Key | Preset(s) | Before | After | Before ratio (worst case) | After ratio |
|---|---|---|---|---|---|
| `CritBrush` (shared `DarkCrit` constant) | all 4 dark presets | `#FFE05A4F` | `#FFE66E64` | 4.18:1 on TileBg | 4.93:1 on TileBg |
| `AccentBrush` | Light | `#FFD97B1F` | `#FFB8650F` | 2.60:1 on ControlBg | 3.60:1 on ControlBg |
| `CritBrush` | Light | `#FFC94438` | `#FFB23A2F` | 4.30:1 on WindowBg | 5.31:1 on WindowBg |

## Captures (`artifacts/ui-polish/after-t2/`), all sidecars show `"Warnings": []`

| Capture | Dimensions | Notes |
|---|---|---|
| `dashboard-normal-dark-amber` | 1166×713 | Group headers now `TextPrimary`; rounded button corners |
| `dashboard-theme-cycle-{0..5}` (Amber/Blue/Green/Purple/Light/custom-accent) | 1166×713 | Every frame fully repainted; Light and custom accent show no stale amber |
| `dashboard-tile-menu` | 1166×713 | Context menu renders correctly |
| `settings-dark-amber` | 1166×713 | `Settings` tab shows the new 2px accent underline |
| `settings-light` | 1166×713 | Same, Light palette |
| `settings-invalid-limit` | 1166×893 | Package Power field shows `CritBrush` border (invalid) |
| `settings-invalid-threshold` | 1166×893 | CPU warn field shows `CritBrush` border + inline error text |
| `picker-860x600-scale1.3` | 846×593 | Renders correctly at narrow width + 1.3 UI scale |
| `fans-on-curve` | 746×633 | Segmented Auto/Manual/Curve control and curve editor unaffected |
| `threshold-dialog-valid-light` | 366×207 | Light theme dialog, `TextPrimary` heading confirmed via pixel sample (`#1E1E22`, not a color bug — apparent warm tint at thumbnail scale was ClearType subpixel anti-aliasing) |
| `input-dialog` | 366×158 | OK/Cancel buttons render with rounded corners and focus ring |

Visual check performed by reading each PNG: no magenta/black fallback boxes, no default Aero buttons, group
headings render in primary (white/near-black) text instead of accent, `HeaderButton`/dialog buttons show
rounded corners, `TextBox` focus/invalid borders render, Light and custom-accent theme-cycle frames are
fully repainted with no leftover amber.

## Not verified

- `ToolTip`'s `Padding`/`BorderThickness` setters were added without a custom template (the stock Aero2
  `ToolTip` template forwards both via `TemplateBinding`, per standard WPF behavior), but no capture in the
  required list opens a live tooltip, so this was not visually confirmed against a real render.
- No interactive/mouse-driven hover or pressed-state capture exists in the harness (captures are static
  DPI/layout snapshots), so `HeaderButton`/`TextBox` hover and pressed visuals were reviewed by template
  inspection, not by an actual hovered/pressed screenshot.
