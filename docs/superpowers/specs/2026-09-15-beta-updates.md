# Opt-in beta updates

Extend the existing updater with a default-off beta channel. Initially implemented on `beta`, then
backported independently onto `master` without merging the larger beta features or publishing a release.
`ReceiveBetaUpdates` defaults false and persists independently of automatic checking. Both manual
and background checks respect it. Stable uses `/releases/latest`; beta uses `/releases?per_page=100`
and picks the highest eligible numeric version, not publication order. Drafts are never eligible.
Supported prerelease naming: `vX.Y.Z-beta.N` (legacy `-beta` sorts before beta.1). Beta opt-in also
receives stable releases. Installed informational version retains the prerelease suffix and ignores
the build metadata hash; numeric assembly version alone cannot order beta-to-beta or beta-to-stable.
Turning opt-in off waits for an equal-core stable or newer stable version; never downgrade.
Changing channels invalidates pending checks/offers; active installs remain user-initiated and cancel
when the channel changes. Existing download integrity and fan-safe shutdown remain unchanged.

Tag workflow marks suffix tags prerelease and not latest; plain tags remain stable. No new dependency.
The stable release containing this checkbox is needed before existing stable users can opt in from
inside the app; alternatively testers install the first beta manually. Before that first beta, merge
the newer master changes into beta and repeat validation. Promotion is beta -> master -> stable tag.

Validation: version ordering/flags/assets; HTTP endpoint/channel; settings compatibility; race guards;
zero-warning build/full tests and real isolated WPF Settings capture. No hardware/installer execution.
GitHub endpoint reference: https://docs.github.com/en/rest/releases/releases#list-releases

## Validation and handoff

Original validation on `beta` after `b0d93d9` (before the stable backport below):
used the existing updater and native WPF controls; no dependencies.
Terra worker owned the setting/VM/checkbox/tests; parent owned parser/HTTP/composition/workflow. Sol reviewed
the whole diff and signed off after repairing a canceled-install continuation that could overwrite a newer
offer. Existing configured routing was reused; effective model metadata remains unobservable.

- Final build: zero warnings/errors.
- Full suite: 943 Core + 252 preview tests passed, zero failures/skips.
- Regression coverage: stable defaults, settings round-trip/event, beta.2 vs beta.10, beta -> equal-core
  stable, opt-out/no downgrade, draft/prerelease flags, malformed assets, endpoint selection, preserved
  checksum and full beta display, invalidated old offer and active-download offer protection.
- Actual isolated WPF Settings captures: [dark](../../../artifacts/beta-updates/settings-dark.png) and
  [light/narrow](../../../artifacts/beta-updates/settings-light-narrow.png), visually inspected at 96 DPI,
  zero binding warnings. Images stay in local ignored artifacts, with simulated settings/network/hardware.
- `git diff --check` passed. Workflow metadata source-reviewed; no tag/release/installer executed.
  No master merge, user settings migration, hardware access or real install performed. Existing stable
  users cannot see the new checkbox until they install a build containing it.

## Stable-only backport validation

Backported only the updater commit `39aa3dd` onto master `fd022fb` in an isolated worktree.
The larger beta features were not merged; master's notification settings were preserved.
Luna resolved the enum-only conflict; independent Sol review found no material issues.

- Build: zero warnings/errors. Full tests: 928 Core + 251 preview passed, no failures/skips.
- Actual isolated WPF captures, visually inspected: `artifacts/beta-updates/master-settings-dark.png`
  (1180x900) and `master-settings-light-narrow.png` (860x700), both zero binding warnings.
  Artifacts are local to the original repository, not tracked; data/services are simulated.
- Capture sidecars report a clean working diff because the candidate was staged. The reviewed/captured
  staged tree before this documentation addition was `8ef148aaf56b91a7e395ca30be484a9ffefc38cd`.
- Staged whitespace check passed. No hardware, real installer, tag or release executed.
  Manual keyboard interaction and live theme switching on this exact backport remain untested.
  Existing users need a subsequent stable installer release to receive the opt-in checkbox.
