# Stats installer — design

**Date:** 2026-08-21 · **Branch:** feature/v1.1-ui · **Status:** approved in conversation, pending spec review

## Goal

A single `Stats-Setup-<version>.exe` that a friend downloads from GitHub Releases,
double-clicks, and ends up with Stats in the Start menu, working CPU sensors, and
an uninstaller. No manual prerequisites. Built by CI from a git tag.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Distribution | GitHub Releases, built by GitHub Actions on `v*` tags |
| Code signing | None. Friends accept one SmartScreen prompt; release notes say so |
| Autostart | Optional installer checkbox, **default off**, via Scheduled Task |
| Installer tech | Inno Setup 6 |
| .NET runtime | Self-contained single-file publish (`win-x64`); no runtime prerequisite |
| PawnIO driver | Official `PawnIO_setup.exe` 2.2.0 embedded, run `-install -silent` only if absent |
| Install scope | Per-machine, `C:\Program Files\Stats`, admin required |

## Repo layout

```
installer/
  Stats.iss            Inno Setup script
  build.ps1            publish → fetch PawnIO (pinned URL + SHA-256) → iscc → dist/
  THIRD-PARTY.txt      PawnIO GPL-2.0 notice + source link; LibreHardwareMonitor MPL-2.0 notice
.github/workflows/
  release.yml          tag v* → test → build.ps1 → GitHub Release with the exe attached
src/Stats.App/Stats.App.csproj
  + Version from $(StatsVersion) (default 0.0.0-dev), Product, Company
  + RuntimeIdentifier/self-contained/single-file publish properties
```

`PawnIO_setup.exe` and `dist/` are git-ignored; the driver installer is fetched at build time, never committed.

## Build (`installer/build.ps1 -Version <semver>`)

1. `dotnet publish src/Stats.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:StatsVersion=<semver>`
   → `publish/Stats.App.exe` (+ native LHM/NotifyIcon files). No `PublishTrimmed` (WPF does not support trimming).
2. Download `https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe`
   to `installer/vendor/`, verify SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`, abort on mismatch. Skip download if a verified copy exists.
3. `iscc /DAppVersion=<semver> /DPublishDir=... installer/Stats.iss` → `dist/Stats-Setup-<semver>.exe`.

Version: CI derives it from the tag (`v1.1.0` → `1.1.0`). Local default `0.0.0-dev`. The same value is stamped into the exe file version and the installer/uninstall entry.

## Installer behaviour (`Stats.iss`)

- `PrivilegesRequired=admin`, `DefaultDirName={autopf}\Stats`, no directory page, fixed `AppId` GUID so newer setups upgrade in place; `CloseApplications=yes` closes a running Stats first.
- Pages: Welcome → third-party notices (`THIRD-PARTY.txt`) → Tasks → Install → Finish ("Launch Stats" checked).
- Tasks: `desktopicon` (default on); `autostart` "Start Stats when I sign in" (default **off**).
- Files: publish output → `{app}`; `PawnIO_setup.exe` → `{tmp}` (not kept).
- `[Run]`:
  - PawnIO: `{tmp}\PawnIO_setup.exe -install -silent`, condition = Uninstall key `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` absent. Existing installs (any version) are left alone — never downgraded. Non-zero exit is logged and shown as a warning; the install **continues** (the app's degraded banner tells the user how to fix it).
  - Autostart task (only if `autostart` ticked): `schtasks /Create /F /TN "Stats" /TR "\"{app}\Stats.App.exe\"" /SC ONLOGON /RL HIGHEST /IT`. A Scheduled Task at highest privilege starts an admin-manifest app at logon without a UAC prompt; a `Run` registry key would prompt every login.
  - Launch: `{app}\Stats.App.exe`, postinstall, checked, `runascurrentuser` not needed (already elevated).
- `[UninstallRun]`: `schtasks /Delete /F /TN "Stats"` (ignore failure if absent).
- Uninstall: removes `{app}`, shortcuts, task. Asks (default **No**) whether to delete `%AppData%\Stats`. **PawnIO is left installed**; the uninstaller's final message says it is a shared component removable from Programs & Features.
- Unsigned: no `SignTool` directive.

## CI (`.github/workflows/release.yml`)

- `on: push: tags: ['v*']` and `workflow_dispatch` (build only, no release).
- `runs-on: windows-latest`; `permissions: contents: write` (job-scoped); no secrets.
- Steps: checkout → `dotnet test -c Release` → `choco install innosetup --version=6.x -y` → `installer/build.ps1 -Version ${GITHUB_REF_NAME#v}` (dispatch: `0.0.0-ci`) → `actions/upload-artifact` of `dist/*.exe` → on tag: `softprops/action-gh-release` with the exe, generated notes, and a fixed preamble (SmartScreen "More info → Run anyway"; requires admin; installs PawnIO if missing).
- Release flow: `git tag v1.1.0 && git push --tags`.

## Verification

- Local: `installer/build.ps1` produces `dist/Stats-Setup-0.0.0-dev.exe`; install it here over the running copy; confirm Start-menu launch, version in Programs & Features, PawnIO step **skipped** (Inno log), task present only when ticked, uninstall removes task and leaves PawnIO.
- PawnIO install branch: cannot be exercised on this machine without removing the driver; verify the condition and command line in the Inno log. Release notes state the first external install is the real test.
- CI: run `workflow_dispatch` once before pushing the first tag.

## Out of scope

Code signing, auto-update, ARM64 build, MSI/MSIX, per-user install, uninstalling PawnIO.
