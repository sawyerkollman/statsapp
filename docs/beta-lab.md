# Beta Lab testing guide

Integration branch: `feature/beta-lab`. This is not a published update or release tag.

## Try it

- Dashboard **View → Recordings**: record/open a session, choose a metric, scrub or play at two samples
  per second, navigate bounded windows, bookmark an experiment, and open a second recording for A/B.
  A/B compares the first bounded windows over their common elapsed duration. It reports paired-bin
  coverage, absolute/relative differences and both runs' notes. Detective reports association, not cause.
- **View → Theme studio**: draft/apply/save/import/export designs. CRT Terminal, Arctic Glass, Reactor
  and Deep Space join the existing themes. Gradients/reactive decoration appear in Auto layout with a
  creative palette; reactive art also requires Graph effects. Overlay colors remain pinned.
- **View → Scene builder / overlays**: save current tile arrangement and overlay style, customize
  Auto-layout group labels, and apply a whole scene or its overlay alone. Export includes authored
  labels, but excludes private notes and sensor IDs. Imports require explicit compatible-sensor mapping.
- **F11** / Cinema mode: fullscreen the current monitor; **Escape** restores the previous window.
  Move the window to the desired monitor before entering cinema.
- **View → Beta lab**: configure per-game layout/overlay choices and optional recording, compound
  AND alerts, minute history, timeline bookmarks and reviewed local diagnostic export.

## Safety and limits

Automatic gaming and history are off by default. Gaming needs already-active frame readings;
Beta Lab never starts ETW or enables fan control on its own. Manual recordings are never taken over.
Game detection uses a five-second entry and twenty-second exit hold. Game names are executable base
names; layout/overlay scene names must match saved entries.

Compound rules start in test-only mode. Missing operands or a gap longer than three configured polls
break a hold. Existing global alert/notification controls still apply. Each sustained episode raises once.

History stores minute min/mean/max/count for up to 128 selected metrics, with a bounded background
queue, 1–90-day retention and 16–512 MB cap. Retention only removes date-named compact-history files,
never saved recordings. After a storage/clock error, adjust limits, Save, then Restart history writer.

Replay retains at most 3600 samples per metric. Window changes stream the source file on a background
task; very large recordings can take time because there is no seek index. Library lists are capped at
200 files. Notes are bounded to 100 bookmarks of 1000 characters each.

Diagnostics are previewed before export and never uploaded. They contain version/runtime/counts and
Stats' own process memory, CPU time and uptime, sampled when the lab opens. CPU percentage is an
average since process launch in one-core units, not an instantaneous reading or hardware benchmark.
Tuning advice is an experiment checklist; it never changes clocks, voltages or fans.

## Acceptance

See `docs/superpowers/specs/2026-09-15-beta-lab.md` for contracts and the final validation record.
Real hardware/PresentMon, long-duration production soak, native popup/hotkey and actual 150% Windows
DPI checks remain separate from isolated, simulated-data WPF captures.
