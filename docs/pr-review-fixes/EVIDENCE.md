# PR review repairs

## Scope

Implements the findings in `artifacts/pr-review-20260914/REVIEW.md` on local branch `fix/pr-review-integration`.
The user's existing `feature/v1.10` at `26793cd` already includes #14/#15. Local integration adds reviewed #17
(`dfb32ee`), #18 (`bdd117d`), and #19 (`cc115ce`); combined baseline is `b0a7e78`.
#16 had no repair findings and is not included. No GitHub PR has been merged or changed.

Unrelated untracked `.codex/`, `AGENTS.md`, `HANDOFF.md`, and `Stats-UI-Codex-Handoff/` are preserved.
Conflicts in README, DashboardWindow code, substate catalog, settings tests, and preview manifest retained both
features and both sets of tests/captures. The integration baseline builds with zero warnings/errors and passes
878 Core + 239 preview tests.

## Routing and ownership

Runtime is the hosted Codex tool session; exact client version and effective model/session metadata are
unobservable. Parent routing is configured for Sol/high in `.codex/config.toml`; all four custom roles explicitly
pin model/effort. Runtime exposes those roles. No config/trust/permission changes were made.

| Packet | Explicit role/model | Ownership and result |
| --- | --- | --- |
| Preflight labels | `stats_ui_luna`, Luna/medium | Toolbar + enum-test inventory; completed read-only |
| Preflight rendering | `stats_ui_terra`, Terra/medium | Chart resources + test-host mapping; completed read-only |
| Preflight isolation | `stats_ui_reviewer`, Sol/high | Bare-control STA tests avoid process-global Application/theme state; completed read-only |
| Render/data repairs | `stats_ui_terra`, Terra/medium | Three graph controls, histogram maths and focused tests |
| UI repairs | `stats_ui_terra`, Terra/medium | Dashboard keyboard/focus, overlay layout, FPS severity template/VM/tests |
| Parent | Configured Sol/high | Integration, enum fallback siblings/tests, test project WPF enablement, validation/evidence |
| Final review | `review_graph_overlay`, reviewer, Sol/high | Clean: no concrete correctness/regression findings |
| Final visual validation | `validate_review`, validator, Sol/high | All nine actual captures inspected; repaired scenarios pass |

At most two source workers edit concurrently with disjoint ownership. Shared TileTemplates belongs only to the
UI worker. Project files and composition/integration belong to the parent. Workers do not delegate or commit.

## Repair contract

- Invalidate graph/bar geometry when the bound sample/bin buffer changes; retain caching between animation frames.
- Histogram excludes non-finite input and avoids overflow in finite range arithmetic.
- Free/Snap nudges accept unmodified arrows or Shift only; size-menu rebuilds restore tile focus.
- Preserve #17's existing canvas-coordinate drag fix.
- FPS summary exposes the 1% low's severity with a non-color cue and accessible text.
- Invalid numeric-string enum values fall back without discarding other settings. Apply the same guard to the
  existing DashboardLayoutMode converter, which shares the defect with TileKind and OverlayGraphs.
- Empty overlay with enabled, nonempty status receives a bounded fallback width. Ordinary tile-host width and
  the status-off path remain unchanged.

### Overlay design amendment

The original overlay spec assumed `MaxWidth=0` would harmlessly hide an empty selection's status. Actual WPF
rendering produced a blank 24×1190 strip. The user authorized the review's proposed repair: show status at a
bounded width when no metrics are selected. This supersedes that assumption only; status remains opt-in, uses
pinned overlay colors, and never writes hardware state.

## Validation

Frozen repair candidate: `dotnet build Stats.sln --nologo -v:q` passed with 0 warnings and 0 errors;
`dotnet test Stats.sln --nologo --no-build --no-restore -v:q` passed 886 Core + 247 preview tests,
0 failures and 0 skipped. New tests exercise skipped-render buffer reuse, histogram non-finite/extreme values,
invalid enum strings, keyboard modifier filtering, and FPS severity/accessibility.
The WPF test project explicitly retains the System.IO global using removed by WPF SDK defaults.

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch artifacts/pr-review-fixes/captures.json`
produced nine actual WPF RenderTargetBitmap captures, all with zero warnings and no failed entries:

- [Empty overlay, dark](../../artifacts/pr-review-fixes/overlay-empty-status-dark.png) and
  [light](../../artifacts/pr-review-fixes/overlay-empty-status-light.png): readable 246x60 instead of the prior 24x1190 strip.
- [Populated overlay](../../artifacts/pr-review-fixes/overlay-populated-status.png) and
  [status off](../../artifacts/pr-review-fixes/overlay-off.png).
- [Critical FPS low, dark](../../artifacts/pr-review-fixes/fps-low-critical-dark.png) and
  [light](../../artifacts/pr-review-fixes/fps-low-critical-light.png): the average remains normal while the low has a critical glyph.
- [Detail warmup](../../artifacts/pr-review-fixes/detail-warmup.png).
- [Free layout at 0.9 UI scale](../../artifacts/pr-review-fixes/layout-scale09.png) and
  [1.3 UI scale](../../artifacts/pr-review-fixes/layout-scale13.png).

These artifacts are local ignored evidence, not committed binary assets. Captures use 96 DPI; 1180x720 dashboard
canvases contain a 1180x681 client render and transparent bottom padding, not a 720px-tall client.
Independent visual validation passed: both empty-overlay themes retain identical pinned-dark pixels; populated
status and sparklines-off captures are byte-for-byte identical to matching original #19 captures. FPS-low critical
cues are legible in both themes, detail warmup is right-anchored/unclipped, and both UI scales show no new overlap.
Existing limitation: the Package Power maximum footer ellipsizes at both captured scales.
Capture sidecars describe HEAD `b0a7e78` plus tracked repair diff; the new `DashboardKeyboardTests.cs` and
`GraphControlRenderingTests.cs` were also included in the successful test run but are outside that tracked-diff hash.
Independent final code review passed with no concrete correctness/regression findings. The reviewer confirmed
cache invalidation, finite histogram arithmetic, FPS accessibility, overlay fallback, modifier filtering, and the
existing deferred-focus helper reuse. Rendering regression tests directly call OnRender and inspect cache state;
they do not automate dispatcher-to-bitmap invalidation. Native menu-close focus retention and physical keyboard/pointer
interaction remain manual checks; modifier parsing is automated and menu handlers reuse the existing focus helper.
All preview data is simulated. Native PresentMon, real notification/tray behavior, hardware/fan writes, and actual
150% Windows DPI are outside the checks already performed; no hardware verification is claimed.
