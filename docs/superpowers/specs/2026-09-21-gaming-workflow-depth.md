# Gaming workflow depth

User approved all six follow-ups on 2026-09-21. Integration branch:
`feature/gaming-workflow-depth`, baseline `8a13100` (beta 6 installer repair).
No release or merge is implied. Keep desktop focus untouched.

## Contract

1. Acquire a machine-wide native single-instance guard before constructing any hardware/services.
   Restrict its owner/ACL to Administrators and SYSTEM; fail closed on an unexpected
   existing guard. Window control stays session-local; other-session owners need manual Exit.
   An installer shutdown request follows the normal poller-stop, fan-Auto, reader-dispose
   path. Wait for process termination; do not forcibly kill a current protocol-aware app.
   Older apps must be closed manually when safe shutdown cannot be negotiated.
   Preserve protected update staging. Report installer outcomes and expose retry.
2. A dedicated Gaming home reuses Lab state/commands: active executable, actual capture
   status, FPS, recording, profile, and latest report. Missing capture is never healthy FPS.
3. Optional per-game restoration defaults off. Snapshot only appearance/layout/overlay/
   compound-alert settings changed by automation. Restore an area only while it still
   matches the automatic value; manual edits win. Never touch fan controls.
4. Existing overlay editor gains fit/zoom, alignment guides, multiple selection, undo,
   and portable custom-canvas import/export. Draft edits still require explicit Apply.
   Reuse current metric mapping and layout validation. Keyboard focus stays visible.
5. Session library gains game grouping, text filter, persistent pinned baseline and
   compare action. Old recordings remain available as unassigned. Missing/corrupt files
   yield useful errors, not an empty success state.
6. Explicit session-only opt-in records raw frame timing from the one existing PresentMon
   stream; it never enables capture automatically. PID is foreground-qualified on the
   poll path; timestamps are UTC receipt times, not GPU presentation timestamps.
   Store PID and timing, retain v1 loading. Report exact whole-file p50/p95/p99 and
   hitch-frame count (>50 ms, a fixed descriptive threshold, not a causal diagnosis).
   Bound queues and import memory; fail visibly instead of silently losing samples.

## Checkpoints and ownership

- D1: Terra `/root/depth_frames`: FrameRateReader + recording file/recorder/summary and
  focused Core tests. Parent: lifecycle/updater/installer and integration.
- D2: Terra bounded overlay editor and scene portability; separate Terra bounded Gaming
  home/profile-restoration model. Parent serializes composition and shared settings.
- D3: Terra session-library VM/view; parent integrates opt-in raw recording and reports.
- At each freeze: zero-warning build, focused tests, independent Sol review. Final full
  suite, documentation and pending native/visual checklist. No builds during source edits.

## Routing and baseline

Custom Luna/medium, Terra/medium, Sol/high review and validation roles resolved from
`.codex/agents`. Read-only routing probes: `depth_inventory` (Luna), `depth_frames`
(Terra), `depth_review` (Sol). Requested roles are available in the runtime schema;
effective model/session metadata and client version are unobservable. Configured routing
is not independent proof of the effective model. At most two source editors; no worker
delegation/commits/push/config edits; Terra repair limit two.

Inherited unchanged baseline: Release build zero warnings/errors; 1,022 Core + 370
UiPreview tests passed for beta 6. Owner's untracked `.codex/`, `AGENTS.md`, `HANDOFF.md`,
`Stats-UI-Codex-Handoff/` are excluded from this work. Ponytail: reuse native WPF, existing
recording/automation, no dependencies or second capture service.

## Evidence limits

No installed app/installer, real sensors, fan writes, ETW or visible preview is launched.
The existing preview activates windows, so fresh screenshots and native keyboard/DPI/
live-theme/installer/gameplay checks remain pending the user's focus availability.
Headless tests prove only their specified behavior, never physical PresentMon operation.
