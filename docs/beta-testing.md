# Testing Stats betas

Install the beta installer from the project's GitHub Releases page, then enable **Receive beta
updates** in Settings. Beta releases are opt-in and are not promoted to stable automatically.

## Useful feedback

Include the version, steps to reproduce, expected/actual behavior, whether the problem occurs with
the game in the foreground, polling interval, and whether restarting Stats changes it. Screenshots
are useful, but review them for private information first. Never include passwords or access tokens.

**View > Beta lab > Diagnostics** previews the exact allowlisted diagnostic JSON before export.
It does not upload anything. Review any feedback text before copying it into a GitHub issue:
notes, recordings, logs and screenshots can contain personal data and are not attached automatically.

Release notes: https://github.com/sawyerkollman/statsapp/releases

Feedback: https://github.com/sawyerkollman/statsapp/issues

## Beta.5 installer workaround

If the beta.5 installer reports that it cannot automatically close applications, cancel setup,
choose **Exit** from the Stats tray menu, then run the downloaded beta.5 installer again. The window's
close button only hides Stats. Beta.5 has a malformed shutdown-command path; the packaging repair
corrects it and checks system executable paths before building future installers.

## Return to stable

Turning **Receive beta updates** off stops offers of future betas; it does **not** downgrade the app.
The normal updater waits for a stable release whose version is at least the installed beta's core
version. This is the safest route if you do not need to leave the beta immediately.

For an immediate manual downgrade:

1. Exit Stats through its tray menu so recordings drain and fan control returns to Auto.
2. Back up the entire `%APPDATA%\Stats` folder to a separate location while Stats is closed.
3. Download the desired stable installer from Releases and run it manually.
4. Older versions may not understand newer settings or features. If settings fail to load, exit Stats,
   preserve the backup, and move the active `settings.json` aside before starting stable with defaults.
   Do not overwrite your backup with the older app's settings. Check all monitoring/fan choices before
   explicitly re-enabling any hardware control.

There is no automatic restore/downgrade tool. Recordings and beta-local data should remain backed up
even if the stable version does not expose their features. Reinstall a compatible beta before using
those files again.
