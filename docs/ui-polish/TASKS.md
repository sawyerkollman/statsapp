# Implementation tasks

Run in dependency order. T0 is orchestration work. Delegate T1–T7 as described; T8 is an independent gate. At each checkpoint, freeze edits and let the parent or Sol validator run build/tests. Existing baseline failures must be recorded before changes. Do not silently weaken warning policies or tests.

Paths are relative to the repository. New paths below are proposed deliverables, not existing files. The parent verifies the current tree first. This plan is authorized design preparation, not evidence these tasks are implemented.

| ID | Owner | Depends on | Output |
| --- | --- | --- | --- |
| T0 | Sol/Astra parent | — | Baseline, capability/routing evidence, branch and ownership ledger |
| T1 | Terra, parent reviews architecture | T0 | Isolated WPF preview host, fixtures, initial screenshots |
| T2 | Terra | T1 | Shared style resources and frozen visual contract |
| T3 | Terra | T2 | Consistent tile templates and core matrix |
| T4 | Terra | T2 | Dashboard toolbar, sections, notices, picker |
| T5 | Terra | T4 | Categorized settings with binding parity |
| T6 | Terra | T2 | Fan window polish |
| T7 | Terra; Luna for explicit mechanical slice | T3, T5, T6 | Peaks/details/overlay/dialog consistency |
| T8 | Sol reviewer + Sol validator, parent accepts | T7 | Final diff review, Windows evidence, resolved findings |

## T0 — preflight

Read root instructions and relevant specs; compare current HEAD with the baseline in SOURCES.md. Record dirty files and preserve them. Create an integration branch according to repo conventions. Inspect Windows/.NET/WPF/render availability and Codex version/config discovery. Run AGENT_WORKFLOW.md routing checks; record available models, requested routes, and observed session metadata. Baseline commands: `dotnet build --nologo` and `dotnet test --nologo` on a capable environment. Record warnings, failures, and environmental limitations distinctly.

Acceptance: concrete task ledger; file owners assigned; workers not silently routed to the parent; no claim of runtime validation when unsupported. T0 can proceed without user screenshots. If routing is blocked, prepare remaining architecture/read-only work while reporting the exact missing capability; do not launch the costly worker batch by fallback.

## T1 — preview harness and baseline capture

Primary owned paths: proposed `tools/Stats.UiPreview/`, proposed `tests/Stats.UiPreview.Tests/`, and evidence output. Parent alone owns additions to `Stats.sln`, project files, and any shared resource extraction/composition seam. If the existing architecture needs a small UI-resource extraction, propose it to the parent before editing those files.

Implement PREVIEW_HARNESS.md. Inspect constructors/commands and resource merge dependencies first. Reuse real views, view models where feasible, templates, converters, and renderers; ensure the preview executable never invokes production App startup. Capture before screenshots with current styling. Record deterministic scenario/version identifiers.

Acceptance: no real sensors, fan writes, ETW, startup task operations, updates, tray background process, or production settings writes. Current real WPF surfaces render using fixtures; popups can be captured; invalid settings scenarios and fan-control states are reproducible. The harness has a documented runnable command and actual output. Isolation/fixture validity gets meaningful automated checks. A Windows limitation remains a blocking visual gate, not a pass.

Checkpoint A: Sol reviews preview isolation and baseline output. Parent freezes resource contract before T2.

## T2 — shared visual foundations

Owned paths: `src/Stats.App/Views/Theme.xaml`, `Controls.xaml`, `src/Stats.App/App.xaml`, any new shared typography/icon dictionary, and `Helpers/ThemeManager.cs` only as required by new tokens. Parent coordinates all project/resource changes. Read the existing theme and v1.7 specs first.

Implement DESIGN.md §2. Keep the existing palette keys and theme presets; add resources only when useful. Update all required palette application paths if adding theme-dependent keys. Respect double-merged Theme.xaml and live top-level brush replacement. Shared HeaderButton availability must precede derived tile styles. Icons should use reusable local geometry; text labels remain. Validate focus, hover, pressed, checked, disabled, and popup states. No dependency upgrade.

Acceptance: all existing theme presets and custom accents work in open windows; no missing resource errors; overlay retains its correct fixed colors; contrast targets measured; existing keyboard behavior preserved. Sol reviewer signs off on the shared contract before downstream parallel editing.

## T3 — tiles and core matrix

Owned paths: `Views/TileTemplates.xaml`, `Converters/TileSizeToLengthConverter.cs`, `Views/CoreMatrixView.xaml`. If needed, parent assigns a narrowly scoped change to `Stats.Core/ViewModels/MetricTileViewModel.cs` and related formatting tests. No independent edits to T2 dictionaries.

Implement DESIGN.md §4 and target size mapping. Prioritize main-value readability in gauges, consistent label/value/footer alignment, reserved menu space, and removing empty optional rows. Keep formatter semantics, severity cues, and interaction handlers. Render every tile kind and S/M/L, including long names, unavailable values, limit labels, and inverted thresholds.

Acceptance: no clipping at required scales; values do not move horizontally as digits change; user serialized tile sizes/IDs/order remain intact; details and context menus remain reachable by mouse and keyboard. Changed formatting gets data-driven behavioral checks, not string-splitting assumptions.

## T4 — dashboard shell and picker

Owned paths: `Views/DashboardWindow.xaml`, `.xaml.cs`; minimal `DashboardViewModel.cs` changes only with parent-assigned scope. Shared resources remain owned by parent/T2 owner.

Implement DESIGN.md §3 and Metrics portion of §5. Group toolbar controls, move Collapse/Expand into View, expose Overlay state, align sections, preserve notices, and add empty/no-results states. Add flyout close/focus behavior. Inventory Settings bindings here before T5 extraction.

Acceptance: actions fit default and narrow sizes, search/live columns and All/None retain semantics, collapse/status and same-group drag reorder still work, flyout escapes/focus are correct. No core monitor changes.

T3 and T4 may run concurrently only after T2 is frozen and all files are disjoint. T6 may replace either parallel slot. Checkpoint B: integrate dashboard/tiles, build/test, inspect selected screenshots.

## T5 — settings organization

Owned paths: same dashboard files after T4 releases them, plus proposed category view(s) under Views and minimal UI navigation state. Parent assigns changes to existing SettingsViewModel or shared project files explicitly. T4 and T5 must never edit concurrently.

Implement DESIGN.md Settings mapping. Prefer view-only category navigation over changing persisted settings. Keep one SettingsViewModel; do not recreate it per category or duplicate controls bound to the same setting. If extracting views, preserve resource lookup, DataContext, event handlers, and command bindings. Build a before/after binding and command checklist.

Acceptance: every current setting and action has a mapped home; live changes, invalid entries, restart flags, update progress/errors, startup status, and theme changes retain semantics; category changes do not discard or unexpectedly apply invalid edits. Add focused checks only for any new navigation logic.

## T6 — fan window

Owned paths: `Views/FansWindow.xaml`, `.xaml.cs`, and UI-only helpers if required. Fan controller, range/safety logic, switcher, and persistence are outside scope. Parent must assess any purported need to edit them.

Implement DESIGN.md §6 using the existing enabled/mode/profile state. Segmented selector must preserve per-channel exclusivity and keyboard navigation. Refine profile row, modified state, game-mode grouping, card alignment, and narrow-window wrapping. Retain warnings and explicit Identify.

Acceptance: fixture captures cover disabled/armed, Auto/Manual/Curve, modified/reload, unavailable, conflict, and recovery states. No template load or view navigation triggers commands or physical writes. All to Auto remains accessible; existing fan tests pass unchanged unless an independently justified behavioral correction is required.

## T7 — secondary surfaces and integration cleanup

Owned paths: `Views/PeaksWindow.xaml`, `MetricDetailWindow.xaml`, `OverlayWindow.xaml`, `InputDialog.xaml`, `ThresholdDialog.xaml`, with their code-behind only if needed. Luna may receive a precise subset of typography/label substitutions after the parent freezes the examples. Terra owns layout and behavior fixes.

Implement DESIGN.md §7. Repair narrow table sizing; make empty states, numeric columns, dialog validation, chart metadata, and overlay reading styles consistent. Do not restyle custom graphs in a way that changes sampling, missing data, or scale meaning.

Acceptance: all required surfaces present; TSV export, reset, chart hover/threshold guides, overlay movement/click-through/hotkey and pinned colors preserved. Checkpoint C: freeze integrated candidate, build/test, collect final screenshots.

## T8 — review, validation, and handoff

Sol reviewer independently inspects the frozen candidate vs the integration base. Sol validator executes VALIDATION.md and inspects actual PNGs. Do not use the same implementation worker as the independent reviewer. Run review and validation sequentially by default to reduce overhead; parallelize only if it shortens a genuine bottleneck.

Parent triages findings and delegates bounded fixes to Terra/Luna. Re-run only affected checks plus the required final build/tests. New edits invalidate affected prior review/visual results. Close all material findings; document any genuine external blockers. Populate EVIDENCE_TEMPLATE.md into an actual evidence report; keep the blank template intact.

Deliver changed files, concise behavior summary, tests/commands, before/after WPF images, model-routing ledger, unresolved hardware/Windows checks, and a review-ready change description. No release/merge. If no runtime evidence exists, status is implementation prepared or implemented pending Windows validation, never visually accepted.
