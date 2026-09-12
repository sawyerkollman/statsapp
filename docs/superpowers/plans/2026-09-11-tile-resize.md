# Tile resize by drag — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-tile-resize-design.md`. Branch `feature/tile-resize` from `feature/v1.10`,
worktree `C:\claude-projects\Stats-wt\tile-resize`. Gates at every checkpoint: `dotnet build --nologo` (0 warnings)
and `dotnet test --nologo` (green, 0 warnings). Commit prefixes per CLAUDE.md. One Sonnet implementer per task with
the listed files as its only write scope; the controller re-runs the gates and commits.

- [x] **Task 1 — Core** (`feat(core): tile size nearest/step helpers, resizable flag, same-size guard`).
      Owns: `src/Stats.Core/Settings/TileDimensions.cs`, `src/Stats.Core/ViewModels/DashboardViewModel.cs`,
      `src/Stats.Core/ViewModels/MetricTileViewModel.cs`, `src/Stats.Core/ViewModels/TileEditRecords.cs`,
      `tests/Stats.Core.Tests/DashboardLayoutTests.cs`, `tests/Stats.Core.Tests/DashboardViewModelTests.cs`,
      `tests/Stats.Core.Tests/DashboardLayoutModeTests.cs`. Does: `TileDimensions.Nearest/Step`, `SetTileSize`
      same-size early return, `StepTileSize` + `TileSizeStep` record + `StepTileSizeEditCommand`,
      `MetricTileViewModel.IsResizable` set in `RebuildSections`; all Core tests in the spec's Acceptance.
- [ ] **Task 2 — App** (`feat(app): resize grip, outline adorner, Ctrl+Plus/Minus, canvas-coordinate drags`).
      Owns: `src/Stats.App/Controls/TileCanvasItemsControl.cs` (new), `src/Stats.App/Controls/ResizeOutlineAdorner.cs`
      (new), `src/Stats.App/Views/DashboardWindow.xaml`, `src/Stats.App/Views/DashboardWindow.xaml.cs`,
      `src/Stats.App/Views/TileTemplates.xaml` (only the new `TileResizeGripStyle`), `src/Stats.App/Views/Icons.xaml`
      (only `Icon.ResizeGrip`), `README.md`. Does: everything in the spec's App section, including the move-drag
      canvas-coordinate correction and the Size submenu items.
- [ ] **Task 3 — Harness + evidence** (`feat(tools): tile-resize preview substates and captures`, then
      `docs: tile-resize evidence`). Owns: `tools/Stats.UiPreview/PreviewComposition.cs`,
      `tools/Stats.UiPreview/Views/CaptureHost.cs`, `tools/Stats.UiPreview/SubstateCatalog.cs`,
      `tools/Stats.UiPreview/captures/baseline.json`, `tests/Stats.UiPreview.Tests/TileResizeSubstateTests.cs` (new),
      `docs/tile-resize/EVIDENCE.md` (new). Does: the substates, manifest entries, tests, captures (new ones plus the
      re-taken layout/Auto captures with the pixel comparison), evidence report incl. "What the harness could not show".
- [ ] **Task 4 — Review**: Opus whole-branch review → `docs/tile-resize/REVIEW.md`; fix wave; gates + captures re-run;
      PR `feature/tile-resize` → `feature/v1.10` with the spec's owner checklist.
