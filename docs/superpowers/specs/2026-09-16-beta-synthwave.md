# Beta synthwave pass

Owner requested synthwave themes, a flashier overview and related creative details on beta.
Base `e7b57ae` (v1.11.0-beta.1); integration branch `feature/beta-synthwave`.
This request explicitly extends the earlier UI-polish prohibition on new theme names; all safety,
compatibility, readability and live resource replacement rules still apply. Owner subsequently authorized
publishing `v1.11.0-beta.2` after review and validation; stable/master remain unchanged.

## Design

- Append three opt-in presets: **Synthwave**, **Outrun**, **Midnight**. Keep the five old presets and
  default exactly intact. Purple/magenta/cyan, sunset pink/orange, and midnight blue/ice palettes.
  Dark tinted surfaces, bright readable labels, amber/red semantic warning/critical colors.
- Reuse the current preset ComboBox, settings schema, graph-effects toggle, theme application path.
- New themes show a compact **Neon overview** with static vector horizon/sun/grid decoration,
  four compact live reading cards and Compare/Recordings shortcuts using existing commands.
  No animation loop, bitmap asset, new timer, package, polling or duplicated metric history.
- Overview uses existing selected dashboard tile instances: first selected metric per CPU/GPU/Memory/Game
  group (in existing user order), skips absent groups. Labels show the actual metric name, not inferred
  total/health claims. Missing readings retain the normal em dash and severity glyphs remain visible.
  Empty selection keeps the existing empty-state action; decorative chrome must not imply good health.
- Old themes do not show this panel. New themes alone opt in; changing back restores existing layout.
  Rebuild overview references whenever selections/profiles/layout rebuild the tile collection.
- Overview appears inside the Auto-layout scroll content. Free/Grid receive the palette only, preserving
  their exact user-positioned canvas and drag coordinate system; the Appearance caption explains this.
- Keep overview compact and responsive at 860x600, app scale1.3. Static art is non-hit-testable.
  No new tile interaction/drag handler and no mutation of user layout coordinates or selected metrics.

## Shared resource contract

Theme worker owns shared dictionaries. Add fallback `NeonOverviewVisibility` (Collapsed) and
`NeonSecondaryBrush` / `NeonSecondaryColor`. ThemeManager replaces the secondary brush/color and
visibility at the application top level on every Apply, including old/light themes. New overview
consumes DynamicResource keys; no static brush captures, no pinned-overlay changes.
Overview VM contract: `OverviewTiles`, at most four existing MetricTileViewModel references; empty
when no matching selected groups. No separate polling/refresh and no settings schema changes.

## Work and acceptance

1. Theme worker: preset data, theme resources/application, preset and live-resource/contrast regression tests.
2. Dashboard worker: overview tile selection/binding, native WPF overview design/shortcuts, meaningful
   tests for selection refresh/reference reuse/missing readings. Disjoint files; no shared dictionary edits.
3. Parent integrates; zero-warning Release build/full tests, isolated WPF dark/light/new preset captures,
   narrow/scaled layout, empty/unavailable data, theme switching with open window and popup, pinned overlay.
4. Independent source review and WPF validator; document any real hardware, DPI or native interaction gaps.

Routing: configured named Luna inventory, Terra implementation and Sol review/validation; runtime effective
model metadata unobservable. Parent owns docs/git/composition/project files. Workers cannot delegate,
commit, push, alter config or expand scope. At most two source editors and two bounded repair attempts.

Baseline: Debug build blocked by pre-existing PowerShell PID4940 holding Core obj DLL; use Release output
without stopping the user's process. Baseline Release build passed with zero warnings/errors.
Baseline full tests: 972 Core + 256 preview passed. Isolated WPF baseline at
`artifacts/synthwave/before-dashboard.png` (1180x720, 96 DPI), zero binding warnings, inspected by parent.
Seven fully visible medium tiles before; new overview's density tradeoff applies only to opt-in themes.
