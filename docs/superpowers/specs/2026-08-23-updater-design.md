# In-app updater — design

**Date:** 2026-08-23 · **Branch:** `feature/updater` (from master 780fce5) · **Status:** approved in conversation (owner picked "download + install on click")

## Goal
The app checks GitHub Releases for a newer version, shows an unobtrusive notice, and on click downloads the installer and updates in place. No surprise restarts; notify-only never blocks use.

## Behavior
- **Check:** on startup (after a 15 s delay, off the UI thread) and every 24 h, `GET https://api.github.com/repos/sawyerkollman/statsapp/releases/latest` (public, unauthenticated; **must send a `User-Agent` header** or GitHub returns 403). 10 s timeout; any failure → silently no update (never a visible error).
- **Gate:** `AppSettings.CheckForUpdatesAutomatically` (default `true`; checkbox in Settings). Dev builds (assembly version 0.0.0.*) never check or offer.
- **Compare:** parse `tag_name` (`v1.4.1`); numeric part vs current `Assembly.GetName().Version` (first three fields). Tags with a prerelease suffix (`-beta`) are never offered. Asset must be named `Stats-Setup-{tag-without-v}.exe`; missing asset → no offer.
- **UI:** dashboard-top banner `Stats v1.4.2 is available — [Update now] [Later]`. *Later* dismisses for the session. Banner also shows download progress and errors ("download failed — retry").
- **Install flow (on Update now):**
  1. Download the asset to `%TEMP%\Stats-Setup-{ver}.exe` (async, progress reported; verify final size == asset `size` from the API; HTTPS only).
  2. Write `%TEMP%\stats-update.cmd`: loop until this PID exits (`tasklist /FI "PID eq {pid}"` + `timeout /t 1`), run the installer `/SILENT /NOCANCEL`, wait, then relaunch `{current exe path}`.
  3. `Process.Start` the helper hidden, then call the existing `ExitApp()` so fans are released and settings saved by our own clean path — the installer must never have to kill us (`CloseApplications` stays as a backstop only).
  - Elevation: app runs elevated (`requireAdministrator`), so helper → installer → relaunched app inherit it; no extra UAC prompts.

## Layout
- `src/Stats.Core/Updates/UpdateChecker.cs` — pure/testable: `UpdateInfo { Version Version; string TagName; string AssetUrl; long AssetSize; string ReleasePageUrl; }`, `static UpdateInfo? Parse(string latestReleaseJson, Version current)`.
- `src/Stats.Core/Updates/UpdateService.cs` — `CheckAsync(HttpClient? injected)` wrapper + `DownloadAsync(UpdateInfo, string destPath, IProgress<double>, CancellationToken)`.
- App: banner in `DashboardWindow`, properties/commands on `DashboardViewModel`; helper-script writer + launch + `ExitApp()` wiring in `App.xaml.cs`; Settings checkbox.

## Tests (`UpdateCheckerTests`, no network)
Parse: newer offered / equal & older null / prerelease tag null / missing asset null / malformed & empty JSON null / asset picked by exact name / sizes and URLs surfaced. Version edges: 1.4.1 vs 1.4.0, 1.10.0 vs 1.9.9, 2.0.0 vs 1.99.99, current 0.0.0 → always null.

## Non-goals
Delta updates, channel selection, auto-install without click, checking more often than daily, signature verification beyond HTTPS (installer is served by GitHub over TLS; SHA pinning impossible for future releases).
