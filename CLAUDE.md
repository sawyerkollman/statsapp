# Stats — project guide for Claude Code

Native Windows PC-monitoring dashboard (WPF, .NET 8): CPU/GPU/memory/storage/network sensors via
LibreHardwareMonitor, FPS via a bundled Intel PresentMon, fan control via LHM `IControl`. Solo project by
Sawyer Kollman; repo default branch is `master` (not `main`).

## Commands

| Task | Command |
|---|---|
| Build | `dotnet build --nologo` (must be **0 warnings**) |
| Tests | `dotnet test --nologo` (xunit, `tests/Stats.Core.Tests`; must be **0 warnings** — xUnit analyzers are on, put constants in the `expected` slot) |
| Focused tests | `dotnet test tests/Stats.Core.Tests --filter "FullyQualifiedName~<ClassName>" --nologo` |
| Installer | `.\installer\build.ps1 -Version 1.2.3` → `dist\Stats-Setup-1.2.3.exe` (publish → fetch+SHA-verify PawnIO & PresentMon → Inno Setup 6; ISCC found in `%LOCALAPPDATA%\Programs\Inno Setup 6`) |
| Release | `git tag -a vX.Y.Z -m "…" && git push origin vX.Y.Z` → `.github/workflows/release.yml` builds and attaches the installer to a GitHub Release |
| Run from source | `dotnet run --project src/Stats.App` — needs elevation (app manifest `requireAdministrator`) |

## Layout

- `src/Stats.Core` — everything testable: `Sensors/` (LHM + perf-counter readers, `SensorPoller`, `CompositeSensorReader`, `SensorMapper`), `Metrics/` (`MetricDefinition`, `MetricGroup`, store/history, thresholds), `Frames/` (PresentMon FPS reader), `Fans/` (fan control loop), `Settings/` (`AppSettings` + `SettingsService`), `ViewModels/` (CommunityToolkit.Mvvm).
- `src/Stats.App` — WPF shell: `App.xaml.cs` is the composition root; `Views/` windows (Dashboard, Overlay, Peaks, Fans), `Controls/` custom `FrameworkElement`s, `Converters/`, `Theme.xaml` brushes/converters.
- `installer/` — `Stats.iss`, `build.ps1`, `THIRD-PARTY.txt`; `vendor/`, `publish/`, `dist/` are git-ignored.
- `docs/superpowers/specs/` and `plans/` — design specs and implementation plans per feature (read the spec before touching a subsystem).

## Non-obvious rules (violating these breaks things)

1. **All LibreHardwareMonitor access happens on the poller thread.** `SensorPoller.SnapshotAvailable` fires on its background thread; `FanController.Tick` is subscribed there (before the Dispatcher-marshalling handler). UI code only mutates desired state under the controller's lock; saves triggered from the poll thread are deferred to the end of `Tick` under `_gate`. At exit: `poller.Stop()` → `fanController.RestoreAll()` → `reader.Dispose()` only if the loop joined.
2. **`MetricGroup` is append-only** (serialized by name inside `ThresholdRule`, and `DashboardViewModel.GroupOrder` must list every member).
3. **Settings compatibility:** every new `AppSettings` field gets a default; `SettingsService.Load` null-guards collections and holds the migrations (`ThresholdDefaults.EnsureDefaults`, fan pref sanitation). Old `settings.json` files must keep loading.
4. **H.NotifyIcon.Wpf is pinned to 2.3.2** — the last version with a `net8.0-windows` asset. Do not bump.
5. **LHM `IsMotherboardEnabled`/`IsControllerEnabled` are on** (fan control needs them) — gated by the `ReadMotherboardAndCoolers` setting; changing it needs a restart.
6. **Fan control safety invariants:** master switch default off; `WriteLocked` sets `InSoftware` *before* the write; `ReleaseLocked` only clears tracking when `SetAuto` succeeds; ranges are sanitized at discovery (`FanRange.Sanitize`); pumps floor at 50 %; three failed writes → Auto with status kept.
7. **PresentMon/ETW cannot be tested from the Claude Code shell:** the shell is the Store (MSIX) build of PowerShell; its children inherit package identity and Windows denies ETW sessions even when elevated. Launch Stats from the Start menu / shortcut / a non-Store terminal to test FPS. Never try to run `PresentMon.exe` from here.
8. The app's elevation prompt can't be satisfied from this shell either — `dotnet run` smoke tests are optional; build + tests are the gate.

## Workflow the owner prefers

- Design first (`superpowers:brainstorming`), written spec + plan in `docs/superpowers/`, then Sonnet implementer subagents per task with per-task reviews, the controller (Fable) re-running build/tests itself at every checkpoint, and an Opus whole-branch review + fix wave before the PR.
- Commit messages: `feat(core): …`, `feat(app): …`, `fix(core): …`, `test(core): …`, `docs: …`, `build(installer): …`.
- Push feature branches after committing (owner works from a desktop and a laptop); PRs to `master`.
