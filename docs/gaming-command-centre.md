# Gaming command centre

Development branch: `feature/gaming-command-centre`. These changes are not yet published.

## Where to find it

- **Tray → Recordings / post-game report:** record or open a session, inspect its report, click a sampled
  slow-frame entry to move replay to that time, and inspect concurrent sensor readings.
- **Session lab → A/B:** enter separate start offsets and one shared duration, then open the second
  recording. Add at least two A and two B recordings for repeated-run variation. Refresh after changing
  windows; Clear removes the comparison list, not files. Coverage includes missing time bins.
- **View → Beta lab → Gaming:** map an executable base name to existing layout, overlay scene, theme
  and named alert-set choices. Enable passive automation and optionally automatic recording. It uses
  FPS capture already enabled by your metric selections; it never starts tracing automatically.
- **Beta lab → Compound alerts:** save, apply or delete named rule sets. Test-only rules remain available.
- **Tray → Edit overlay:** add/select cards, drag or resize, use the numeric position/size fields, choose
  Compact/Analysis/Synthwave, then Apply. Focus a card's drag surface to nudge with arrow keys. Changes
  are drafts until Apply; Reset to auto restores automatic layout without removing selected metrics.
- **Ctrl+Shift+F**, or the tray toggle, shows only selected FPS metrics for this session. It does not
  enable new metrics or change your saved selection. If another app owns the hotkey, use the tray menu.
- **Beta lab → Notebook:** add local tuning notes and optional `.stats-session.jsonl` A/B paths, save,
  and compare linked recordings. Entries do not alter clocks, voltages, power limits or fan settings.
- **Beta lab → Support / Diagnostics:** copy your feedback or export exactly the displayed allowlisted
  diagnostic JSON. Nothing is uploaded or attached automatically. See [beta testing](beta-testing.md).

## Important limits

Game profile choices persist after leaving the game, including theme and active alert rules. Another
profile or a manual change replaces them; automatic restoration is not implemented. Reports appear
without taking focus away from a game.

Recordings contain poll-sampled metrics, not an exhaustive per-frame trace. Slow-frame entries are
the largest sampled frame times in the retained replay window, not proven hitch events. Concurrent
sensor changes and correlation do not establish a cause. Reports identify full-recording totals and
window-only results. Comparing like-for-like workloads and repeating both runs remains essential.

Comparison windows retain at most 3600 snapshots and repeated groups accept ten recordings each.
Output prioritizes frame metrics and displays up to twelve metrics. Notebook limits are 100 entries,
1000 characters per text field and 512 KB total. A notebook that fails to load is not overwritten;
back it up and repair/move it aside while Stats is closed, then restart before saving a new notebook.

Custom overlays support 32 cards on a 1920×1080 logical canvas. Scenes save geometry locally; portable
export of custom-canvas scenes currently reports an explicit unsupported message. If metric selection
adds a card without saved geometry, the overlay uses automatic layout so the new metric stays visible;
saved geometry is retained. Reopen the editor to arrange the selection.

## Native testing still needed before publishing

Launch Stats normally on Windows (not from the Store-hosted tool terminal), test Helldivers 2 or Arc
Raiders at 0.5-second polling, foreground/Alt-Tab transitions and capture restart. Verify automatic
recording/report completion, hotkey conflicts, drag/resize/keyboard behavior, mixed-DPI monitor movement,
and already-open window theme changes. Preview screenshots and simulated tests are not ETW or hardware
verification. No overclock, fan policy or update installer is exercised by the preview harness.
