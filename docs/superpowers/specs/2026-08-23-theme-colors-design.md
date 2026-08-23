# Colour customization — design

**Date:** 2026-08-23 · **Branch:** `feature/theme-colors` (from master 2a861f7) · **Status:** approved in conversation (owner picked accent + presets, live apply)

## Goal
Curated preset palettes plus a custom accent colour, applied live — no restart. Warn/crit stay semantic (amber/red) in every preset.

## Settings
- `AppSettings.ThemePreset` (string, default `"Dark Amber"`) — unknown/legacy value falls back to default at load (SettingsService sanitation, rule 3).
- `AppSettings.ThemeAccent` (string?, default null = use the preset's accent) — `#RRGGBB` hex; invalid value sanitized to null.

## Presets (each defines the full 11-brush palette from Theme.xaml)
- **Dark Amber** — current palette exactly (accent #E68A2E).
- **Dark Blue** — same neutrals, accent #4A9EE0.
- **Dark Green** — same neutrals, accent #4FC06A.
- **Dark Purple** — same neutrals, accent #A47AE0.
- **Light** — light neutrals (WindowBg #F2F2F4, TileBg #FFFFFF, FlyoutBg #FAFAFC, ControlBg #EBEBEF, BorderDim #D0D0D6, TextPrimary #1E1E22, TextSecondary #6A6A72, GaugeTrack #D8D8DE), accent #D97B1F (darker for contrast on white). Warn #C4841D / Crit #C94438 darkened likewise.
Dark presets keep Warn #E6A23C / Crit #E05A4F.

## Live apply mechanism
- `ThemeManager` (static, Stats.App — WPF-coupled) holds the palette definitions and `Apply(preset, accentOverride)`:
  mutates the **existing** `SolidColorBrush` instances in the merged Theme.xaml dictionary (`brush.Color = …`) so every StaticResource consumer updates instantly. Brushes must therefore not be frozen — verify none carry `PresentationOptions:Freeze`; do NOT replace dictionary entries (StaticResource would not re-resolve).
- Raises `ThemeManager.Changed` event. The four custom-drawn controls (`Sparkline`, `ArcGauge`, `LevelBar`, `FanCurveEditor`) and `HeatToBrushConverter`/`DashboardWindow` code-behind stop hardcoding palette colours: they read the shared brushes (via `Application.Current.Resources` or injected static accessors on ThemeManager) at render time and subscribe to `Changed` → `InvalidateVisual()` (unsubscribe on unload to avoid leaks). Truly semantic constants (e.g. heat gradient stops, severity mapping) stay hardcoded — only palette-derived colours route through the theme.
- Recent fixes stay correct by design: ComboBox popup items and context submenu items keep their fixed dark-on-light foregrounds (popups are light Aero2 in every preset, including Light).
- `App` applies the saved theme at startup (before first window) and on `SettingsChange.Theme`.

## UI (Settings tab)
- "Theme" section: preset ComboBox; accent row of ~10 predefined swatches (click to set `ThemeAccent`) + a `#RRGGBB` TextBox (validated live, red outline when invalid) + "Reset" (accent → preset default). All changes save via the existing settings pipeline and apply live via ThemeManager.

## ViewModel
`SettingsViewModel`: `ThemePresetNames`, `SelectedThemePreset`, `AccentHex` (string, two-way), `AccentSwatches` (IReadOnlyList<string>), `ResetAccentCommand`. Setters write settings + save + raise the existing settings-changed event with a new `SettingsChange.Theme`.

## Tests (Stats.Core stays WPF-free)
Palette *data* and validation logic live where testable: hex validation/sanitation (`SettingsService` load: bad preset → default, bad accent → null) and any pure preset-name/lookup logic get unit tests. Rendering is not unit-tested.

## Non-goals
Full per-brush editor, import/export, per-tile colours, automatic light/dark following Windows, changing warn/crit semantics.
