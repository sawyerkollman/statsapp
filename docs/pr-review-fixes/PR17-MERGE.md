# PR #17 base update after #16

Merge feature/v1.10 `419c588` into tile-resize `68ec123`. Preview conflicts preserve all base graph/layout/toast
cases plus the resize cases and all 71 unique RTB capture outputs. One auto-merged duplicate ParentCanvas helper
was removed; the resize code-behind matches the previously reviewed PR17 implementation.

Final build: zero warnings/errors. Tests: 846 Core + 214 preview passed, none failed/skipped.
Terra performed bounded resolution; parent validated; independent Sol review checks both parents and runtime
evidence. Explicit role routing reuses the session preflight; effective model metadata remains unobservable.

Post-commit simulated WPF capture command (from this branch):

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch C:/claude-projects/Stats/artifacts/pr17-evidence/captures.json`

Four local PNGs/sidecars under `C:/claude-projects/Stats/artifacts/pr17-evidence/` cover the resize grip at 0.9/1.3
application scales, resized tiles in Light, and disabled notification settings. Actual DPI is 96; dashboard canvases
include 39 transparent bottom rows. Static previews do not prove physical drag/resize feel, keyboard focus after a
native menu closes, actual 150% DPI, live theme changes, real toast delivery, hotkeys/tray, PresentMon, or hardware.
No production startup, notification sending, or hardware/fan writes are used for these checks.
