# Gaming workflow depth implementation ledger

Baseline `8a13100`; branch `feature/gaming-workflow-depth`. Scope/constraints are in
the matching spec. No desktop-focus-taking validation. The subsequent user request
authorizes stable promotion of beta 6 as `v1.11.0` and this work as `v1.12.0-beta.1`
on `beta`, including tags and release publication. Native validation remains pending.

| Checkpoint | Owner | Status / evidence |
| --- | --- | --- |
| Routing | Parent; Luna inventory, Terra frame map, Sol lifecycle probe | Available custom roles explicitly selected; effective model metadata unobservable |
| D1 raw recording core | Terra `depth_frames` | Implemented; one repair for C# local shadowing; 38 focused Core checks pass including frames, recordings, updater outcomes |
| D1 lifecycle / updater | Parent | Implemented; zero-warning Release solution build; 33 focused WPF-headless/isolation checks pass |
| D1 installer | Parent | Static system-path validation passes; Inno 6.7.3 compiles script successfully with old staged payload; compiler check only, not installable candidate verification |
| D1 independent review | Sol `depth_review` | Final source gate clear: Global ACL ownership, authenticated HWND process, delivery failures and preserved shutdown ordering; native checks pending |
| D2 overlay | Terra `depth_frames` | Implemented; two bounded repairs for selection, guides, fit, import and history; independent source recheck found no remaining material blocker |
| D2 gaming/restoration | Terra `depth_gaming` | Implemented; independent domain restoration, manual-edit preservation and headless tests |
| D3 library / raw UI | Terra, then parent integration | Implemented; parent repaired malformed-entry isolation, baseline confinement/background I/O, duration and failure-finalization paths |
| Final source / automated gates | Sol reviewer, Sol validator, parent | Final Release build: zero warnings/errors. Full TRX: 1,053 Core + 375 UiPreview = 1,428 passed, zero failed/skipped. Independent source review clear |
| Visual/native gates | User-controlled focus availability | Pending; not replaced by compilation or fake FPS |

## Integration details to retain

- Installer uses native window discovery, process identity, shutdown post and process-handle
  wait. Inno's documented [PostMessage](https://jrsoftware.org/ishelp/topic_isxfunc_postmessage.htm)
  is asynchronous; waiting on the exact process proves termination rather than merely dispatch.
  [Abort](https://jrsoftware.org/ishelp/topic_isxfunc_abort.htm) at `usUninstall` stops uninstall
  before deletion when graceful shutdown cannot finish.
- Raw capture subscriber must attach after recorder Start, detach before Stop/drain and on
  visible recorder failure. No capture enablement; raw checkbox is explicit session opt-in.
- Frame file count/percentiles are whole recording; poll charts/replay remain bounded.
  CSV currently exports polls; label that explicitly when raw recording is present.
- Increase raw queue from the poll-only 128 to a bounded burst allowance; existing PresentMon
  delivery batches can exceed 128. Batch raw flushes and retain visible overflow failure.
- Profile restoration uses independent domains and sticky observed manual-change flags.
  Never let dashboard restoration overwrite manually edited overlay selection.
- Ownership is machine-wide (`Global\Stats.Native.v1`); the hidden control window remains
  session-local. Existing unexpected owner/ACL fails closed before hardware initialization.
  Setup without a local endpoint blocks while the global guard exists. Cross-session
  manual Exit is required; native multi-session acceptance remains pending.

## Review repairs and evidence boundaries

- Source review covered shutdown ordering, secure update staging, recorder subscription
  lifetime, stale callbacks, bounded queues, incomplete-file duration, untrusted library
  metadata/paths, restoration domains and editor selection/undo/import behavior.
- Final independent review found no remaining material source issue. Secondary launches
  authenticate the endpoint's executable before granting foreground permission or sending
  outcomes, and failed delivery reaches the manual-exit fallback.
- Raw recording tests use supplied frames and stalled writers, not real ETW. The
  security-descriptor check does not create the production mutex or construct App.
- No source editors were active during build/test checkpoints. Source ownership was
  disjoint; shared composition/installer files were parent-only. No worker delegated,
  committed, pushed, or edited agent configuration.
- Requested routing: `depth_inventory` Luna/medium; `depth_frames`, `depth_gaming`
  Terra/medium; `depth_review`, `depth_validation` Sol/high. Effective runtime model,
  session IDs and actual cost remain unobservable; configured routing is not verification.

Unrelated owner files remain untouched. Final artifacts should include a source manifest,
build/test results and independent review, with screenshots explicitly pending.

Final validator handoff: `artifacts/gaming-workflow-depth/final-validation-report.md`.
Final automated evidence: `artifacts/gaming-workflow-depth/final-release-build.log`,
`final-release-tests.log`, `final-test-results/*.trx`, `source-review.md`, and `source-manifest.txt`.
The TRX files, not just MSBuild's test-target summary, establish executed test counts.

Final installer checks: `installer-validate.log` passed; `iscc-compile.log` passed with
Inno 6.7.3 and the old staged payload. Its validation-only executable was not run and is
not a release candidate. A unique test-only Local mutex diagnostic could not assign the
Administrators owner from the available terminal token (`mutex-sddl-roundtrip.log`);
the token was confirmed medium-integrity/non-admin. Native elevated ACL round-trip remains pending. Production elevation requirements and
ACLs were not weakened to accommodate the terminal. No UAC/focus-taking run was attempted.
