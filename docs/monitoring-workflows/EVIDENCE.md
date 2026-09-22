# Monitoring workflows evidence

Status: implementation complete; simulated Windows UI review passed after the heading-spacing repair.
Native hardware and environment-specific checks below remain pending.

Base: `c42264b`; branch `feature/monitoring-workflows`. See the
[implementation ledger](../superpowers/plans/2026-09-15-monitoring-workflows.md) for routing and ownership.
Owner untracked files remain untouched. No release, merge, commit or push performed.

## Baseline

`dotnet build C:\claude-projects\Stats\Stats.sln --nologo`: zero warnings/errors.
`dotnet test C:\claude-projects\Stats\Stats.sln --nologo`: 886 Core + 247 preview tests passed.
Commands required normal SDK/cache access outside the filesystem sandbox. This runner ignores the cwd
parameter for escalated processes; explicit paths and `Set-Location` are used where cwd matters.

Before capture from baseline compiled binaries (source `c42264b`):
[dashboard](../../artifacts/monitoring-workflows/before-dashboard-rtb.png), real WPF RenderTargetBitmap,
1180x720, Dark Amber, simulated values, zero binding warnings. The first screen capture also exists at
`before-dashboard.png`; its metadata collection stalled because Git ran outside the repository. That
preview and its two Git children were stopped; the explicit-cwd RTB rerun completed successfully.
Runtime sidecar diff identities reflect worktree state at capture time, not rebuilt candidate binaries.

## Checkpoint A

Layouts/profiles, lock/undo and Processes built with zero warnings after replacing direct observable
backing-field writes with guarded generated setters. Initial suite: 247 preview passed, 894 Core passed,
one new CPU-clamp test had incorrect input units (10/100 ms correctly yields 2.75%, not 100%). The test
now supplies 10,000/100,000 ms to exercise clamps; rerun pending. Existing tests all passed.

Sol review identified profile-modified state not restored by undo, stale undo after an external
core-matrix toggle, and same-kind selection unnecessarily clearing undo; bounded repairs are underway.
Parent found process Reset racing an in-flight sample on resume; reset now runs on the sampler loop,
obsolete generation output is suppressed, and a blocked-source regression test was added.

Checkpoint A repair rerun passed: **906 Core + 247 preview tests; build zero warnings/errors**.
All three profile findings were repaired and reviewed. Resume now wakes the paused process sampler immediately.

## Recording, comparison and persistent alerts

Core implementations and UI integration are complete; consolidated build and simulated runtime review passed.
Sol review identified disabled session selection, incomplete recorder state transitions, repeated chart-row
replacement, shared cursor binding/cache issues, missing axes, and captured/live mode guards. Repairs add
drain-aware recording controls, stable chart rows, explicit mode switching and binding-preserving UTC cursor.
Timestamped histories keep legacy index charts unchanged. JSONL export streams all rows and preserves an
existing destination on failure. The recorder's bounded queue stops visibly instead of dropping rows.

Alert persistence runs off the UI thread at five-second intervals, captures owned immutable series, and drains
after hardware cleanup. Reviewer findings about interrupted duration text and malformed/oversized imported
context have been repaired. Preview coverage and simulated visual acceptance passed.

Checkpoint B: build **zero warnings/errors**. Independent `stats_ui_validator` ran
Final `dotnet test .../Stats.sln --no-build --nologo`: **929 Core + 252 preview passed**, zero failed/skipped.
Final source review by `stats_ui_reviewer`: **no remaining material findings** after prompt transition
persistence, ordered selected recording columns and one-sample chart rendering were repaired.
Session open/export streams with bounded memory but runs as an explicit synchronous UI operation;
large-file responsiveness is not stress-validated. Background hardware polling/fan control is separate.

## Runtime Windows UI evidence

All captures use real WPF windows with deterministic simulated data, temp-root persistence and no production
App startup. Construction-time binding errors are retained, not discarded by the harness. Every candidate
sidecar reports **zero warnings** at 96 DPI. Independent validator inspected dark/light/narrow renderings.
The one visual defect (comparison title touching unit) was repaired and the comparison images recaptured.
The independent validator re-inspected both comparison recaptures and reported no remaining material visual
findings. Final `git diff --check` passed. Source, documentation and all evidence remain local and uncommitted.

| View | Evidence | Check |
| --- | --- | --- |
| View menu / saved layout | [screen capture](../../artifacts/monitoring-workflows/profile-view-menu.png) | Actual popup, profile name, new commands, lock/undo state |
| Live comparison | [dark](../../artifacts/monitoring-workflows/validator-comparison-live-dark-760x640.png), [light](../../artifacts/monitoring-workflows/comparison-theme-4-light.png) | Separate scales/units, shared UTC axis/cursor, selection and freeze controls |
| Captured comparison | [narrow light](../../artifacts/monitoring-workflows/validator-comparison-captured-light-560x360.png) | Captured title, disabled live controls, bounded scroll |
| Recordings | [dark](../../artifacts/monitoring-workflows/validator-sessions-recorded-dark-760x640.png), [narrow light](../../artifacts/monitoring-workflows/validator-sessions-recorded-light-560x360.png) | Selected metrics, formatted summaries, dates, actions and scrolling |
| Top apps | [dark](../../artifacts/monitoring-workflows/validator-processes-populated-dark-640x480.png), [narrow light](../../artifacts/monitoring-workflows/validator-processes-populated-light-480x240.png) | Column widths, sort/copy controls, grouped simulated processes |
| Persistent alert | [dark](../../artifacts/monitoring-workflows/validator-alerts-context-dark-640x480.png), [narrow light](../../artifacts/monitoring-workflows/validator-alerts-context-light-480x240.png) | Historical date, interrupted state and context action |

Comparison and Sessions each traversed Dark Amber, Blue, Green, Purple, Light and custom teal while the
same window stayed open; all twelve captures had zero warnings. Parent inspected dark/light/blue/custom
comparison and dark/light Sessions. Full set: `comparison-theme-*.png`, `sessions-theme-*.png`.
The final binaries are identified independently of the sidecar's tracked-diff-only hash (new source files
are intentionally still untracked; no staging/commit authorization was inferred):

```
Stats.Core.dll      3ae2979cbea9f46d186ec8d30d8d5973e32b8ccd089f9b9d151099a1c79eb4fd
Stats.App.dll       44e37e0aa549ae1f2579f2827939f7b649db5744d6d0c9a622086a9f72eae06a
Stats.UiPreview.dll 1790c94fe234e786da164ca30e754e4d7150fcb2eab12f1ccf1f274e0b9df7d4
```

## Final outstanding checks

Real PresentMon, physical hardware/fan behavior, native tray notifications, monitor DPI and live clipboard
are not covered by simulated preview captures. These remain explicitly separate from build/unit/UI evidence.
Real process-counter access/permission failures, keyboard focus traversal, long-running recorder queue/disk
failure stress and multi-day session load/export responsiveness also require environment/runtime checks.
No simulated FPS or fake fan write is claimed as hardware verification. No installer/release was produced.
