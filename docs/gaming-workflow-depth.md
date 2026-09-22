# Gaming workflow depth (1.12 beta)

This ships in `v1.12.0-beta.1` on the `beta` channel. The preceding beta 6 code is
promoted separately to stable `v1.11.0`; these new features are not part of that stable release.
Source/automated validation is separate from pending native Windows acceptance.

## Workflow

- Open **Gaming** from the dashboard View menu or tray. Capture status is the real
  PresentMon reader status, not a guess based on whether a game executable exists.
  Recording and report actions reuse the Session lab.
- In **Gaming options**, enable **Restore desktop after game** for the desired executable
  to undo automatically selected appearance/layout/overlay/compound-alert changes on exit.
  This defaults off. A domain edited manually during the game is left alone; fan control
  is never part of this snapshot. This is an in-memory game-run snapshot, not crash recovery.
- In the overlay editor, select multiple cards, align or move them together, zoom/fit and
  undo draft changes. Import/export uses portable metric roles that must be explicitly
  mapped on the receiving PC. Import does not alter the live overlay until **Apply**.
- Session library groups recordings by their game label, filters by text, and retains a
  pinned baseline for comparison. Older files have no game label and remain available.
- **Include per-frame timing** is explicit opt-in for a recording, not permission to start
  FPS capture. Enable frame metrics through the existing controls first. It uses the same
  PresentMon process; no second ETW session, injection or fan changes.

## Measurement limits

Raw frame rows store process ID, frame interval and UTC receipt time. Foreground identity
is updated at the hardware poll cadence, so process switches have that qualification
delay. Receipt timestamps can arrive in batches and are not display-present timestamps.

The raw report computes whole-file nearest-rank p50/p95/p99, mean, maximum, and the number
of frame intervals strictly above 50 ms. That fixed “hitch frame” threshold describes
timing only; it does not identify a cause or prove a perceptible stutter. Poll snapshots,
rolling FPS lows, replay charts and sampled spike correlations retain their original scope.
The CSV export is the poll-snapshot export; the JSONL recording retains raw frame rows.

Raw recording increases disk use. A bounded queue reports an error and leaves a partial
file if the writer cannot keep up; it does not silently drop frames. Exact raw percentile
analysis supports up to 5,000,000 frames and refuses larger files rather than labeling a
truncated result as a whole-session summary. Replay charts retain at most 3,600 snapshots.

## Updater behavior

New versions acquire a machine-wide single-instance guard before hardware initialization,
restricted to Administrators and SYSTEM. A copy in another Windows session must be exited
manually; window activation and graceful-shutdown messages stay in the current session.
The installer asks a protocol-aware installed Stats process to exit normally and waits for
that process. A timeout leaves installation blocked with manual tray **Exit** guidance.
Older copies without that protocol may need manual tray Exit. No image-name force kill is
used. Update failure/reboot/version-mismatch outcomes appear in Settings; check for updates
to retry. Detailed installer logs remain in protected `Windows\Temp\Stats-update-*` staging.

## Acceptance still needed

The user requested no desktop focus changes during development. Fresh screenshots, live
theme/DPI/input checks, second-launch activation, native graceful update/uninstall, and real
PresentMon recording/game transitions must be tested later. Build/tests and simulated data
do not establish those results. Beta publication does not imply those checks have passed.
