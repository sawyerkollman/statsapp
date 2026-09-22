# Monitoring workflows implementation ledger

## Routing / baseline

- Base `c42264b`, local integration `feature/monitoring-workflows`.
- Preserved untracked `.codex/`, `AGENTS.md`, `HANDOFF.md`, `Stats-UI-Codex-Handoff/`.
- Hosted agent runtime exposes all four requested custom roles. TOML pins Luna/medium,
  Terra/medium, reviewer Sol/high, validator Sol/high. Parent routing permits Sol/Astra; project
  configuration requests Sol/high. Effective session metadata/client version is unobservable.
  No configuration changes.
- Useful read-only probes completed: Luna toolbar/menu inventory; Terra resource order; Sol
  preview isolation. Requested roles accepted; effective worker metadata unobservable.
- .NET SDK 9.0.316 targeting existing .NET 8 Windows projects. Sandbox SDK/git access denied;
  escalated commands with explicit repository/solution paths succeed (runner ignores cwd).
- Baseline `dotnet build .../Stats.sln --nologo`: zero warnings/errors.
- Baseline `dotnet test .../Stats.sln --nologo`: 886 Core + 247 preview pass, zero warnings.

## Packets

| Packet | Owner | Files / dependency | Status |
| --- | --- | --- | --- |
| A Layout profiles + lock/undo | Terra | Dashboard, profiles/settings, Fans profile picks; no App/project/preview | Implemented and reviewed |
| B Process monitor | Terra | New process classes/VMs/tests, Peaks view; no shared settings/App/preview | Implemented and reviewed |
| Checkpoint A | Parent + Sol | Integrate settings/startup, build/tests, review | Zero warnings, 906 Core + 247 preview pass |
| C Session/alert core | Terra + parent repair | Recording/history persistence, alert context and tests | Implemented and reviewed; parent completed recorder/file parser |
| D Compare/session UI | Terra + parent repair | Comparison/session VMs/windows/chart seam and tests | Implemented and reviewed |
| Checkpoint B | Parent + Sol | Integrate events/UI entrypoints, build/tests, review | Zero warnings, 929 Core + 251 preview pass; source review signed off |
| E Preview/docs | Terra + parent | Deterministic fixtures, screenshots, README | Complete; 23 candidate captures, zero binding warnings |
| Final | Reviewer + validator | Independent diff review, full tests and actual WPF evidence | Zero-warning build; 929 Core + 252 preview pass; source and simulated visual review clean; native checks explicitly pending |

Workers cannot delegate, commit, push, edit config or expand scope. Luna gets one repair attempt;
Terra gets two before Sol diagnosis. Shared files are serialized and builds run with edits frozen.
