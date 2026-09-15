# Graph smoothing, fixed time axis, and visual effects — design

Date: 2026-09-11. Base: master @ `75115aa` (v1.9.2). Branch `feature/graph-effects`. Independent of
`feature/dashboard-layout-modes` (disjoint files except `AppSettings`/`SettingsService`/`SettingsViewModel`
/the Settings tab of `DashboardWindow.xaml`, which both branches only *append* to).

Owner ask, verbatim: "fixed line smoothing and some snazzy graph/bar effects, purely for pizzazz".

## Goal

1. **Smooth lines.** Sparklines and the detail-window history chart draw a smooth curve through the samples
   instead of a jagged polyline. The curve is a **monotone cubic** (Fritsch–Carlson tangents, rendered as cubic
   Béziers): it passes through every sample and never overshoots between two samples, so a spike still reads as
   a spike and a flat run stays flat. The smoothing amount is fixed (not user-tunable); the user only turns it
   on/off.
2. **Fixed time axis.** Today `X(i) = w * i / (count - 1)` stretches however many samples exist across the full
   width, so during warm-up (before the ring buffer fills) the line visibly stretches and its time scale keeps
   changing every tick. Both charts now lay samples out at a constant `w / (capacity - 1)` spacing anchored to the
   right edge: the line grows in from the right and scrolls left at a fixed scale from the first sample on. Once
   the buffer is full this is pixel-identical to today. (Applies regardless of the smoothing toggle.)
3. **Effects, all optional.**
   - **Line glow:** the sparkline/history line is drawn once more underneath itself with a wide (4.5 px), low-alpha
     (~0x38) pen of the same colour, plus the existing gradient fill gains a slightly stronger top stop (0x55 → 0x70
     in tiles). Cheap: two `DrawGeometry` calls, no `Effect`/`BlurEffect` (those force software bitmap effects on
     every frame).
   - **Last-value pulse:** when a new sample arrives the last-value dot emits one ring (radius 2.5 → 9, opacity
     0.6 → 0) over 500 ms. One short animation per poll tick, nothing runs between ticks.
   - **Bars:** `LevelBar` fills with a left-to-right gradient (colour → colour at 70 % alpha), a 1 px lighter top
     highlight, and the fill length eases to its new value over 250 ms (quadratic ease-out) instead of jumping.
   - **Gauges:** `ArcGauge` sweep eases the same way; the arc end gets a soft glow cap (the same double-draw trick
     as the line glow).
   - **Value text:** no change (per-character animation is noise on a number that changes every second).

All effects are gated by two settings so the "before" look is one click away and the harness can capture both.

## Settings (rule 3: defaults, null-guards, old files load)

- `AppSettings.SmoothLines` (`bool`, default **true**).
- `AppSettings.GraphEffects` (`bool`, default **true**) — glow, gradients, pulse, eased bars/gauges as one switch.
  The OS "animation effects" preference (`SystemParameters.ClientAreaAnimation == false`) additionally disables
  the *animated* parts (pulse, easing) while leaving the static glow/gradients — checked in Stats.App only.
- No sanitation needed (bools). `SettingsViewModel` exposes both as observable bools under the Appearance tab,
  new header **"Graphs"** placed after the "Dashboard" header: "Smooth lines", "Glow and motion effects".
  Raises a new `SettingsChange.Graphs`; the composition root applies it via a static `GraphStyle` (below).

## Core (`Stats.Core`, testable, WPF-free)

- New `Stats.Core/Metrics/CurveSmoothing.cs` (static):
  - `MonotoneTangents(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, Span<double> tangents)` —
    Fritsch–Carlson: secant slopes `d_k`, tangents `m_k` = average of neighbouring secants (one-sided at the
    ends), zeroed where the secant is zero or changes sign, then clamped so `α² + β² ≤ 9`.
  - `BezierControlPoints(x0, y0, m0, x1, y1, m1)` → `(c1x, c1y, c2x, c2y)` with `h = x1 - x0`,
    `c1 = (x0 + h/3, y0 + m0·h/3)`, `c2 = (x1 − h/3, y1 − m1·h/3)`.
  - Operates on already-projected pixel coordinates so the App controls call it once per finite run after their
    existing `X(i)`/`Y(v)` projection and stride decimation. Runs of length 1 draw a dot only, length 2 a straight
    segment.
- New `Stats.Core/Metrics/SampleAxis.cs` (static): `X(int index, int count, int capacity, double left, double width)`
  = `left + width − (count − 1 − index) · width / max(1, capacity − 1)`, with `capacity < count` treated as
  `count` (the buffer was just resized smaller). This is the one place the fixed-axis rule lives; both controls
  call it.
- `MetricTileViewModel.HistoryCapacity` (`int`, from `_history.Capacity`, raised in `Refresh`) and
  `MetricDetailViewModel` (or whatever feeds `MetricDetailWindow`) gets the same, so the controls can bind
  `Capacity`.

## App (`Stats.App`)

- New `Stats.App/Helpers/GraphStyle.cs` (static): `SmoothLines`, `Effects`, `Motion` (= `Effects &&
  SystemParameters.ClientAreaAnimation`) plus a `Changed` event; set from `App.xaml.cs` at startup and on
  `SettingsChange.Graphs`. Controls subscribe in `Loaded`/unsubscribe in `Unloaded` exactly like
  `ThemeManager.Changed` today and call `InvalidateVisual`.
- `Sparkline` and `HistoryChart`: new `Capacity` DP (int, default 0 = behave as today, i.e. `count`);
  `X(i)` → `SampleAxis.X`. Line and fill figures: when `GraphStyle.SmoothLines`, emit `BezierTo` per segment
  using `CurveSmoothing` (fill figure uses the same curve so the gradient hugs the line); otherwise `LineTo` as
  today. Glow pass before the main line when `GraphStyle.Effects`. Pulse: on `Values` change with `Motion`, start
  a `DoubleAnimation` on a private `PulseProgress` DP (0→1, 500 ms, `AffectsRender`); `OnRender` draws the ring
  from it. Hover/guides/gap behaviour untouched; NaN runs still break the curve.
- `LevelBar`: gradient + highlight when `Effects`; `AnimatedFraction` private DP animated from the old to the new
  `Fraction` (250 ms, `QuadraticEase` out) when `Motion`, else set directly. `OnRender` draws `AnimatedFraction`.
- `ArcGauge`: same `AnimatedFraction` pattern; glow cap when `Effects`.
- `TileTemplates.xaml` / `MetricDetailWindow.xaml`: bind `Capacity="{Binding HistoryCapacity}"`.
- `README.md`: "Graphs" settings paragraph.

## Preview harness

`PreviewComposition` substates: `graphs-plain` (both settings off) and `graphs-effects` (both on, the default) on
the dashboard scenario; plus `detail-smooth`/`detail-plain` for the detail window if a detail scenario exists.
Because the pulse and easing are time-based, the harness must capture at rest: substates set the values *before*
the window is shown so no animation is in flight (the existing capture path already waits for layout). Three new
`baseline.json` entries.

## Non-goals

No user-tunable smoothing amount, no per-tile toggles, no `BlurEffect`/`DropShadowEffect`, no continuous
(idle) animation, no changes to Value tiles, the overlay, or the tray icon, no new NuGet packages.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings.
- Tests (Core): `MonotoneTangents` — monotone data yields tangents with the data's sign, an extremum sample gets
  tangent 0, a flat run yields 0s, the α/β clamp engages on a documented case; `BezierControlPoints` for a
  straight line returns collinear controls; `SampleAxis.X` — full buffer equals `left + width·i/(count−1)`, a
  half-full buffer right-aligns (last sample at `left + width`, spacing `width/(capacity−1)`), `capacity < count`
  degrades to the full-buffer formula, `count == 1` puts the sample at the right edge; settings round-trip and
  default-true for both bools (and a pre-1.10 file without them loads as true); `SettingsViewModel` raises
  `SettingsChange.Graphs` for each toggle.
- Captures: `graphs-plain` vs `graphs-effects` at 1180×720 Dark Amber and Light; a warm-up fixture (buffer 25 %
  full) shows the line hugging the right edge.
- Owner checklist: curve looks right on a spiky FPS line and a flat temp line; no overshoot below 0 % on a bar
  metric; pulse timing; bar/gauge easing feel; "Glow and motion effects" off restores the v1.9.2 look; CPU cost at
  idle unchanged with effects on (Task Manager, ~1 % or less).
