# Troubleshooting

Start here:

```text
Tools / diagnostics -> Copy diagnostics
```

Diagnostics show dependency status, profiles, drive letters, RC ports, mount state, watch-folder state, log paths, and recent log tails. Secret-looking values are scrubbed before copying.

## First checks

Before chasing a deeper issue:

1. Run Pixelpipe normally, not as Administrator.
2. Open `Diagnostics` and confirm rclone is found.
3. Confirm WinFsp is installed.
4. Use `Test profile` on the affected profile.
5. Check whether the selected drive letter is already used.
6. Open the profile's rclone log from the Logs tab.

## SmartScreen blocks first launch

Expected for unsigned builds.

```text
Click "More info" -> Click "Run anyway"
```

To verify the download first:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

Compare the result with the release's `SHA256SUMS.txt`.

## rclone is missing

Use one of these:

```text
Setup / dependencies -> Download portable rclone now
Setup / dependencies -> Install/update rclone with winget
```

Portable rclone installs to:

```text
%USERPROFILE%\Apps\rclone\rclone.exe
```

Pixelpipe verifies the portable rclone download against rclone's published SHA-256 sums before extracting it.

## WinFsp is missing

Use:

```text
Setup / dependencies -> Install WinFsp with winget
```

If mounting still fails after installation, restart Windows.

## winget is missing

Install Microsoft's App Installer package, then reopen Pixelpipe.

You can also install rclone and WinFsp manually, then use `Setup / dependencies` to re-check.

## A drive letter does not appear in File Explorer

Common causes:

- Pixelpipe is running as Administrator.
- WinFsp is missing or needs a restart.
- The drive letter is already in use.
- rclone exited immediately after launch.
- The remote is not configured in rclone.
- The mount exists in a different Windows privilege context.

Try:

1. Exit Pixelpipe.
2. Start Pixelpipe normally.
3. Run `Test profile`.
4. Change the profile drive letter to a clearly free letter such as `X:` or `Z:`.
5. Keep `Mount as network drive` enabled.
6. Mount again.

## rclone is stuck or the drive will not release

Use:

```text
Tools / diagnostics -> Clear stale drive
Tools / diagnostics -> Find / kill orphan rclone processes
```

Pixelpipe scopes cleanup to Pixelpipe-launched or drive-related rclone processes where it can. If Windows refuses to release a wedged WinFsp mount, restart Windows.

## A remote does not appear after setup

For OAuth providers, rclone owns the sign-in flow. Make sure you completed rclone's config wizard and that the remote name matches what Pixelpipe expects.

Check manually:

```text
Setup / dependencies -> Open rclone config in terminal
```

Then run or inspect:

```text
rclone listremotes
```

Use `Import existing rclone remotes` after the remote appears.

## Quota or storage says unavailable

This can be normal.

- Pixeldrain transfer quota requires a saved Pixeldrain API key.
- Storage display requires the backend to answer `rclone about`.
- S3-compatible buckets, FTP servers, and some WebDAV/SFTP servers may not report quota.
- A provider limitation is different from a mount failure.

Use `Test profile`; if the remote and storage probe are OK or only warned, the profile may still mount fine.

## Bandwidth limit does not change live

Live bandwidth changes require a Pixelpipe-launched mount, because Pixelpipe enables and talks to rclone RC.

Fix:

1. Unmount the profile.
2. Stop any manually started rclone mount for the same drive.
3. Mount from Pixelpipe.
4. Change the bandwidth limit again.

If a per-profile limit or bandwidth schedule is set, it can override the global tray setting for that profile.

## A schedule did not run

Check:

- The profile is saved and unmounted/remounted if you recently edited it.
- `Enable schedule` is checked.
- At least one of `Mount at` or `Unmount at` is set.
- The selected days include today.
- Times are 24-hour local times such as `09:00` or `18:30`.
- Pixelpipe is running at the scheduled time.

The schedule timer checks every 30 seconds. It should run during the matching minute, not necessarily at exactly second zero.

## Watch-folder uploads do not start

Check:

- Watch folder is enabled in the profile editor.
- The local watch path exists.
- The file is not zero bytes.
- The file has stopped changing for the quiet period.
- You are watching the folder itself; subfolders are not watched.
- rclone can write to the remote target path.

Use the profile menu and Diagnostics tab to see the watch state: queued, uploading, uploaded, failed, and last result.

## Watch-folder move mode removed a local file

In move mode, Pixelpipe uses `rclone moveto`. The local file is removed only after rclone exits successfully.

When testing a new workflow, start with copy mode until you trust the remote, target folder, and credentials.

## Settings seem corrupted or profiles disappeared

Pixelpipe keeps:

```text
%APPDATA%\Pixelpipe\settings.json.bak
%APPDATA%\Pixelpipe\backups\
```

Use:

```text
Tools / diagnostics -> Open settings backups folder
```

Exit Pixelpipe before restoring a backup.

## Pixelpipe says another instance is already running

Pixelpipe uses a per-session mutex so only one tray instance runs at a time.

If the old process is invisible or hung:

1. Wait a few seconds and try again.
2. Open Task Manager and end the stale Pixelpipe process if needed.
3. Start Pixelpipe normally.

## Update check does not install anything

That is expected. Pixelpipe checks for updates and opens the releases page; it does not silently install new builds.
