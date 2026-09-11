# Dashboard layout modes — implementation plan

Spec: `docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md`. Branch
`feature/dashboard-layout-modes` from master `75115aa`. Gates at every checkpoint: `dotnet build --nologo`
(0 warnings) and `dotnet test --nologo` (green, 0 warnings). Commit prefixes per CLAUDE.md.

- [x] **Task 1 — Core model** (`feat(core):`): `DashboardLayoutMode` enum + `AppSettings.DashboardLayoutMode`,
      `TilePref.X/Y`, `AppSettings.CoreMatrixX/Y`, `SettingsService` sanitation, `TileDimensions`,
      `DashboardLayout` statics, `MetricTileViewModel.X/Y/Width/Height`. Tests: settings round-trip/sanitize,
      `TileDimensions`, `Snap`.
- [x] **Task 2 — Dashboard view model** (`feat(core):`): `LayoutMode` + commands, `SetTilePosition`,
      `SetCoreMatrixPosition/Size`, seed pack, `ResetPositions`, canvas extent, `StatusLines`. Tests per the
      spec's acceptance list.
- [ ] **Task 3 — View** (`feat(app):`): converter delegates to `TileDimensions`; free canvas + drag +
      keyboard nudge + status lines + View menu items; Auto path byte-identical.
- [ ] **Task 4 — Harness + docs** (`feat(tools):`/`docs:`): substates and manifest entries; captures in
      `artifacts/ui-polish/layout/`; README section.
- [ ] **Task 5 — Review**: Opus whole-branch review + fix wave; PR to master with owner checklist.
