# Dashboard layout modes — whole-branch review

**Verdict: needs a fix wave.** Two blockers, both visible in this branch's own captures: the first entry
into Free/Snap leaves every tile overlapping the core-matrix block, and the canvas is horizontally
centred instead of anchored top-left. Gates themselves are clean: `dotnet build --nologo` 0 warnings /
0 errors, `dotnet test --nologo` green (740 `Stats.Core.Tests` + 182 `Stats.UiPreview.Tests`, 0 warnings),
re-run at `c301dff`.

Reviewed: `master..HEAD` (`1cf8230`…`c301dff`), the spec, the plan, `docs/layout-modes/EVIDENCE.md`, and
all 7 PNGs in `artifacts/ui-polish/layout/`.

---

## Blockers

**B1 — The unmeasured-block fix-up consumes the *first* `SizeChanged`, not the *final* one; tiles land
on top of the core-matrix block.**
`src/Stats.Core/ViewModels/DashboardViewModel.Layout.cs:132-144` (and the `_coreMatrixHeight == 0` guard
at `:214`), driven by `src/Stats.App/Views/DashboardWindow.xaml.cs:312`.

Scenario: the user switches Auto → Free. `OnLayoutModeChanged` → `RebuildSections` → `PlaceUnpositioned`
runs while the Free `ScrollViewer` is still `Collapsed`, so `_coreMatrixHeight == 0`; tiles are seeded at
`y = Gap` and tracked. WPF then lays the block out and `SizeChanged` fires **more than once** — the first
time with a partial/intermediate height, the last time with the real one. The handler shifts by the first
height and then `Clear()`s the list, so the later real height never lands.

This is not hypothetical — it is what the branch's own captures show. In
`artifacts/ui-polish/layout/v13-layout-free-dark-amber.png` the block ("Cores — load · clock · temp")
occupies canvas y≈0…188 while row 1 of tiles (Tctl/Tdie, Package Power, TDC Current) sits at y≈73, i.e.
`Gap(12) + 61`, where 61 was an intermediate measured height. The tiles cover the bottom two-thirds of the
core matrix. `v13-layout-grid-dark-amber.png` shows the same at y≈77 (`Snap(12)=16 + 61`). The evidence
doc's claim that the pack placed tiles "below it … no two tiles overlap" is contradicted by the images it
cites.

Fix: make the reservation idempotent rather than one-shot. Track `(id, baseY)` pairs (the pre-shift seed
y) instead of bare ids, and on every `SetCoreMatrixSize` with a new height re-apply `pref.Y = baseY +
height` (re-snapping when `IsGridLayout` — see B1b) for still-tracked tiles; only drop an id when the user
explicitly moves that tile (`SetTilePosition`, already at `:90`), on `ResetPositions`, or when
`PlaceUnpositioned` starts a fresh seed. Save only when a value actually changed so the per-measure churn
stays at zero once the height settles. Alternatively, have the view report the block size before the VM
ever seeds (measure the block once on `Loaded`/`LayoutUpdated` and call `SetCoreMatrixSize` ahead of the
mode switch) and delete the fix-up path entirely — simpler, and it removes a whole class of ordering bug.

**B1b (same fix wave):** the shift at `:138` adds a raw measured height and never re-snaps, so in
**Snap-to-grid** mode every seeded tile ends up off-grid (y=77 in the grid capture is not a multiple of
16) — the one thing Snap mode exists to guarantee. Run the shifted value back through
`ClampAndMaybeSnap`.

**B2 — The Free/Snap canvas is horizontally centred, not left-anchored.**
`src/Stats.App/Views/DashboardWindow.xaml:271`
(`<Grid Width="{Binding CanvasWidth}" Height="{Binding CanvasHeight}">`).

Scenario: a WPF element with an explicit `Width` and the default `HorizontalAlignment="Stretch"` is
arranged **centred** in the leftover space. Whenever the window is wider than `CanvasWidth` the whole
canvas floats in the middle, so a tile the user dropped at x=0 does not sit at the dashboard's left edge,
and every tile slides sideways when the window is resized — persisted positions stop meaning what the user
sees. Visible in `v13-layout-free-dark-amber.png`: the left gutter is ≈143 px (Auto's is 16 px), and it
differs between the Free and Grid captures purely because their canvas widths differ by 4 px.

Fix: `HorizontalAlignment="Left" VerticalAlignment="Top"` on that `Grid`.

---

## Should-fix

**S1 — `SetGroupStatus` now churns `StatusLines` on every poll tick.**
`src/Stats.Core/ViewModels/DashboardViewModel.cs:389` → `.Layout.cs:246-252`.
`App.xaml.cs:544` calls `SetGroupStatus(MetricGroup.Game, FrameStatus())` on **every** coalesced refresh
while the dashboard is visible. It used to be idempotent (`section.StatusText = text`); now it
unconditionally `Clear()`s and refills an `ObservableCollection`, firing a `Reset` + N `Add` per tick and
forcing the status `ItemsControl` (XAML:258) to tear down and rebuild its containers — flicker in Free/Snap
and pointless allocation in Auto too. Fix: build the new list locally and return early when
`SequenceEqual` with the current `StatusLines` (or early-return in `SetGroupStatus` when the text for that
group is unchanged).

**S2 — `ResetPositions` can silently fail to persist.**
`.Layout.cs:64-77`. It nulls every `TilePref.X/Y` and `CoreMatrixX/Y`, then relies on `PlaceUnpositioned`'s
`if (placedAny) _saveSettings()` (`:243`). With no tiles and no core matrix — or if the command is ever
reached in Auto, where `PlaceUnpositioned` doesn't run at all — nothing is placed, so nothing is saved and
the reset is lost on the next launch (the in-memory nulls are also never re-seeded, and the extent is
never recomputed). Fix: call `_saveSettings()` (and `RecomputeCanvasExtent()`) unconditionally at the end
of `ResetPositions`; also clear `_tilesSeededBelowUnmeasuredBlock` there.

**S3 — Every newly-added tile seeds at the same spot, on top of an existing tile.**
`.Layout.cs:218-241`. The pack restarts at `(0, startY)` and `continue`s past already-placed tiles without
advancing `x`/`y`, so when the user enables one new metric in Free/Snap it is always placed at the very
first slot — directly under the tile that has been there since the first seed. Overlap is allowed by
design, but a new tile that is *invisible* on arrival reads as "nothing happened". Fix: advance the cursor
past already-placed tiles' bounding boxes, or seed a new tile below `max(bottom)` of everything placed.

**S4 — `ReassertCanvasBindings` may be a no-op, leaving the tile where the mouse dropped it.**
`src/Stats.App/Views/DashboardWindow.xaml.cs:244-248`. `Canvas.Left`/`Top` come from a **Style setter**
binding (XAML:308-309), not a local binding; `BindingOperations.GetBindingExpression` on a style-sourced
expression after a `SetCurrentValue` is not a guaranteed retrieval. If it returns null, a Snap-mode drop
that snaps back to the value the VM already held fires no `PropertyChanged` and the raw drag position
stays painted — the tile visibly sits off-grid until something else re-evaluates the binding. Make it
deterministic: after the commit, push the VM's final value back with
`container.SetCurrentValue(Canvas.LeftProperty, tile.X)` / `TopProperty, tile.Y` (and
`vm.CoreMatrixX/Y` for the block) instead of relying on `UpdateTarget()`.

**S5 — Nothing focuses the container, so keyboard nudge is unreachable after a drag.**
`xaml.cs:194-209` / `:263-271`. `FreeTile_PreviewMouseLeftButtonDown` captures the mouse but never calls
`container.Focus()`, so the discoverable path to arrow-key nudge is Tab-cycling the whole dashboard.
Fix: `container.Focus()` on button-down (before capture) in both the tile and block handlers.

**S6 — The core-matrix block handler is missing the tile handler's two guards.**
`xaml.cs:263-271` has neither the `FindAncestorButtonOrSelf` exclusion (`:197`) nor the `ClickCount == 2`
branch (`:198`) that the tile path has, so the two containers diverge: any interactive child a future
`CoreMatrixView` gains becomes un-clickable (drag starts instead), and a double-click on the block behaves
differently from a double-click on a tile. Mirror both guards.

**S7 — Test asserts the buggy behaviour from B1.**
`tests/Stats.Core.Tests/DashboardLayoutModeTests.cs:181-183`:
`vm.SetCoreMatrixSize(300, 250); // a later re-measurement must not shift it again` and the assertion that
Y stays at `Gap + 200`. That comment encodes exactly the assumption that fails in the real window
(multiple `SizeChanged` events, the first with a partial height). Rewrite it to the converse: a later,
larger measurement re-applies `baseY + newHeight` for tiles the user has not explicitly moved — and keep
`SetCoreMatrixSize_DoesNotShiftATileMovedExplicitlyAfterSeeding` (`:185`) as the counter-case.

**S8 — `LayoutGrid_..._EverySeededPositionIsSnapped` checks a state the app never renders.**
`tests/Stats.UiPreview.Tests/DashboardLayoutSubstateTests.cs:66`. It asserts `X/Y % 16 == 0` on the VM
immediately after the substate runs — before any `SizeChanged` shift. The rendered capture
(`v13-layout-grid-dark-amber.png`) is off-grid at y≈77. The test is green and the screen is wrong; add a
post-`SetCoreMatrixSize` assertion so it covers the state that actually ships.

**S9 — Missing negative cases.** Per spec acceptance: no test for `ResetPositions` with zero
tiles / no core matrix (S2); no test that switching Free → Auto → Free does not re-seed or re-save
(`RebuildSections` runs the pack on every mode change and every tile add/remove — currently correct only
because `PlaceUnpositioned` no-ops when everything is placed, which nothing asserts across a mode
round-trip); no test that a tile size change in Free mode updates the canvas extent (it does not —
`RecomputeCanvasExtent` is not called from `OnSizeChanged`, `MetricTileViewModel.cs:122`, so a tile grown
S→L at the canvas edge can be clipped until the next rebuild).

**S10 — `DashboardLayoutModeConverter.Read` corrupts the parse on a container token.**
`src/Stats.Core/Settings/DashboardLayoutMode.cs:31-35`. A string/number/null/bool all behave correctly
(the reader is already positioned on the scalar), but a hand-edited `"DashboardLayoutMode": {}` or `[]`
returns without consuming to the matching end token, and `System.Text.Json` then throws
"read too much or not enough" — defeating the whole point of the converter (the doc comment at `:20-29`
promises the opposite). Add `if (reader.TokenType is JsonTokenType.StartObject or
JsonTokenType.StartArray) reader.Skip();` before returning `Auto`. The interaction with the global
`JsonStringEnumConverter` is otherwise correct — the property-level `[JsonConverter]` at
`AppSettings.cs:117` wins, and `Write` emits the same member name, so files round-trip identically either
way. Rest of rule 3 checks out: every new field is defaulted, `SanitizePosition`
(`SettingsService.cs:132-134`) null-guards NaN/negative/>100 000 including the `TilePrefs` loop's
`Where(p => p is not null)`, and old `settings.json` files load unchanged.

**S11 — Mode switch saves twice.** `.Layout.cs:47-49` calls `_saveSettings()` then `RebuildSections()`,
whose `PlaceUnpositioned` saves again. Harmless but the test that documents it is named
`SetLayoutModeCommand_Persists_Rebuilds_AndRaisesModeFlags_SavesOnce` while asserting `Equal(2, saves())`
(`DashboardLayoutModeTests.cs:57,69`) — either drop the redundant save (let the rebuild's save cover it,
with an unconditional save as the fallback) or rename the test.

**S12 — Evidence doc's visual observations are wrong and should be corrected before the PR.**
`docs/layout-modes/EVIDENCE.md` "Visual evidence" claims "no two tiles overlap" and "every tile packed …
below it" for `layout-free`, and calls the layout "visually accepted". The captures show tiles covering
the core matrix (B1). Also: because the seed depends on a measurement that arrives at an arbitrary point,
these captures are **not** reproducible run-to-run or across DPI — the harness substates are only
deterministic at the VM level, not at the pixel level. Re-capture after B1/B2 and restate.

---

## Nits

- **N1** — `src/Stats.App/Views/DashboardWindow.xaml:273-276`: the z-order comment is a garbled run-on
  ("sits under the tiles layer, matching the Auto template's core-matrix-above-tiles visual order isn't
  required here since…"). Rewrite as one sentence. Substantively the ordering is a real trade-off worth
  stating plainly: the block is the **bottom** layer, so a tile dragged over it permanently blocks
  grabbing the block in that region.
- **N2** — `DashboardWindow.xaml:300`: `x:Name="FreeCanvas"` is never referenced from code-behind; the
  generated field is dead. Drop the name or use it.
- **N3** — `xaml.cs:331-339`: `FindAncestorButtonOrSelf` walks past the container into the window chrome;
  bound the walk at `container` so a future ancestor `Button` can't suppress all dragging.
- **N4** — `xaml.cs:263-308` duplicates `xaml.cs:194-261` almost verbatim (two parallel sets of
  `_freeDrag*`/`_coreDrag*` fields, identical threshold/commit logic). One generic drag helper
  parameterised by a commit callback would halve it.
- **N5** — `.Layout.cs:19-28`: the `_tilesSeededBelowUnmeasuredBlock` doc comment is ~8 lines for one
  field and, after B1, describes behaviour that will no longer be true. Trim it to one line pointing at
  `SetCoreMatrixSize`.
- **N6** — `src/Stats.App/Views/CoreMatrixView.xaml:4`: the block's root `Border` carries `Margin="6"`, so
  the block renders 6 px right/below its `Canvas.Left/Top` and the size reported to
  `SetCoreMatrixSize` includes that margin. Consistent, but it means "drop at x=0" is not flush with
  tiles dropped at x=0. Worth a comment at minimum.
- **N7** — `README.md:154-167` omits that the core-matrix block is also arrow-key nudgeable, and does not
  mention that positions are kept per tile even for tiles removed from the dashboard (they return to the
  same spot when re-added).

Everything else checks out. All bindings resolve against the view models (`X`/`Y`/`Width`/`Height` on
`MetricTileViewModel`, `CoreMatrixX/Y`/`CanvasWidth`/`CanvasHeight`/`StatusLines`/`IsAutoLayout`/`IsEmpty`
on `DashboardViewModel`); every resource key used by the new XAML exists (`AllTrueToVis` Theme.xaml:93,
`NullToVis` :88, `StatsFocusVisual`, `IconGlyph`, `Icon.Warn`, `FontSizeDense`, `WarnBrush`); the
`vm:`/`views:` prefixes are declared (DashboardWindow.xaml:4-5); the block's implicit `CoreMatrixView`
`DataTemplate` in `Canvas.Resources` resolves for its `ContentControl` child; the `ContentPresenter`
`Focusable`/`IsTabStop`/`FocusVisualStyle` trio is copied verbatim from the already-shipped Auto container
style, so focus and Shift+F10 work the same; capture lifecycle is re-entrancy-safe
(`CommitFreeDrag`/`CommitCoreDrag` null the field *before* `ReleaseMouseCapture`, so the synchronous
`LostMouseCapture` re-entry is a no-op); double-click in Free mirrors Auto's `Tile_PreviewMouseLeftButtonDown:105`
exactly, so Details opens once, not twice; the `"…"` button exclusion works for tiles; the two
visibility `MultiBinding`s and `AllTrueToVisibilityConverter` correctly collapse on
`DependencyProperty.UnsetValue`; the View menu builds checkable items from `vm.LayoutMode` and disables
Collapse/Expand outside Auto and Reset inside Auto as specified; and the Auto path is untouched apart from
two cheap extra calls per rebuild (`RebuildStatusLines`, `RecomputeCanvasExtent`) — no positions are ever
written in Auto, which `AutoMode_NeverWritesPositions_EvenAcrossMultipleRebuilds` covers.

---

## Owner checklist additions

Only verifiable on real hardware with the app launched from the Start menu (elevation):

1. **After B1/B2 are fixed**, enter Free for the first time on a machine with a core matrix and confirm
   row 1 of tiles sits fully *below* the block, at the left edge of the window, at every window width
   and after a resize.
2. **Snap alignment after a real drop.** Drag a tile to a deliberately odd position in Snap mode and
   confirm it visibly jumps to the 16 px grid — and stays there (this is the S4 `UpdateTarget()` risk;
   the one case to watch is a short drag that snaps back to the cell it started in).
3. **Drag feel** — latency, whether the tile tracks the cursor 1:1 at UI scale 0.9 and 1.3, and whether
   the grab point stays under the cursor.
4. **Keyboard nudge reachability** — after dropping a tile, does an arrow key move *that* tile, or does
   focus still sit elsewhere (S5)? Check Shift+arrow = 4 steps, and that arrows don't scroll the
   ScrollViewer instead.
5. **Core-matrix block drag**, including dragging a tile on top of it and then trying to grab the block
   again (N1 — expected to be impossible in the overlapped region).
6. **Per-monitor DPI / multi-monitor**: drag a tile, move the window to a 150 % display, confirm positions
   don't drift (positions are logical px; the harness only ever tested 96 DPI).
7. **Persistence across restart**: build a Free layout, close via the tray, relaunch, confirm mode *and*
   every position — including the block's — come back identically, with no first-frame flash of the
   seeded pack.
8. **Auto unchanged in the live app**: group collapse/expand, same-group drag-reorder, and the insertion
   adorner still behave exactly as in v1.9.2 (the capture comparison covers static pixels only).
9. **Reset tile positions…** from a heavily customised Free layout, then immediately restart, and confirm
   the reset actually persisted (S2).
10. **Free mode with zero tiles** (deselect every metric): confirm the empty state shows and the View
    menu's Reset item doesn't throw.

---

## Fix wave

Applied on top of `c301dff` (uncommitted — see `docs/layout-modes/EVIDENCE.md`'s Candidate section).
`dotnet build --nologo` 0 warnings / 0 errors; `dotnet test --nologo` 759/759 `Stats.Core.Tests` +
182/182 `Stats.UiPreview.Tests`, 0 warnings.

- **B1** — Fixed. `_tilesSeededBelowUnmeasuredBlock` now tracks `(id, baseY)`; `SetCoreMatrixSize`
  re-applies `baseY + height` for every tracked tile on *every* call (not just the first), saving
  once per call only when something moved, so the final `SizeChanged` measurement always wins.
- **B1b** — Fixed. The re-applied shift now goes through `DashboardLayout.Snap` when `IsGridLayout`,
  so a shifted tile in Grid mode stays on the 16px grid.
- **B2** — Fixed. `DashboardWindow.xaml`'s canvas `Grid` now has `HorizontalAlignment="Left"
  VerticalAlignment="Top"`, so it's left-anchored instead of centred in leftover space.
- **S1** — Fixed. `RebuildStatusLines` builds the new list locally and returns early on
  `SequenceEqual` with the current `StatusLines`, instead of unconditionally `Clear()`+refilling.
- **S2** — Fixed. `ResetPositions` now clears the seed-tracking list and calls `_saveSettings()`
  unconditionally (plus an explicit `RecomputeCanvasExtent()`), so a reset with zero tiles / no core
  matrix still persists.
- **S3** — Fixed. `PlaceUnpositioned` now starts the pack below `max(bottom)` of every already-placed
  tile and the core-matrix block (if positioned), not just below the block, so a newly-enabled metric
  never lands on top of an existing tile.
- **S4** — Fixed. `CommitFreeDrag`/`CommitCoreDrag` push the VM's final X/Y back with
  `SetCurrentValue(Canvas.LeftProperty/TopProperty, …)` instead of relying on
  `BindingOperations.GetBindingExpression(...).UpdateTarget()` against a style-sourced binding.
- **S5** — Fixed. Both `FreeTile_PreviewMouseLeftButtonDown` and
  `CoreMatrixBlock_PreviewMouseLeftButtonDown` call `container.Focus()` before capturing the mouse.
- **S6** — Fixed. The block handler now mirrors the tile handler's button-exclusion
  (`FindAncestorButtonOrSelf`) and `ClickCount == 2` (no-op) guards.
- **S7** — Fixed. The test at `DashboardLayoutModeTests.cs` was rewritten to the converse: a later,
  larger `SetCoreMatrixSize` measurement re-applies against the same `baseY`, not the first shift's
  value; the explicit-move counter-case test is unchanged.
- **S8** — Fixed. `LayoutGrid_SetsGridMode_AndEverySeededPositionIsSnapped` now also calls
  `SetCoreMatrixSize` and re-asserts every position is on-grid afterwards.
- **S9** — Fixed. Added `ResetPositionsCommand_WithZeroTilesAndNoCoreMatrix_StillPersists`,
  `ModeRoundTrip_FreeToAutoToFree_DoesNotReSeedOrReSave`, and
  `TileSizeChange_InFreeMode_UpdatesCanvasExtent`; `DashboardViewModel.SetTileSize` now calls
  `RecomputeCanvasExtent()` explicitly.
- **S10** — Fixed. `DashboardLayoutModeConverter.Read` calls `reader.Skip()` on `StartObject`/
  `StartArray` before falling back to `Auto`; new `DashboardLayoutModeConverterTests` cover `{}`,
  `[]`, nested containers, and a whole-`AppSettings` round-trip.
- **S11** — Fixed. `OnLayoutModeChanged` only calls `_saveSettings()` when the rebuild's own seed pack
  didn't already save (tracked via `_seedPackSavedLastRebuild`); the existing test was renamed/fixed
  to assert one save, and a new test covers the fallback-save path when nothing was seeded.
- **S12** — Fixed. All 7 captures re-run (`--method rtb`), opened, and the "Visual evidence" section
  of `docs/layout-modes/EVIDENCE.md` rewritten to describe what they actually show post-fix.
- **N1** — Fixed. The z-order comment in `DashboardWindow.xaml` is now one sentence stating the
  bottom-layer trade-off plainly.
- **N2** — Fixed. `x:Name="FreeCanvas"` dropped (was unreferenced).
- **N3** — Fixed. `FindAncestorButtonOrSelf` now takes a `boundary` parameter and stops walking past
  the drag container, so an ancestor Button outside the container can no longer suppress dragging.
- **N4** — Skipped, as instructed (the drag-helper dedupe didn't fall out naturally from the other
  fixes).
- **N5** — Fixed. `_tilesSeededBelowUnmeasuredBlock`'s doc comment trimmed and updated to describe the
  new every-call re-apply behavior.
- **N6** — Fixed. Added a comment on `CoreMatrixView.xaml`'s root `Border` noting the `Margin="6"`
  offset.
- **N7** — Fixed. `README.md`'s Dashboard layout section now mentions the block is arrow-key
  nudgeable and that positions persist per tile across removal/re-add.
