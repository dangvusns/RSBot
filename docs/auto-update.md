# Windows updates

RSBot checks the latest stable public release of `dangvusns/RSBot` at startup.
When a newer version is available, choose **Update** to download it and restart
the bot, or **Skip** to continue using the installed version. Updating ends the
current bot session. Offline checks are logged without blocking normal use.

## Publish a version

1. Commit and push the changes you want to release.
2. Open GitHub **Actions → Manual Release → Run workflow**.
3. Select the branch containing those changes and choose `patch`, `minor`, or
   `major`. The workflow file must also exist on the repository's default branch
   for GitHub to offer the Run workflow button.
4. Wait for the workflow to finish. It builds with `build.ps1` and MSBuild,
   runs updater checks, then publishes `RSBot.vMAJOR.MINOR.PATCH.zip` and its
   SHA-256 checksum as a public stable release.
5. Start or restart the Windows bot and accept its update prompt.

The workflow calculates the next version from the greater of the application
project version and existing semantic version tags. It stamps that version into
the build through MSBuild arguments; project files do not need manual edits.
The updater selects the matching binary archive, rather than GitHub's source
archives. Nightly prereleases are excluded from the stable update feed.

## First installation of the new updater

The older updater points to another repository. Install this change once by
pulling and rebuilding on Windows, or by extracting the first new release into
your existing installation with RSBot closed. Keep your existing `User` folder.
Further releases can be installed from the update prompt.

The release uses the existing framework-dependent x86 build. Windows still needs
.NET 8 Desktop Runtime (x86), as it does for the current bot. Run from a writable
installation folder and close other RSBot instances using the same folder before
installing an update.

## Installation and recovery

Downloads are verified against GitHub's SHA-256 asset digest when supplied.
The installer runs from a temporary copy outside the installation and waits up
to 60 seconds for the calling RSBot process to exit. It validates the archive
layout and rejects archive traversal and linked installation paths.

`User`, `Logs`, previous update staging, and debug symbols are excluded from the
release package. The installer also refuses to copy packaged `User` and `Logs`
files. Existing files absent from the package, including custom plugins, remain.
Packaged application and `Data` files replace their installed counterparts.

Replaced files are backed up in `update_temp/backup-<id>`. A copy failure triggers
rollback of attempted replacements, and the updater opens `updater_error.log`
with the failure and backup path. A failed update does not automatically restart
the application. If rollback itself encounters locked files, close the locking
process and restore the listed backup before restarting. Successful updates keep
the backup for manual recovery; older backups can be removed when no longer needed.

## Validate on Windows

From the repository root:

```powershell
powershell.exe -ExecutionPolicy Bypass .\build.ps1 -Configuration Release -DoNotStart
powershell.exe -ExecutionPolicy Bypass .\scripts\test-updater.ps1
```

The checks use disposable installations and test replacement of application and
updater files, profile/log preservation, retained custom plugins, invalid
packages, archive traversal, and rollback after a copy failure. They do not
launch the real game or bot. GitHub runs them before publishing each release.

Finally, verify the complete flow on Windows with an older installed build and a
newer public release: accept the prompt, observe the restart, confirm the new
version, and check that your profile remains available. This covers UI lifetime,
network download, checksum verification, and restart beyond the installer tests.

References: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases),
[manual workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow),
[.NET runtime installation for CI](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
