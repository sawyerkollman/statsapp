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
- Overall status: **implemented (T0)** — see task ledger

## Routing ledger

| Task/probe | Role | Requested model/effort | Runtime-observed model or unobservable | Session ID | Attempts | Result |
| --- | --- | --- | --- | --- | --- | --- |
| T0 | parent | Claude Fable 5.1 | this session | — | 1 | done |

Model self-reports are not runtime proof. The Codex Sol/Terra/Luna routing in `AGENT_WORKFLOW.md` is replaced for this run by the repo's Claude workflow (Sonnet implementers, Opus review, Fable controller); the technical invariants are unchanged.

## Task ledger

| Task | Owner | Files | Dependencies met | Implementation | Review | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| T0 | parent | `src/Stats.App/App.xaml`, new `Views/AppStyles.xaml`, `Views/DashboardWindow.xaml.cs`, `App.xaml.cs`, `docs/ui-polish/EVIDENCE.md` | — | done | parent | build/tests green |
| T1 | Sonnet implementer | `tools/Stats.UiPreview/`, `tests/Stats.UiPreview.Tests/`, `Stats.sln` (parent) | T0 | pending | | |
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

## Commands and results

| Candidate | Command | Exit | Warnings | Result | Log |
| --- | --- | --- | --- | --- | --- |
| `ad5ec1d` baseline | `dotnet build --nologo` | 0 | 0 | ok | terminal |
| `ad5ec1d` baseline | `dotnet test --nologo` | 0 | 0 | 674 passed | terminal |
| T0 seams | `dotnet build --nologo` | 0 | 0 | ok | terminal |
| T0 seams | `dotnet test --nologo` | 0 | 0 | 674 passed | terminal |

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
| G1 Baseline | pass | commit `ad5ec1d`, clean tracked tree, build/tests green (above); before-screenshots pending T1 |
| G2 Preview isolation | pending T1 | |
| G3 Build | pass at T0 | |
| G4 Tests | pass at T0 | |
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
