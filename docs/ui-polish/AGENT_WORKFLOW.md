# Agent workflow and model routing

## 1. Runtime preflight

This is an agent configuration and orchestration harness, not a standalone paid API loop. It relies on Codex's agent tools. Model names and configuration were checked against current official docs on 2026-09-06; local client support and account access must be verified at run time. See SOURCES.md.

For local Codex, merge `.codex/config.toml` and `.codex/agents/*.toml` into the trusted project and reload the session as required. Preserve permissions, sandbox, providers, and unrelated configuration. Do not change trust or approval settings programmatically to make the harness run.

Before implementation:

1. Record client/version, parent model, project config discovery, and available agent/model capabilities.
2. Resolve all four custom role definitions. Confirm they pin both model and reasoning effort.
3. Execute small useful read-only routing probes, one per model tier: Luna inventories the existing toolbar labels; Terra maps tile resource dependencies; Sol reviewer identifies one preview-isolation risk from the proposed architecture. Each probe stays bounded and returns a short result. It may be folded into the relevant T0 work rather than adding dummy tasks.
4. Record requested model and the runtime-reported model/session metadata if exposed. Do not accept the worker saying "I am Terra" as routing evidence. When effective metadata is hidden, write `unobservable`, retain the explicit request/config, and state routing is configured but not independently verified.
5. If a role/model is rejected or custom config is not loaded, use the explicit-spawn route below only if supported. If neither works, stop launching implementation children and report the capability gap. Do not automatically run the whole batch on Sol/Astra or silently switch provider/API billing.

Local current configuration declares Terra as the default child, plus named exceptions for Luna/Sol. Named agent files can override explicit spawn settings; select the intended role and do not assume an explicit model overrides a conflicting role file. The parent can use Sol or user-selected Astra. An unavailable Astra does not prevent the default Sol workflow if Sol is available.

## 2. Hosted Work adapter

Project TOML is not assumed to affect hosted Work. Inspect the actual spawn schema and available models for that session. Use the explicit runtime model and reasoning fields and provide the relevant role instructions in each packet. In a runtime exposing `collaboration.spawn_agent`, the intended request shapes are:

```json
{"task_name":"t3_tiles","fork_turns":"none","model":"gpt-5.6-terra","reasoning_effort":"medium","message":"<complete T3 packet with owned paths, contract, and acceptance checks>"}
```

```json
{"task_name":"labels","fork_turns":"none","model":"gpt-5.6-luna","reasoning_effort":"medium","message":"<bounded mechanical packet with examples and owned paths>"}
```

```json
{"task_name":"review_candidate","fork_turns":"none","model":"gpt-5.6-sol","reasoning_effort":"high","message":"<frozen candidate, diff scope, validation evidence, read-only review task>"}
```

These are runtime-specific examples, not universal API payloads. Use the advertised schema when it differs. The no-history fork permits explicit model overrides in the Work runtime used to prepare this package and avoids duplicating the full conversation. Include complete task context and accessible file locations because the child has no inherited conversation. Do not use a full-history fork that ignores or rejects model overrides.

## 3. Roles and ownership

Parent owns the task ledger, dependency decisions, shared contracts, integration, and final acceptance. Terra implements bounded WPF work. Luna handles exact transformations after design decisions are frozen. Sol independently reviews and validates. Higher-tier agents should not redo routine worker implementation merely for reassurance.

At most three child threads may be open, with at most two simultaneous source editors. Shared files are serialized. Workers do not create grandchildren. Running two workers against different sections of DashboardWindow.xaml is still a file ownership conflict: serialize T4 and T5. Parent alone handles `.sln`, `.csproj`, `App.xaml.cs`, integration, config, and any unassigned shared resource unless it explicitly transfers ownership.

Prefer a shared integration branch with disjoint ownership for this small repo. Use isolated worktrees only when the client supports them and useful independence justifies integration cost. Never assume a worktree isolates hardware, user settings, or shared build artifacts. Do not run build/test while source edits are in flight.

## 4. Work packet

Supply each child:

- Task ID and one concrete outcome.
- Baseline/current integration commit and relevant dirty changes.
- Owned files, read-only context files, and prohibited areas.
- Exact DESIGN sections and task acceptance criteria.
- Dependencies and frozen resource names/layout conventions.
- Current fixture/screenshots when available; label simulated data.
- Exact focused checks expected and environment limitations.
- Output: changed files, behavior summary, commands/results, issues, review pointers.
- No delegation/commit/push/config edits and repair limit.

Use focused source snippets and paths rather than the full history, repository dump, or long tool logs. Context reduction is not permission to omit the technical invariants. Workers should ask the parent about scope, not bounce routine questions to the user.

## 5. Routing and escalation policy

Default implementation is Terra. Luna is suitable for label/resource substitutions with explicit examples, binding inventories, and evidence organization; it is not the default owner of WPF layout behavior. A Luna task with one failed repair goes to Terra. A Terra task with two failed repair attempts goes to Sol for a concise diagnosis; return the diagnosed repair to Terra where sensible. Do not start endless replacement workers.

Sol/Astra parent reviews architectural choices and resolves ambiguous tradeoffs. Sol review checks each integrated checkpoint and the final diff; Sol validation checks real build/runtime evidence. Astra is optional for unresolved architecture or difficult final visual judgment, not required for every edit. Do not quote fixed monetary savings: this workflow controls routing and concurrency, not total spend.

If Luna is unavailable but Terra is available, Terra can perform its bounded tasks with the substitution recorded. If Terra is unavailable, do not silently substitute a higher-cost worker pool; report it and retain the ready task packets. If Sol is unavailable, do not silently downgrade independent acceptance to a worker; use an explicitly available/user-selected higher-tier alternative or mark the gate pending.

## 6. Checkpoint loop

Assign ready task → implement → freeze/integrate → parent/Sol build and focused checks → Sol review → bounded worker fixes → recheck affected evidence → parent acceptance. No acceptance while a reviewer is still working against an obsolete candidate. Final build/test runs are required by repo policy; avoid full-suite runs after every cosmetic line change.

Record model usage per task: role, requested model/effort, effective metadata or unobservable, child/session ID if exposed, retries, result, changed files, evidence. Actual cost is unknown unless the client supplies billing/usage data; do not infer dollars from elapsed time. Mark completed children closed when the runtime supports it; otherwise reuse available slots or serialize.

## 7. Completion status vocabulary

- `prepared`: plan/config ready, application work not implemented.
- `implemented`: change exists; evidence may still be pending.
- `verified`: named build/behavior checks actually ran and passed.
- `visually accepted`: Sol/Astra inspected actual WPF captures and the visual gates passed.
- `blocked`: exact capability or failed gate identified with a next action.

Use the narrowest true status. Copying TOML is not proof a lower-cost agent ran; a clean build is not visual acceptance.
