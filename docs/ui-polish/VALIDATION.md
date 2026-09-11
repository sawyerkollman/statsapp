# Validation and definition of done

Validation belongs to Sol (or the Sol/Astra parent), independently of the implementation worker. This file is the future acceptance checklist; it contains no completed test results.

## Required gates

| Gate | Pass evidence |
| --- | --- |
| G0 Routing | Role/config discovery and model-request ledger; effective metadata recorded or explicitly unobservable |
| G1 Baseline | Current commit/dirty state and initial build/tests recorded; before screenshots captured before style changes |
| G2 Preview isolation | Preview composition does not invoke production services or touch production settings; automated isolation check and fixture command log |
| G3 Build | `dotnet build --nologo` exits 0 with zero warnings |
| G4 Tests | `dotnet test --nologo` exits 0 with zero warnings; any new behavioral/preview tests included |
| G5 Visual | Actual WPF PNGs inspected; required sizes/themes/scenarios pass; binding/resource failures absent |
| G6 Interaction | Focus, keyboard, menus, picker/settings parity, tile actions and simulated fan commands checked |
| G7 Compatibility | Existing settings selections/order/overrides/profiles preserved; live themes and background behavior checked at appropriate level |
| G8 Independent review | Sol findings resolved against the final candidate, remaining external limits disclosed |

Use the repository's exact build/test commands above; install/restore prerequisites only through the environment's normal permitted flow. Do not silence warnings or remove tests to pass a gate. Baseline/environment failures are recorded separately from introduced failures. For preview tests outside the solution, explicitly add them to the solution or document and run their project command; do not assume `dotnet test` found them.

## Compact visual matrix

Avoid an expensive full Cartesian product. Run this targeted matrix, expanding only to address a detected issue. Capture equivalent before/after states where the old UI supports them. New categories/empty states receive after-only evidence labeled as new.

| Case | Surface/scenario | Environment |
| --- | --- | --- |
| V1 | Dashboard normal | 1180×720, 100% Windows DPI, app scale 1.0, Dark Amber |
| V2 | Dashboard dense + core matrix | 1180×720, 100% DPI, app scale 1.0, Dark Amber |
| V3 | Dashboard/picker/settings | 860×600, 100% DPI, app scale 1.3, Dark Amber; deliberate wrapping, usable flyout |
| V4 | Dashboard normal + settings | 1180×720, 150% actual Windows DPI, app scale 1.0, Light; use a display/desktop large enough to fit |
| V5 | Dashboard dense | 1180×720, 100% DPI, app scale 0.9, Dark Amber; labels remain legible |
| V6 | Already-open dashboard, dropdown, context menu | Live switch through Dark Amber, Blue, Green, Purple, Light, then custom accent; verify brushes refresh |
| V7 | Tile gallery | Each kind S/M/L; thresholds, unavailable readings, long labels/units/limits, menu hover/focus; Dark Amber and Light |
| V8 | Dashboard missing/empty | Read-failure/degraded/game status, history gap, all groups collapsed, no selected metrics |
| V9 | Fans | 760×640 and 560×360, 100% DPI; off/on, Auto/Manual/Curve, modified profile, no channels, conflict/recovery; default theme plus Light check |
| V10 | Peaks/Alerts | 640×480 and 480×240; empty/populated, long metric names, ongoing alert, aligned columns |
| V11 | Details/dialogs | Crosshair and threshold guides, gap, long unit, invalid threshold/hotkey/limit; keyboard focus; Light and Dark Amber |
| V12 | Overlay | Fixed dark surface with parent theme switched to Light, opacity extremes from supported settings, long value/unit, move mode outline |

Logical dimensions are not physical pixels. Record actual DPI, app scale, client/window bounds convention, and screenshot method. Keep a small screenshot gallery per checkpoint; use full-size PNGs for inspection. Do not call resized screenshots an actual DPI test. Verify popup surfaces independently when the capture method excludes their windows.

### Visual pass criteria

- Main values, units, severity and actions never clip or overlap at tested dimensions. Labels may ellipsize only with a full-name path.
- No hover menu covers tile content or causes text reflow. Keyboard focus is visible without hover.
- Numeric columns and tile value baselines remain stable across changing values.
- Consistent padding, control sizing, corner radii, icon treatment, and hierarchy; chart content remains legible.
- Required small text and control/focus contrast meet the design targets with final resolved colors.
- Light-theme and overlay contrast both work; custom accent does not recolor essential text into invisibility.
- Missing values are visibly missing; no false zero or fictitious healthy state. Chart gaps and threshold meaning preserved.
- Record visible tile count before/after at V1/V2; explain a reduction over 20% and justify readability/density tradeoffs.
- No unrelated animation, shadows, or effects that materially increase continuous rendering cost.

## Interaction checks

Use the simulated preview for safe view behavior. Record exact steps/results instead of only static images.

1. Tab through header, open/close picker/settings, change categories, open popup, Escape back, and verify focus returns sensibly.
2. Search/clear/no results; toggle Dashboard and Overlay metric selections; verify original group All/None scope.
3. Exercise rename, size/kind, gauge max, threshold override/clear, Details/double-click, remove, same-group reorder, and cross-group rejection. Use keyboard context menu too.
4. Load a copy of pre-change settings with mixed tile sizes, custom labels/order, theme/accent, disabled groups, thresholds, overlay options, and fan profiles. Compare semantic values after UI navigation/restart; incidental formatting differences in JSON are not failures.
5. Visit every Settings binding/command in the T4/T5 inventory. Validate invalid input, field focus-loss behavior, live theme/scale updates, restart notices, and status/error rendering. Confirm no duplicate view-model subscriptions/timers after category navigation.
6. Simulated Fans: master off/on, mode change per channel without changing another, manual edit, curve source/edit, Identify logging, profile modified/Reload, save/delete/defaults, game-mode selectors, All to Auto, warning dismiss/collapse. Assert expected fake command effects and no real hardware access.
7. Peaks copy produces expected TSV and reset changes the session; Alerts clear/ongoing state remain correct. Details crosshair, units, guide lines, and missing history still work.
8. Overlay/tray/real global hotkey and close-to-tray require separate native runtime verification if unavailable in the fixture host. Mark fixture-only coverage honestly. Do not enable real fan control as part of general UI smoke testing.

## Regression checks and boundaries

Existing core tests are the primary regression coverage for polling, fan safety, thresholds, profiles, and settings compatibility. Add focused tests only when this UI work changes behavior or introduces a meaningful preview-isolation risk. Do not manufacture tests for each padding constant.

Review no sensor polling/thread changes, no resource dictionary lookup regression, no event-handler leak, no duplicated poller/PresentMon launch, and no production settings writes from preview. If production startup composition is touched, independently review lifecycle ordering. Any physical hardware check that is not run stays listed as pending; simulated success cannot satisfy it.

Check monitoring/render performance only to a proportionate level: compare baseline/candidate with identical fixtures and update cadence; look for continued allocations, timer duplication, and material CPU regression while idle/updating. Record method and observations rather than inventing a universal CPU percentage target. Expand profiling only if a regression is observed.

## Final acceptance

Freeze the final source state and record its commit plus dirty diff identity. After final repairs, rerun required build/tests and impacted visual/interaction cases. Parent reviews Sol's evidence and material findings. An actual Windows build and inspected WPF output are required for `visually accepted`. If external capabilities are missing, deliver the implementation and exact outstanding commands with `pending Windows validation` rather than claiming completion.
