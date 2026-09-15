# v1.10 master sync

Merge master `d7fc1ad` (merged PRs #14/#15) into feature/v1.10 `26793cd`.
The five shared-file conflicts were ordering differences: resolved source, tests, and preview tools now match
master exactly. All fourteen v1.10-only planning/spec documents are retained. PRs #16–#19 are not merged here.

Validation: build completed with zero warnings/errors; 800 Core + 205 preview tests passed, none skipped.
Terra handled the bounded conflicts; parent normalized final block ordering to master and ran validation;
independent Sol review checks both source identity and preservation of the branch-only documents.
Role routing reuses the session preflight; effective model metadata is unobservable.

Post-commit simulated WPF capture command, from this branch:

`dotnet tools/Stats.UiPreview/bin/Debug/net8.0-windows/Stats.UiPreview.dll --batch C:/claude-projects/Stats/artifacts/v110-sync-evidence/captures.json`

The three local PNGs/sidecars cover Free layout at 0.9/1.3 application scale and detail warmup. Actual DPI is 96;
dashboard capture canvases include 39 transparent bottom rows. Native drag/focus/hotkeys, live theme switching,
150% Windows DPI, PresentMon, and physical hardware/fan checks remain pending. Existing master captures are
also applicable because source/tests/tools are identical; no production hardware is initialized by this preview.
