# PR #19 final base update

Merge feature/v1.10 `6ea41ca` into overlay-sparklines `0517cbd`, combining the reviewed layout, graph, toast,
resize, game-tile, and overlay changes. Four conflicts resolved: settings, settings tests, chart-rendering tests,
and baseline manifest. All 94 unique output paths are retained (82 base + 12 overlay); both chart and histogram
mutable-buffer regression tests remain. Overlay fallback/enum fixes and all earlier fixes are preserved.

Final build: zero warnings/errors. Tests: 914 Core + 251 preview passed, none failed/skipped.
Terra performed the bounded resolution; parent validated; independent Sol review checks shared startup wiring,
resolved source, and actual PNGs. Role routing reuses the session preflight; effective metadata is unobservable.

Post-commit isolated WPF capture command (from this branch):

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch C:/claude-projects/Stats/artifacts/pr19-evidence/captures.json`

Nine local PNGs/sidecars cover empty-overlay status in Dark/Light, populated/off/vertical overlay modes, critical
FPS-low in Dark/Light, resize grip at 1.3 application scale, and notification-off settings. Actual DPI is 96;
dashboard canvases include 39 transparent bottom rows. The fixtures do not initialize hardware or production App,
send notifications, or write fans. Native drag/menu focus, live theme changes, actual high DPI, overlay click-through/
hotkeys/tray, real Windows notifications, PresentMon, and physical hardware behavior remain pending manual checks.
