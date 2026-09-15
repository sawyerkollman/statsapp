# Tile resize by drag — whole-branch review (Task 4)

Branch `feature/tile-resize` (3 commits on `feature/v1.10`: `76fc2e9` core, `181b2f2` app, `6727edf` harness).
Contract: `docs/superpowers/specs/2026-09-11-tile-resize-design.md` (owner decisions 1–3, assumed 1–11).
Gates re-run here: `dotnet build --nologo` → **0 warnings, 0 errors**; `dotnet test --nologo` → **816/816 +
203/203, 0 failures, 0 warnings**. Captures `v14-*` opened and inspected.

**Verdict: needs a fix wave — one blocker (B1), four should-fixes.** The design is faithfully implemented and the
drag lifecycle, commit ordering, Esc path, `FocusTileById`, and the canvas-coordinate correction are all correct.
The container swap has one unnoticed collateral casualty.

## Blockers

**B1 — the `ContentControl` container swap silently kills the tile "…" button's keyboard-focus reveal in
Free/Snap.** `src/Stats.App/Views/TileTemplates.xaml:125` —
`<DataTrigger Binding="{Binding RelativeSource={RelativeSource AncestorType=ContentPresenter}, Path=IsKeyboardFocusWithin}" Value="True">`
— used to resolve to the Free container itself (a `ContentPresenter`, `feature/v1.10`'s
`<Style TargetType="ContentPresenter">`). The new `ControlTemplate` inserts a `ContentPresenter`
(`src/Stats.App/Views/DashboardWindow.xaml:333`) *below* the focusable `ContentControl`, so `FindAncestor` now
stops at that inner presenter — a **descendant** of the focused element, whose `IsKeyboardFocusWithin` is always
`false`. Failure scenario: Tab to a tile in Free or Snap; the resize grip appears but the "⋯" tile-options button
does not, so a keyboard-only user has no visible route to kind/size/rename/thresholds (Shift+F10 still works, the
affordance is gone). Auto is unaffected (its container is still a `ContentPresenter`).

This is visible in the branch's own evidence and was read as a success: `docs/tile-resize/EVIDENCE.md` "Visual
evidence" records that focusing a Free tile changes **one** 12×12 region; pre-branch it would have changed two.
Confirmed by eye in `artifacts/ui-polish/layout/v14-layout-resize-grip-dark-amber.png` — grip present at the
Tctl/Tdie tile's bottom-right, no "⋯" at its top-right.

Fix (does not touch Auto): name the template presenter and re-publish the container's focus state onto it —
`DashboardWindow.xaml:333` → `<ContentPresenter x:Name="TileContent"/>`; add to the existing trigger at
`DashboardWindow.xaml:352` `<Setter TargetName="TileContent" Property="Tag" Value="FocusWithin"/>`; add to
`TileMenuButtonStyle` (`TileTemplates.xaml:121–128`)
`<DataTrigger Binding="{Binding RelativeSource={RelativeSource AncestorType=ContentPresenter}, Path=Tag}" Value="FocusWithin"><Setter Property="Visibility" Value="Visible"/></DataTrigger>`
(Auto's container presenter has a null `Tag`, so it is inert there). Then re-take `layout-resize-grip` and expect
**two** changed regions vs `v13-layout-free-dark-amber.png`.

## Should-fix

**S1 — `Ctrl++` as advertised is unreachable on a US keyboard.** `DashboardWindow.xaml.cs:580`
`if (Keyboard.Modifiers != ModifierKeys.Control) return false;` combined with `Key.OemPlus` (line 583). On a US
layout "+" *is* Shift+OemPlus, so pressing the gesture the Size submenu prints (`InputGestureText = "Ctrl++"`,
`DashboardWindow.xaml.cs:620`) and the README advertises (`README.md:144`) sends Ctrl+Shift+OemPlus and is
rejected; only `Ctrl+=` and numpad `Ctrl+Add` work. Fix: accept Shift alongside Control —
`if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || (Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;`
(the browser/VS zoom convention). Spec §7 names only `Ctrl+OemPlus`, so this is a contract clarification — the
alternative is to relabel the gesture text as "Ctrl+=" / "Ctrl+-", which is worse.

**S2 — Ctrl+Plus/Minus is not suppressed while a grip drag is in flight, and double-applies.**
`DashboardWindow.xaml.cs:561–568` — `Tile_PreviewKeyDown` runs `TryStepTileSize` unconditionally, and during a
resize the container holds both mouse capture and keyboard focus. Scenario: hold the grip, drag to "L", press
Ctrl+Minus → `StepTileSize` rebuilds the container out from under the captured mouse → `LostMouseCapture` →
`CommitFreeResize` applies the drag candidate **as well**, so the tile ends at L after the user asked for smaller.
Fix: `if (_resizeContainer is not null) return false;` at the top of `TryStepTileSize` (ignore the key mid-drag,
the way Esc is already special-cased at `DashboardWindow.xaml.cs:388`).

**S3 — adorner leak when the container is torn down mid-drag.** `DashboardWindow.xaml.cs:361`
`AdornerLayer.GetAdornerLayer(_resizeAdorner.AdornedElement)?.Remove(_resizeAdorner);` re-looks-up the layer from
an element that, on the S2 path (and any other rebuild that detaches the container while the drag is live),
is already out of the visual tree — the lookup returns null and the dashed + accent outline stays painted on the
window's adorner layer indefinitely. Fix: cache the layer where it is added (`DashboardWindow.xaml.cs:309`) in a
`private AdornerLayer? _resizeAdornerLayer;` and remove through that, nulling both. (`RemoveInsertionAdorner`,
line 193, has the same latent shape — pre-existing, out of scope.)

**S4 — the outline renders into the window-level adorner layer, not one inside the dashboard `ScrollViewer`.**
`DashboardWindow.xaml.cs:309` — there is no `AdornerDecorator` in `DashboardWindow.xaml`, so
`GetAdornerLayer` returns the default `Window` template's layer, which is outside the scrolled region and unclipped
by it. Scenario: scroll a Free canvas so a tile's top edge is above the viewport, grab its grip, drag — the
candidate rectangle is drawn over the header bar. Fix: wrap the Free canvas `Grid`
(`src/Stats.App/Views/DashboardWindow.xaml:272`) in an `<AdornerDecorator>`; it is layout-transparent, so the
existing `v13-layout-*` captures stay byte-identical, and it scopes the change away from Auto's `InsertionAdorner`.

## Nits

**N1** — `src/Stats.Core/Settings/TileDimensions.cs:29` allocates `new[] { S, M, L }` on every `Nearest` call,
i.e. on every `PreviewMouseMove`. Hoist to a `private static readonly TileSize[] All = …`.

**N2** — `src/Stats.App/Controls/ResizeOutlineAdorner.cs:67` draws the S/M/L letter at the candidate rect's
**top-left** (`Left + 4, Top + 2`), i.e. straight over the tile's own name text, with no backing plate. Move it to
the candidate rect's bottom-right (next to the cursor) or draw a small filled chip behind it.

**N3** — `README.md:144` says the Larger/Smaller items sit "next to the size menu"; they are *inside* the Size
submenu, after a separator (`DashboardWindow.xaml.cs:619–625`). Reword to "inside the size submenu".

**N4** — `src/Stats.App/Views/DashboardWindow.xaml.cs:368`
`private static Canvas ParentCanvas(FrameworkElement container) => (Canvas)VisualTreeHelper.GetParent(container);`
is an unguarded cast; if the container is detached mid-drag `GetParent` returns null and `e.GetPosition(null)`
silently measures against the wrong root instead of failing. Return `… as Canvas` and no-op the drag when null.
(Both current call sites — the Free item container and the core-matrix `ContentControl` at
`DashboardWindow.xaml:288` — are genuine direct `Canvas` children today, so the correction itself is right and the
block drag still works.)

**N5** — `DashboardWindow.xaml.cs:251` / `:257` dispatch on `_resizeContainer is not null` without the
`ReferenceEquals(sender, …)` guard every neighbouring handler uses. Harmless today (mouse capture makes a move and
a resize mutually exclusive), but add it for symmetry.

**N6** — `docs/tile-resize/EVIDENCE.md` "Candidate" still describes Task 3 as "not committed"; it is `6727edf`.
Its "Visual evidence" pixel-diff row must be re-taken after B1 (expected: two changed regions, not one).

## Checked and correct (no action)

Drag lifecycle: grip hit-test runs after the button exclusion and before the double-click branch, and `e.Handled`
suppresses the move drag (`:208–222`). `CommitFreeResize` (`:341–356`) nulls the slot before releasing capture, so
the `LostMouseCapture` re-entry is a no-op; every field is read off the container *before* `SetTileSize` destroys
it; sub-threshold drags and same-size drags apply nothing. `CancelFreeResize` (`:324`) uses the same ordering.
`FocusTileById` (`:510`) is correct in both layouts — `DispatcherPriority.Loaded` (6) runs after `Render` (7), so
the regenerated containers exist; the Auto path's `ContentTemplate.FindName("SectionTilesList", …)` resolves
because the section `DataTemplate` object graph is built eagerly even when the `Expander` is collapsed. No
window-level `PreviewKeyDown` or `Escape` handler competes with the cancel path (`PickerFlyout_KeyDown` is a
bubbling handler on a non-ancestor; `HotkeyBox_PreviewKeyDown` is on a `TextBox` outside the tile containers), and
nothing else in the app binds Ctrl+Plus/Minus. Trigger precedence in the new `ControlTemplate` is right: template
triggers outrank the `TileResizeGripStyle` `Visibility` setter, and the last-declared active trigger wins, so the
`IsResizable` `DataTrigger` does override both reveal triggers. `ItemTemplateSelector` still applies —
`ItemsControl.PrepareContainerForItemOverride` handles `ContentControl` containers natively, and `RebuildSections`
(`DashboardViewModel.cs:418`) clears and re-creates every `MetricTileViewModel`, so `TileSelector` re-runs on a size
change. Core: `Nearest` ties resolve larger via `<=` over S,M,L; `Step` is safe against `enum TileSize { S, M, L }`;
`SetTileSize`'s guard has exactly one behavioural effect on existing callers (re-picking the already-selected radio
item in the Size submenu no longer saves — intended, spec §6); `IsResizable` is re-set on every rebuild including
mode switches. Harness substates, catalog entries, `baseline.json` and the four `TileResizeSubstateTests` match the
spec; `docs/ui-polish/PREVIEW_HARNESS.md` carries no substate list needing an update. The spec edit in `6727edf`
(`Nearest_WideButShort` (400,80)→(350,80)) is a justified correction — (400,80) really is nearer to L.

## Owner checklist additions

Only settleable on real hardware (Start-menu launch, elevation):

1. **After B1**: Tab to a tile in Free *and* in Snap — grip **and** "⋯" both appear, and both disappear together
   on blur; hover reveals both as before.
2. **Focus ring on the new container type** — Tab through Free tiles and confirm `StatsFocusVisual` still draws on
   the `ContentControl`. Nothing in this branch proves it: the harness focuses programmatically, which does not
   raise a focus visual, and the `v14` capture shows none.
3. **L→S in one drag** — with height barely changing, the Euclidean rule needs width ≤ ~112 px before the candidate
   flips from M to S (S is itself 160 wide). Confirm this does not feel stuck on M.
4. **Grip vs the tile's own hover targets** — the sparkline tooltip must still work everywhere except the 14×14
   corner, and the grip must be usable on an S tile (14×14 inside 160×80).
5. **Ctrl+= / Ctrl+Shift+= / numpad Ctrl+Add**, on a non-US layout too (S1).
6. **Repeated Ctrl+Plus** on the same tile — in Free, and in Auto including a tile in a *collapsed* group and a
   tile scrolled out of view (focus restoration through `FocusTileById`).
7. **Esc** cancels mid-drag with nothing applied; Esc with the picker flyout open still closes the flyout.
8. **1:1 tracking at UI scale 0.9 and 1.3** for both the resize drag and the two move drags — this closes
   `docs/layout-modes/REVIEW.md` owner item 3.
9. **Size submenu** shows "Larger Ctrl++" / "Smaller Ctrl+-" and greys each at L / S.
10. **Drag back to the current size** — no re-template flicker and no `settings.json` write (check the file's
    modified time).

## Fix wave

Applied by the controller on top of `6727edf`: **B1** fixed (the template presenter is named `TileContent` and the
container's `IsKeyboardFocusWithin` trigger stamps `Tag="FocusWithin"` on it; `TileMenuButtonStyle` gains a
`Tag`-based DataTrigger, inert in Auto) — `v14-layout-resize-grip-dark-amber.png` re-taken and now shows both the
"⋯" button and the grip on the focused tile; **S1** fixed (Ctrl required, Shift tolerated, Alt/Win rejected);
**S2** fixed (`TryStepTileSize` ignores the keys while a grip drag is in flight); **S3** fixed (`_resizeAdornerLayer`
cached at add time and used for removal); **S4** fixed (the Free canvas Grid is wrapped in an `AdornerDecorator`).
`v13-layout-free-dark-amber.png` re-taken after the fix: byte-identical to the branch base. Gates: build 0 warnings,
816 + 203 tests green.
