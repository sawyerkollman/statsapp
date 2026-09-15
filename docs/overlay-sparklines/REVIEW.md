# Overlay sparklines and status line — whole-branch review

**Verdict: needs a fix wave.** Two blockers (one layout defect, one false evidence claim); the rest is sound and
matches the spec. Gates re-run in this worktree: `dotnet build --nologo` → **0 warnings, 0 errors**;
`dotnet test --nologo` → **Stats.Core.Tests 822/822, Stats.UiPreview.Tests 211/211, 0 failures, 0 warnings**.

## Off-path identity check (required)

`feature/v1.10` has no substate-less `--view overlay` entry in `baseline.json`. Its only overlay substate that adds
nothing to the panel is `light-parent` (`PreviewComposition.cs:323` — a no-op; the theme is applied at the
`CaptureHost` level and every overlay brush is pinned), so
`artifacts/ui-polish/before/v12-overlay-light-parent.png` (normal / overlay / Light / 400×200) is the base's plain
overlay render.

| File | SHA-256 |
| --- | --- |
| base `artifacts/ui-polish/before/v12-overlay-light-parent.png` | `074611650c47a3c0c29cd04c06c266a87aaf6886f7b484c886be7edfa2671c86` |
| branch `artifacts/overlay-sparklines/v13-overlay-sparklines-off-dark-amber.png` | `074611650c47a3c0c29cd04c06c266a87aaf6886f7b484c886be7edfa2671c86` |

**Byte-identical** (239×55 both). Owner decision 3 / goal 3 is proven for the horizontal off path. The vertical off
path is not covered by any capture (S1).

Every capture present in both trees, compared by SHA-256 (69 files): **62 identical, 7 differ**, all 7 explained.

- `artifacts/ui-polish/before/v12-overlay-{light-parent,long-value,move-mode,opacity-min,opacity-max,vertical}.png`
  — the branch's batch re-rendered them with the new `OverlayGraphs = Sparkline` default, so they now show
  sparklines. Expected consequence of the default change (see N2).
- `artifacts/ui-polish/before/v11-settings-invalid-hotkey.png` — that entry is
  `"Substate": "invalid-hotkey+category-overlay"` (`baseline.json:53`), i.e. it renders the Settings → Overlay tab,
  which gained the two new checkboxes. Expected.
- Every `artifacts/graph-effects/*`, `artifacts/ui-polish/layout/*` and all non-overlay `before/*` capture is
  unchanged — no collateral rendering drift.

Side check on an explicit evidence claim: `v13-overlay-sparklines-light.png` and
`v13-overlay-sparklines-dark-amber.png` are both `61c15c9d682f2596214d2a27d074ddfb4535b4f74754eb4c528e46b4baa2bd4b`
— EVIDENCE.md's "pixel-for-pixel the same" is true. All 11 new sidecars have `"Warnings": []`, which clears the
`ElementName=ValueText`, `ElementName=TilesHost` and `RelativeSource` bindings.

## Blockers

**B1 — `src/Stats.App/Views/OverlayWindow.xaml:54`: the status strip widens the overlay panel instead of wrapping
at the tiles' width, through a multi-pass layout feedback loop.**
`MaxWidth` is bound to `TilesHost.ActualWidth`, but `Margin="10,4,10,0"` is *outside* `MaxWidth` — WPF adds margin
to `DesiredSize` after clamping to `MaxWidth`. So the strip's footprint is `MaxWidth + 20`, which makes the outer
`StackPanel` 20 px wider, which (the `ItemsControl` is `Stretch`) makes `TilesHost.ActualWidth` 20 px wider, which
pushes a new `MaxWidth` through the binding, and so on until the text's longest wrapped line fits in
`MaxWidth − 36`. Failure scenario: with a vertical overlay and a long warn strip the panel silently grows by ~90 px
of empty space beside the tiles, and every status *text* change (a game-mode transition, a fan write failure
clearing) re-converges the width over several layout passes — a visible width jump on an always-on-top layered
window, exactly the `SizeToContent` jitter the spec's owner checklist says must not happen. Proven by the branch's
own committed captures: `v13-overlay-vertical-sparklines-dark-amber.png` is **100×202** while
`v13-overlay-vertical-status-line-warn-dark-amber.png` (same tiles, same defaults, plus the strip) is **187×259**,
and the PNG shows the tile column still ~76 px wide inside a 187 px panel. The same mechanism makes the spec's
"an empty overlay (no metrics) gives `MaxWidth` 0 and hides the strip" false — `HasStatus` is independent of the
tiles, so with no overlay metrics the strip alone would drive the panel width.
*Fix:* move the horizontal insets inside the capped element — wrap the strip `Grid` in
`<Border MaxWidth="{Binding ActualWidth, ElementName=TilesHost}" Padding="10,4,10,0" HorizontalAlignment="Left"
Visibility="{Binding HasStatus, …}">` and drop the `Grid`'s own `Margin`/`MaxWidth`/`Visibility`. A `Border`'s
`Padding` is inside its `MaxWidth`, so the strip's footprint is `≤ TilesHost.ActualWidth`, the loop settles in one
pass, and the panel width is fixed by the tiles. (Equivalent: keep the `Grid`, set `Margin="0,4,0,0"`, and put
`Margin="10,0,0,0"` / `Margin="0,0,10,0"` on the `Path` and `TextBlock`.)

**B2 — `docs/overlay-sparklines/EVIDENCE.md:76` (and the summary at `:83`) asserts the opposite of the capture it
cites.** It says the vertical warn strip "wraps across three lines … and the panel's width is unchanged from the
plain vertical capture — confirms the strip's `MaxWidth` binding … only ever adds height". The capture wraps across
**four** lines, and the panel is **187 px** wide against **100 px** for `vertical+sparklines` (sidecar
`PhysicalWidth`, and the PNG header agrees). Failure scenario: the owner signs off the "panel width never changes"
checklist item on an evidence row that documents the defect. *Fix:* after B1, re-capture
`vertical+status-line-warn` and rewrite that row and the "What the harness can show" sentence from the new sidecar
numbers; if the widening is ever accepted instead, the spec's assumed decision 7 and the owner checklist item both
have to be amended rather than the evidence.

## Should-fix

**S1 — `tools/Stats.UiPreview/captures/baseline.json:81`: the off path is only proven horizontally.** There is no
`vertical+sparklines-off` entry, and the branch's regenerated `v12-overlay-vertical.png` now has sparklines on, so
nothing in the repo shows the vertical overlay with `OverlayGraphs = None`. That is the orientation where the value
row's `StackPanel` changes from the old direct-child layout to a nested vertical panel, so it is the one most worth
pinning. *Fix:* add one entry
`{ "Scenario": "normal", "View": "overlay", "Substate": "vertical+sparklines-off", "Theme": "Dark Amber",
"Width": 200, "Height": 400, … }` and SHA-compare it against the base's `v12-overlay-vertical.png`
(`5017acd39579913361c51cca60e15abd6a6691c205c5ef0b9c42e28f079ee1fd`).

**S2 — `src/Stats.App/App.xaml.cs:436`: `Views()` per tick to read three enum values.** No deadlock — `Views()`
takes `_gate` (= `AppSettings.SyncRoot`), and `FanController.Tick` deliberately releases that lock for all hardware
I/O (phases 1 and 3 are CPU-only, `FanController.cs:266-288`), so the UI thread can only ever wait on a short
CPU-bound section, and the lock is never held across a Dispatcher call. But this is a new per-poll UI-thread
acquisition of the settings lock whenever the overlay is visible with the strip on (previously only while the Fans
window was open), and each call allocates a `List<FanChannelView>` plus one 14-field `FanChannelView` record per
channel plus the `Select(...).ToList()`, to read `.Status`. *Fix:* add
`public IReadOnlyList<FanChannelStatus> Statuses()` next to `Views()` (`FanController.cs:644`) returning just the
statuses under the same lock, and call that from `PushOverlayStatus`.

**S3 — `src/Stats.App/Views/OverlayWindow.xaml:58`: the warn glyph is `VerticalAlignment="Center"` beside wrapping
text.** With a multi-line strip the triangle floats to the vertical middle and reads as belonging to the middle
line — visible in `v13-overlay-vertical-status-line-warn-dark-amber.png`, where it sits next to "PresentMon: access
denied" rather than at the start of the strip. *Fix:* `VerticalAlignment="Top"` with a small top margin
(`Margin="0,2,4,0"`) so it anchors to the first line; keep `Center` only if the strip is guaranteed single-line,
which it is not.

**S4 — `src/Stats.Core/ViewModels/OverlayStatusComposer.cs:55`: `TrimEnd('.')` strips *every* trailing period.**
Spec assumed decision 6 says "minus one trailing period". `FrameStatus()` (`App.xaml.cs:419-425`) cuts at the first
`". "` and keeps one period, so today's inputs are fine, but a reason ending in an ellipsis ("PresentMon: starting…"
spelled with three dots) would lose all three and read wrong. *Fix:* `var r = frameReason.Trim(); segments.Add(
r.EndsWith('.') ? r[..^1] : r);` and add a `"…"`-style case to `Compose_FrameReason_TrimsTrailingPeriodAndWarns`.

## Nits

**N1 — `tests/Stats.UiPreview.Tests/OverlaySparklinesSubstateTests.cs:40`:** `SubstateCatalog_RejectsThemForDashboard`
is a `[Theory]` over `dashboard`, `settings` and `fans`. The name (inherited verbatim from the spec) undersells it;
`SubstateCatalog_RejectsThemForOtherViews` reads true.

**N2 — `artifacts/ui-polish/before/`:** the full-baseline batch overwrote the v1.9.2 "before" reference set — the six
`v12-overlay-*.png` and `v11-settings-invalid-hotkey.png` in this worktree are now *after* images. Harmless
(`artifacts/` is git-ignored and the main checkout still holds the real ones), but worth a line in EVIDENCE.md so a
later reader does not diff against them.

**N3 — `src/Stats.App/App.xaml.cs:774`:** `PushOverlayStatus()` runs for *every* `SettingsChange.Overlay` —
orientation, font scale, opacity and click-through included — and without an `IsVisible` check, so a click-through
toggle with the overlay hidden still takes the fan lock and builds a status. Once per settings edit, so cost is
nil; gating on `_overlay is { IsVisible: true }` would make the "no work while hidden" rule uniform, at the price of
losing the "apply live" immediacy the spec asked for. Leave as is unless S2 is not taken.

**N4 — `artifacts/overlay-sparklines/smoke.png` / `smoke-vertical.png`:** Task 2's plan-mandated smoke captures are
in the artifacts folder but not in EVIDENCE.md's table; one row each (or a sentence) would close the loop.

## Owner checklist additions

1. **The existing "panel width never changes when the strip appears or its text changes" item cannot pass as
   built** — re-run it after B1, specifically: vertical overlay, 1 metric, status line on, force a fan write
   failure and then clear it, and confirm the panel width is identical before/during/after and that the text wraps
   inside the tiles' column.
2. **Pulse cost in a layered window.** `OverlayWindow` is `AllowsTransparency="True"`, so it is software-composited;
   with Glow and motion effects on, every visible tile starts a 500 ms `DoubleAnimation` on each poll tick
   (`Sparkline.OnValuesChanged`) and each of those ~30 frames re-renders the whole layered window and allocates a
   fresh `Pen` + `SolidColorBrush` (`CurveRenderer.PulsePenFor`). Measure Task Manager CPU with 3 *and* 8 overlay
   metrics, effects on vs off, at `OverlayFontScale` 1.0 and 1.6, over a fullscreen game — not just the 3-metric
   case the spec lists. (The glow itself is a plain wide pen, not a `BlurEffect`, so it is safe in software.)
3. **Vertical off path** (no automated coverage, see S1): `OverlayGraphs = None` + vertical must look exactly like
   v1.9.2.
4. **Zero overlay metrics + status line on:** confirm the panel is sane and the strip is legible (the tiles no
   longer supply a width).
5. **Status line at `OverlayFontScale` 1.6:** the 10 px strip text scales with the panel; check the wrap point and
   that the window does not oscillate in width as the status text changes.

## Fix wave

Applied by the controller on top of `81f303c`: **B1** fixed — the strip's inset moved into a `Border`'s `Padding`
(inside its `MaxWidth`), `TilesHost` is left-aligned so its `ActualWidth` is the tiles' natural width rather than
the StackPanel's, and the strip clips to its bounds; the re-taken `vertical+status-line-warn` capture is 100×392
(same width as `vertical+sparklines`, 100×202; height only), where the pre-fix capture was 187×259 — WPF now
emergency-breaks the two tokens wider than the column ("PresentMon:", "(simulated)") instead of widening the
panel. **B2** fixed (evidence row rewritten). **S1** fixed (`vertical+sparklines-off` capture added, 97×132).
**S2** fixed (`FanController.Statuses()` — status-only read under the same brief lock; `PushOverlayStatus` uses it).
**S3** fixed (glyph top-aligned with the first line). **S4** fixed (exactly one trailing period stripped). The
off path (`sparklines-off`) re-taken after the fix is still byte-identical to the base's plain overlay capture.
Gates: build 0 warnings, 822 + 211 tests green.
