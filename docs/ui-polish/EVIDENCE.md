# Stats UI polish — implementation evidence

Living report for the ui-polish pass (copied from `EVIDENCE_TEMPLATE.md`; the blank template is kept intact).
Placeholders are not passed checks. Status vocabulary per `AGENT_WORKFLOW.md` §7.

## Candidate

- Date/time: started 2026-09-10 (T0)
- Repo/branch: `c:\claude-projects\Stats`, branch `ui-polish` (pre-existing local integration branch, created from `master`)
- Baseline commit: `ad5ec1d` (master, tagged `v1.8.0`) — matches the SOURCES.md baseline, so no path adaptation was needed
- Final candidate commit: _pending_
- Dirty files/diff identity at T0 start: untracked only — `AGENTS.md`, `.codex/`, `HANDOFF.md`, `Stats-UI-Codex-Handoff/`, `docs/ui-polish/`, empty `tools/Stats.UiPreview/Artifacts/`. No tracked file was modified. The Codex-specific files (`AGENTS.md`, `.codex/`, `Stats-UI-Codex-Handoff/`, `HANDOFF.md`) are deliberately left untracked.
- Windows version, .NET SDK, desktop availability: Windows 11 Pro Insider Preview 10.0.26220; .NET SDK 9.0.316 (targets net8.0-windows); interactive desktop session available; two monitors at 96 DPI (100 %): 3440×1440 primary, 2560×1440 secondary
- Client: Claude Code (not Codex). The Codex role files in `.codex/` are not used.
- Parent model/effort: Claude Fable 5.1 (this session); implementers Claude Sonnet, independent review Claude Opus — per the repo's `CLAUDE.md` workflow. Runtime-observed model metadata for subagents: unobservable from inside the session; requested models are recorded per task.
- Overall status: **implemented (T0–T1)**, baseline captured — see task ledger

## Routing ledger

| Task/probe | Role | Requested model/effort | Runtime-observed model or unobservable | Session ID | Attempts | Result |
| --- | --- | --- | --- | --- | --- | --- |
| T0 | parent | Claude Fable 5.1 | this session | — | 1 | done |
| T1 | implementer | Claude Sonnet (Agent tool, model=sonnet) | unobservable | — | 1 | done |

Model self-reports are not runtime proof. The Codex Sol/Terra/Luna routing in `AGENT_WORKFLOW.md` is replaced for this run by the repo's Claude workflow (Sonnet implementers, Opus review, Fable controller); the technical invariants are unchanged.

## Task ledger

| Task | Owner | Files | Dependencies met | Implementation | Review | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| T0 | parent | `src/Stats.App/App.xaml`, new `Views/AppStyles.xaml`, `Views/DashboardWindow.xaml.cs`, `App.xaml.cs`, `docs/ui-polish/EVIDENCE.md` | — | done | parent | build/tests green |
| T1 | Sonnet implementer | `tools/Stats.UiPreview/` (28 files), `tests/Stats.UiPreview.Tests/` (4 test classes, 92 tests), `Stats.sln`, `Stats.App.csproj` (InternalsVisibleTo), `.gitignore` | T0 | done | parent (inspected 12 baseline PNGs, fixed picker live-value refresh) | build/tests green; 55-capture batch ran with 0 binding/resource warnings |
| T2 | | | | | | |
| T3 | | | | | | |
| T4 | | | | | | |
| T5 | | | | | | |
| T6 | | | | | | |
| T7 | | | | | | |
| T8 | | | | | | |

### T0 notes

- **Baseline gates** (before any change): `dotnet build --nologo` → 0 warnings, 0 errors; `dotnet test --nologo` → 674 passed, 0 failed, 0 skipped, 0 warnings.
- **Parent-owned resource seam** (permitted by `PREVIEW_HARNESS.md` "If sharing currently App-only resources requires extraction to a dictionary, the parent owns that limited change"): the styles that were declared inline in `App.xaml` (`GroupHeader`, `SettingsHeader`, `SettingsLabel`, implicit `TextBox`, `TabControl`, `TabItem`, `ToolTip`) moved verbatim to `src/Stats.App/Views/AppStyles.xaml`, merged last in `App.xaml` (Theme → Controls → TileTemplates → AppStyles). The preview host merges the same four dictionaries in the same order without constructing `Stats.App.App`.
- **Dashboard settings seam**: `DashboardWindow` previously reached `(Application.Current as App)?.Settings` for the tile context menu's raw pref kind and the threshold dialog. It now has a `Settings` property the composition root sets (`App.xaml.cs`) with the old lookup as fallback, so the preview host can supply fixture settings and exercise those menu paths.
- **Environment limitation recorded**: both monitors run at 100 % DPI. Validation case V4 (150 % actual Windows DPI) cannot be produced without changing the owner's display scaling; it stays **pending** unless the owner runs the documented command on a 150 % display.

### T1 notes

- Harness: `tools/Stats.UiPreview` (own `PreviewApp : Application`, merges Theme → Controls → TileTemplates → AppStyles by pack URI; real views + real Core view models over `FakeSensorReader`, `FakeFanControlBackend`, `NullFanArmedMarker`, per-run `%TEMP%\Stats.UiPreview\<run-id>` settings root, recording delegates for startup/update/log-folder/restart). Full detail in `T1-REPORT.md`.
- Commands: single capture `dotnet run --project tools/Stats.UiPreview -- --scenario <s> --view <v> --theme "<t>" --width W --height H --ui-scale S [--substate a+b] [--method screen|rtb] --output <png>`; gallery `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json`; live `--interactive`.
- Isolation evidence (G2): `IsolationMetadataTests` scans the built `Stats.UiPreview.dll` metadata for forbidden production types (LHM/perf-counter readers, PresentMon, startup task, updater, hotkey, tray, file marker, trace log, `Stats.App.App`); `CompositionIsolationTests` asserts fake service types and temp-root settings path for every scenario; `FanCommandRecordingTests` proves All-to-Auto/Identify only reach the fake backend.
- Baseline gallery: 55 PNG + JSON sidecars in `artifacts/ui-polish/before/` (git-ignored). Sidecars record commit `b8b9c44`, 96 DPI, `--method screen` (DWM extended-frame rect, includes popups on screen), Invariant culture, `Mountain Standard Time`, fixture time `2026-09-06T14:30Z`, seed 1234, 0 warnings each.
- Visible medium tiles at 1180×720 Dark Amber (manual count from PNG): V1 normal = 8 (Cpu 4, Gpu 3, Memory 1; Storage row cut at the fold). V2 dense = 3 of 8 M tiles (dense mixes S/M/L by design).
- Parent fix after review: harness now calls `DashboardViewModel.RefreshAll()` after opening the flyout, so the picker's live "Now" column is populated as in production.
- Baseline observations feeding T2+: group headers use AccentBrush text (Light accent `#D97B1F` on `#F2F2F4` ≈ 3.0:1, fails the 4.5:1 small-text target); Peaks at 480 wide leaves "Metric" a few pixels; the 440-unit flyout under UI scale 1.3 covers most of an 860-wide window; header actions are Unicode glyphs; tiles S 150×70, M 215×120, L 440×160 with 6 margin / 10 padding / radius 6 / value 26 / label 11 / footer 10.

## Commands and results

| Candidate | Command | Exit | Warnings | Result | Log |
| --- | --- | --- | --- | --- | --- |
| `ad5ec1d` baseline | `dotnet build --nologo` | 0 | 0 | ok | terminal |
| `ad5ec1d` baseline | `dotnet test --nologo` | 0 | 0 | 674 passed | terminal |
| T0 seams | `dotnet build --nologo` | 0 | 0 | ok | terminal |
| T0 seams | `dotnet test --nologo` | 0 | 0 | 674 passed | terminal |
| T1 | `dotnet build --nologo` | 0 | 0 | ok | terminal |
| T1 | `dotnet test --nologo` | 0 | 0 | 674 + 92 passed | terminal |
| T1 | `dotnet run --project tools/Stats.UiPreview -- --batch tools/Stats.UiPreview/captures/baseline.json` | 0 | 0 | 55/55 captured | `artifacts/ui-polish/before/*.json` |

## Visual evidence

| Case | Before PNG | After PNG | Scenario/substate | Theme | Logical size | Actual DPI | UI scale | Method | Inspection result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | | | |

## Settings binding parity

| Original binding/command | Original location | New category/location | Preserved behavior | Check result |
| --- | --- | --- | --- | --- |
| | | | | |

## Review findings

| Severity | File/symbol | Evidence/reproduction | Assigned repair | Fixed candidate | Recheck |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

## Gate outcomes

| Gate | Pass / fail / blocked | Evidence or exact blocker |
| --- | --- | --- |
| G0 Routing | pass (adapted) | Claude workflow per CLAUDE.md; Codex roles not applicable |
| G1 Baseline | pass | commit `ad5ec1d`, clean tracked tree, build/tests green; 55 before-screenshots captured at `b8b9c44` |
| G2 Preview isolation | pass | metadata scan + composition tests (92 green); sidecars list every simulated service |
| G3 Build | pass at T1 | |
| G4 Tests | pass at T1 | |
| G5 Visual | pending | |
| G6 Interaction | pending | |
| G7 Compatibility | pending | |
| G8 Independent review | pending | |

## Final handoff

- Concrete UI behavior changed: none yet (T0 is resource extraction + a settings seam; no visual change)
- Design adjustments and reasons:
- Simulation-only evidence:
- Real Windows/hardware checks performed:
- Remaining blockers/checks and next commands:
- Parent acceptance decision:
