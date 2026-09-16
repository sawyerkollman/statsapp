# Opt-in beta updates

Extend the existing updater on `beta`; do not merge master or publish a release in this task.
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

Implemented on `beta` after `b0d93d9`. Used the existing updater and native WPF controls; no dependencies.
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
