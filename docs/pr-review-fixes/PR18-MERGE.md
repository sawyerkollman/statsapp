# PR #18 base update after #17

Merge feature/v1.10 `632a639` into game-tiles `59d0abf`. Six conflicts resolved as unions: README, dashboard
tile-menu helpers, settings tests, chart-rendering regression tests, substate catalog, and capture manifest.
The manifest contains 82 unique outputs; full chart and histogram buffer-reuse tests remain present.
Resize/focus/keyboard fixes coexist with Histogram/FPS-summary choices, finite histogram arithmetic, enum
fallbacks, and non-color/accessibility severity cues. Notification code remains unchanged from the merged base.

Final build: zero warnings/errors. Tests: 888 Core + 237 preview passed, none failed/skipped.
Two Terra workers had disjoint three-file ownership; parent validated; independent Sol reviews source and PNGs.
Role routing reuses the session preflight; effective model metadata remains unobservable.

Post-commit isolated WPF capture command (from this branch):

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch C:/claude-projects/Stats/artifacts/pr18-evidence/captures.json`

Four local PNGs/sidecars cover critical FPS-low cues in Dark/Light, resize grip at 1.3 application scale, and
notification-off settings. Actual DPI is 96; dashboard canvases include 39 transparent bottom rows.
Static simulated captures do not validate native drag/Escape/menu focus, live theme transitions, actual 150% DPI,
Windows notifications, tray/hotkeys, PresentMon, or physical fan/hardware behavior. Production startup, real
notification sending, and hardware/fan writes are not invoked by this preview.
