# Tile resize by drag in Free and Snap layouts — design

Date: 2026-09-11. Base: `feature/v1.10` @ `0652685` (master v1.9.2 + dashboard layout modes + graph effects).
Branch `feature/tile-resize`. Supersedes the layout-modes spec's non-goal "no resizing by drag"; "no per-mode tile
sizes" still holds (one `TilePref.Size` per tile across modes). Rules 4/5/6 (H.NotifyIcon pin, LHM flags, fan
safety) are untouched; no NuGet changes.

## Goal

In **Free** and **Snap to grid** layouts a tile can be resized by dragging a bottom-right grip. The drag is a picker
for the three existing presets (S 160×80, M 224×144, L 460×192 from `TileDimensions.Of`), not free-form sizing:
while dragging, an outline shows which preset the current rectangle is nearest to; on release that preset is applied
through the existing `DashboardViewModel.SetTileSize`, so `TilePref.Size` stays the single source of truth and the
tile re-templates exactly as it does from the tile menu today. **Ctrl+Plus / Ctrl+Minus** step the focused tile's
size S→M→L / L→M→S in **any** layout mode (the accessible equivalent). In Auto nothing is drawn and nothing changes.

## Owner decisions (already made — do not re-litigate)

1. Sizes remain the three fixed presets; the grip drag is a picker for S/M/L, not free-form sizing.
2. During the drag an outline shows the candidate size (the nearest preset to the dragged rectangle); the tile itself
   does not re-template until release.
3. Only in Free/Snap (Auto keeps the WrapPanel and the existing size menu); the grip is visible on hover and keyboard
   focus, hidden otherwise, and excluded from the move-drag hit test.

## Owner decisions assumed

1. **Nearest preset** = smallest Euclidean distance between the dragged (width, height) and each preset's (W, H);
   ties resolve to the larger preset (a drag that lands exactly between two sizes reads as "grow").
2. **Grip placement**: the grip is not added to the five tile `DataTemplate`s. The Free canvas `ItemsControl`
   becomes a `TileCanvasItemsControl` (new, `Stats.App/Controls`) whose containers are `ContentControl`s with a
   `ControlTemplate` of `Grid { ContentPresenter, grip }`. The Auto `ItemsControl` and every `DataTemplate` are
   untouched, so the Auto path is byte-identical and the after-gallery captures do not change.
3. **Grip look**: a 14×14 `Border` (`x:Name="TileResizeGrip"`) holding a 3-dot diagonal glyph (`Icon.ResizeGrip`,
   new geometry in `Icons.xaml`), bottom-right of the tile's visual bounds (container margin 6 = `TileBorder`'s
   margin), `Cursor="SizeNWSE"`, `AutomationProperties.Name="Resize tile"`, `ToolTip="Drag to resize (S / M / L)"`,
   `Focusable="False"`. Visible when the container `IsMouseOver` or `IsKeyboardFocusWithin`, else `Collapsed` (the
   `TileMenuButtonStyle` recipe). Shown at every size, including S.
4. **Live feedback** = a `ResizeOutlineAdorner` (new, `InsertionAdorner` pattern: `IsHitTestVisible=false`, accent
   brush read per render) on the container drawing (a) the raw drag rectangle as a 1 px dashed `BorderDim` outline
   and (b) the candidate preset rectangle as a 1.5 px `AccentBrush` outline with the preset letter ("S"/"M"/"L")
   in the corner. The real tile is not touched during the drag.
5. **Drag threshold and cancel**: a grip drag below `SystemParameters.Minimum*DragDistance` is a click (no change);
   Esc during the drag cancels (releases capture, removes the adorner, applies nothing).
6. **No-op guard** lives in the view model: `SetTileSize` returns without rebuilding or saving when the pref already
   holds that size (`DashboardViewModelTests` that assert one save per op use a *different* size; a same-size test is
   added asserting zero saves).
7. **Keyboard**: `Ctrl+OemPlus` / `Ctrl+Add` → larger, `Ctrl+OemMinus` / `Ctrl+Subtract` → smaller; **saturate**
   at S and L (no wrap; a saturated press is a no-op, `e.Handled = true` so the ScrollViewer never sees it). Works
   in Auto and Free/Snap on the focused tile container. After the size-triggered `RebuildSections`, focus is
   restored to the new container for the same tile id (`Dispatcher.BeginInvoke` at `DispatcherPriority.Loaded`,
   searching the Free canvas or the Auto nested `ItemsControl`s by `DataContext.Definition.Id`) so repeated presses
   keep working and the grip stays visible.
8. **Position never changes on resize** (top-left anchored; Snap does not re-snap the position); a grown tile may
   overlap neighbours (base decision: overlap allowed) and extends the canvas extent (`SetTileSize` already calls
   `RecomputeCanvasExtent`). No auto-shift.
9. **Canvas-coordinate deltas**: the resize measures mouse positions against the canvas (`e.GetPosition(canvas)`),
   which sits under the UI-scale `LayoutTransform`, so deltas are 1:1 at every `DashboardUiScale`. The existing
   Free move handlers (`FreeTile_*`, `CoreMatrixBlock_*`) measure against the window and are corrected the same way
   in this branch (a pre-existing gap noted in `docs/layout-modes/REVIEW.md` owner item 3).
10. **Size submenu**: the tile menu's Size submenu gains "Larger" (`InputGestureText="Ctrl++"`) and "Smaller"
    (`Ctrl+-`) items after a separator, disabled when saturated.
11. **Core-matrix block** is not resizable (it has no `TileSize`).

## Settings

No new `AppSettings` field and no `SettingsChange` member: `TilePref.Size` already persists. Rule 3 holds trivially.

## Core (`Stats.Core`, WPF-free, testable)

- `TileDimensions.Nearest(double width, double height) → TileSize` (in `Settings/TileDimensions.cs`): Euclidean
  distance to each preset's (W, H); ties → larger. Negative/NaN inputs are treated as 0 (→ S).
- `TileDimensions.Step(TileSize size, int delta) → TileSize`: `Math.Clamp((int)size + delta, 0, 2)` — saturating.
- `DashboardViewModel.SetTileSize(string id, TileSize size)`: early return when `PrefFor(id).Size == size` (no
  rebuild, no save). Otherwise unchanged.
- `DashboardViewModel.StepTileSize(string id, int delta)`: `SetTileSize(id, TileDimensions.Step(current, delta))`;
  plus `record TileSizeStep(string Id, int Delta)` in `TileEditRecords.cs` and `[RelayCommand] StepTileSizeEdit`.
- `MetricTileViewModel.IsResizable` (`[ObservableProperty] bool`): set by `DashboardViewModel.RebuildSections` for
  every tile to `LayoutMode != Auto` (and re-set in `OnLayoutModeChanged`'s rebuild). The view binds the grip's
  existence to it, so Auto never shows a grip even if a template is reused.

## App (`Stats.App`)

- **`Controls/TileCanvasItemsControl.cs`**: `sealed class TileCanvasItemsControl : ItemsControl` overriding
  `GetContainerForItemOverride() => new ContentControl()` and `IsItemItsOwnContainerOverride(item) => item is
  ContentControl`. Nothing else.
- **`DashboardWindow.xaml`** Free canvas: `<controls:TileCanvasItemsControl>` replaces the Free `ItemsControl`
  (`ItemsSource`, `ItemTemplateSelector`, `ItemsPanel` unchanged). `ItemContainerStyle` now targets `ContentControl`:
  the same `Canvas.Left/Top` setters, `Focusable`/`IsTabStop`/`FocusVisualStyle`, and the same `EventSetter`s as
  today (`FreeTile_PreviewMouseLeftButtonDown/Move/Up`, `LostMouseCapture`, `MouseRightButtonUp`, `Button.Click`,
  both `PreviewKeyDown` handlers), plus a `Template`:

  ```xml
  <ControlTemplate TargetType="ContentControl">
      <Grid>
          <ContentPresenter/>
          <Border x:Name="TileResizeGrip" Style="{StaticResource TileResizeGripStyle}"
                  Visibility="{Binding IsResizable, Converter={StaticResource BoolToVis}}"/>
      </Grid>
      <ControlTemplate.Triggers>
          <!-- grip shown only while hovered or focused-within; collapsed otherwise (same recipe as TileMenuButtonStyle) -->
      </ControlTemplate.Triggers>
  </ControlTemplate>
  ```

  `TileResizeGripStyle` (in `TileTemplates.xaml`, next to `TileMenuButtonStyle`): 14×14, `HorizontalAlignment=Right`,
  `VerticalAlignment=Bottom`, `Margin="0,0,8,8"` (6 container margin + 2 inset), `Cursor=SizeNWSE`, `Background`
  transparent (hit-testable), a `Path` child with `Icon.ResizeGrip` filled `TextSecondary`, `Focusable=False`.
- **`DashboardWindow.xaml.cs`**:
  - `FindAncestorNamedOrSelf(DependencyObject? source, string name, DependencyObject bound)` (sibling of
    `FindAncestorButtonOrSelf`, bounded at the container). In `FreeTile_PreviewMouseLeftButtonDown`, after the button
    exclusion and before the double-click check: if the source is inside `TileResizeGrip`, start a **resize** drag
    (`_resize*` fields mirroring `_freeDrag*`: container, tile id, start point in canvas coords, origin size from
    `tile.Width/Height`, exceeded-threshold flag, adorner), `container.Focus()`, `CaptureMouse()`, `e.Handled = true`;
    return. A double-click on the grip does nothing.
  - `FreeTile_PreviewMouseMove`: if a resize is in flight, compute `w = originW + dx`, `h = originH + dy` (canvas
    coords, min 1), `candidate = TileDimensions.Nearest(w, h)`, and update the adorner (raw rect + candidate rect +
    letter). No VM calls per pixel.
  - `CommitFreeResize()` (from `PreviewMouseLeftButtonUp` and `LostMouseCapture`, same nulling-before-release order as
    `CommitFreeDrag`): remove the adorner, release capture, and if the threshold was exceeded and the candidate
    differs from the current size, call `vm.SetTileSize(id, candidate)` **after** the container reference is dropped
    (the rebuild destroys it), then `FocusTileById(id)`.
  - `PreviewKeyDown` on the window level (or the shared `Tile_PreviewKeyDown`): `Escape` while a resize is in flight
    → cancel (adorner removed, capture released, nothing applied). `Ctrl` + `OemPlus`/`Add`/`OemMinus`/`Subtract` on
    a tile container (Auto or Free) → `vm.StepTileSize(id, ±1)` then `FocusTileById(id)`; `e.Handled = true`.
  - `FocusTileById(string id)`: `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, …)` that walks the Free canvas
    containers (`ItemContainerGenerator.ContainerFromItem`) or, in Auto, each section's nested `ItemsControl`, and
    calls `Focus()` on the container whose `DataContext` is the tile with that id.
  - Move drags (`FreeTile_*`, `CoreMatrixBlock_*`): positions measured with `e.GetPosition(canvas)` where `canvas` is
    the container's `Canvas` parent (`VisualTreeHelper.GetParent(container)`), instead of `this`.
  - `OpenTileMenu` Size submenu: separator + "Larger" / "Smaller" `MenuItem`s with `InputGestureText`, enabled only when
    not saturated, invoking `StepTileSizeEditCommand`.
- **`Controls/ResizeOutlineAdorner.cs`**: `Adorner` over the container; properties `RawRect`, `CandidateRect`,
  `CandidateLabel`; `OnRender` draws both rectangles in canvas-local coordinates offset by the container's margin,
  reading `AccentBrush`/`BorderDim`/`TextPrimary` from `Application.Current.Resources` each render.
- **`Icons.xaml`**: `Icon.ResizeGrip` — three dots on the diagonal (e.g. circles at (10,10), (6,10)/(10,6), (2,10)/(6,6)/(10,2) in a 12×12 box).
- **`README.md`**: the Dashboard layout bullet gains "Resize: in Free or Snap, drag the grip in a tile's bottom-right
  corner; the outline shows which size (S, M, L) it will snap to. Ctrl+Plus / Ctrl+Minus resize the focused tile in
  any layout." The Tiles bullet mentions the two shortcuts next to the size menu.

## Preview harness

- `PreviewComposition.ApplySubstate`: `layout-resized` — set `LayoutMode = Free`, then `SetTileSize` on three seeded
  tiles (one to S, one to L, one to M-from-S) so the capture shows mixed sizes with top-left anchoring and an extended
  canvas. VM-level, deterministic.
- `CaptureHost.ApplyVisualSubstates`: `layout-resize-grip` — after `layout-free`, focus the first Free container
  (`Keyboard.Focus`) so the grip renders; `WaitFrames`.
- `SubstateCatalog` `["dashboard"]` gains both. `baseline.json`: `layout-resized` × Dark Amber + Light and
  `layout-resize-grip` × Dark Amber, `dense` scenario, 1180×720, `rtb`, under `artifacts/ui-polish/layout/`.
- Harness tests (`tests/Stats.UiPreview.Tests/TileResizeSubstateTests.cs`): catalog accepts both substates for the
  dashboard view and rejects them elsewhere; `layout-resized` leaves every tile with a non-null position, the three
  resized tiles at their sizes, and `CanvasWidth/Height` ≥ the largest tile's right/bottom + Gap; `IsResizable` is
  true for every tile in Free and false after switching back to Auto.
- **Cannot be captured** (owner checklist): the in-flight outline adorner and the cursor shape (no `SendInput`), the
  snap on release, Esc-cancel, Ctrl+Plus/Minus (modifiers are read from the real device), focus restoration, and
  the UI-scale 1:1 correction.

## Non-goals

Arbitrary pixel sizes; per-mode sizes; resizing the core-matrix block; touch/pen; resizing in Auto by drag; a
setting to hide the grip; changing the three preset dimensions; changing the seed pack.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings.
- **Tests (Core)** in `tests/Stats.Core.Tests/DashboardLayoutTests.cs` (`TileDimensionsTests`):
  `Nearest_ExactPreset_ReturnsIt` (theory over S/M/L); `Nearest_Midpoints_TieGoesLarger` (the exact midpoint between
  S and M → M, between M and L → L); `Nearest_TinyOrNegative_ReturnsS` ((0,0), (-5,-5), (NaN,NaN));
  `Nearest_Huge_ReturnsL` ((2000, 900)); `Nearest_WideButShort_PicksByDistance` ((400, 80) → M, since
  M (224,144) is nearer than L (460,192)); `Step_SaturatesAtEnds` (S,-1 → S; L,+1 → L; M,+1 → L; M,-1 → S).
- **Tests (Core)** in `DashboardViewModelTests.cs` / `DashboardLayoutModeTests.cs`: `SetTileSize_SameSize_NoRebuildNoSave`;
  `StepTileSize_UpAndDown_WritesThroughAndSavesOnce`; `StepTileSize_AtL_Up_IsNoOp`; `StepTileSizeEditCommand_Delegates`;
  `IsResizable_TrueInFreeAndGrid_FalseInAuto_AndAfterModeSwitch`; the existing
  `TileSizeChange_InFreeMode_UpdatesCanvasExtent` still passes.
- **Tests (harness)**: the four `TileResizeSubstateTests` above.
- **Captures**: `layout-resized` (Dark Amber, Light), `layout-resize-grip` (Dark Amber); the existing `layout-free`,
  `layout-grid`, `layout-free-placed`, and the Auto `dense` capture are re-taken and pixel-compared to their pre-branch
  versions (Auto must be identical; Free/Grid must be identical except the grip is absent since nothing is hovered).
- **Owner checklist**: grip appears on hover and on keyboard focus only, at S/M/L; dragging shows the dashed raw
  rectangle and the accent candidate outline with the letter; release applies the size with the top-left anchored;
  a drag back to the current size changes nothing (no save, no flicker); Esc cancels; Ctrl+Plus/Minus on a focused
  tile in Auto and in Free, repeated presses keep working (focus restored), saturation is silent; the grip does not
  steal the sparkline hover tooltip except in its own 14×14 corner; move and resize track the cursor 1:1 at UI scale
  0.9 and 1.3; the Size submenu shows "Larger Ctrl++" / "Smaller Ctrl+-" and greys them at the ends.
