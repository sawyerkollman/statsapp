# PR #16 base update

Merge feature/v1.10 `518c93b` into toast-alerts `aac0e42`, retaining notification behavior and the validated
layout/graph fixes. Three explicit conflicts resolved: AppSettings, SettingsServiceTests, and baseline captures.
Relative to the updated base these add only two toast settings, three compatibility tests, and four captures.
All 68 output paths are unique; the repeated alerts/ongoing scenario intentionally compares the unchanged log.

One repair removed duplicated graph settings/tests caught by the initial build and independent review.
Final build: zero warnings/errors. Final tests: 828 Core + 208 preview passed, none failed/skipped.
Terra performed the bounded resolution; parent validated; independent Sol source review found no remaining issues.
Role routing reuses the session preflight, with effective model metadata unobservable.

Post-commit WPF capture command (from this branch):

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch C:/claude-projects/Stats/artifacts/pr16-evidence/captures.json`

Four simulated RTB captures cover notification settings in Dark/Light, notification-off dependent control state,
and the ongoing alert log. PNGs/sidecars are local under `C:/claude-projects/Stats/artifacts/pr16-evidence/`.
Actual DPI is 96; dashboard canvases include 39 transparent bottom rows. No real notification, sound, App startup,
hardware, fan write, or PresentMon is invoked. Actual toast delivery/click activation, foreground gating, Windows
quiet-time behavior, tray operation, native keyboard/focus, and 150% DPI remain manual checks.
