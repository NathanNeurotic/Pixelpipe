# Pixelpipe smoke test checklist

Use this after changes to the tray menu, mount workflow, setup helpers, diagnostics, provider setup, schedules, watch folders, or docs that describe those areas.

You do not need to complete every destructive or credential-changing path on every PR. Do cover the sections that match your change.

## Build and automated checks

```powershell
.\scripts\build-release.ps1
.\scripts\run-tests.ps1
.\dist\Pixelpipe.exe /smoketest-menu
```

Expected:

- build creates `dist\Pixelpipe.exe`
- tests pass and exit 0
- `/smoketest-menu` exits 0 without launching the tray app

Optional when rclone is installed:

```powershell
.\scripts\watch-folder-smoke.ps1
```

Expected:

- `copyto` keeps the source and creates the destination
- `moveto` removes the source only after success
- missing source produces a non-zero failure
- timeout probe is killed cleanly

## Launch and baseline

- Run the optional rclone local-backend watch smoke when rclone is installed:

```powershell
.\scripts\watch-folder-smoke.ps1
```

It should copy one file, move one file, observe a non-zero failure for a missing source, and kill a deliberately long-running rclone `rcd` timeout probe.

- Run Pixelpipe normally, not as Administrator.
- Confirm the tray icon appears.
- Confirm the tooltip is short and reflects mounted/unmounted state.
- Open the main window from the tray.
- Open `Diagnostics` and confirm it shows rclone, WinFsp, profile count, settings file, log directory, UI log, and per-profile log tails.
- Use `Copy diagnostics` and confirm copied text includes the UI log tail and redaction note.

## Tray menu

- Open the main tray menu from the Windows notification area.
- Open each submenu: profile, `Add cloud remote`, `Bandwidth limit`, `Setup / dependencies`, and `Tools / diagnostics`.
- Confirm submenus open next to the parent row, stay on-screen, use the dark theme, and do not appear at the top-left of the desktop.
- Repeat with the taskbar at bottom, top, left, and right when practical.
- Repeat on each monitor when using multiple monitors or mixed DPI scaling.

## Profile workflow

- Confirm each profile shows remote, drive, provider, status, storage, traffic, speed, and log tail when available.
- Use `Test profile` and confirm the preflight report checks rclone, WinFsp, remote configuration, drive letter, RC port, and storage probe.
- Mount with `Mount - low overhead`.
- Open the mounted drive from the menu.
- Unmount and confirm the menu state returns to unmounted.
- If Pixelpipe asks whether to stop a stuck rclone process, confirm the dialog defaults to `No`, explains the scope, and logs the decision.
- Repeat with `Mount - full cache` when the remote is safe for cache testing.

## Main window

- Profiles tab: confirm profile cards resize without clipped text.
- Activity tab: confirm mount/unmount/test/watch events classify into useful categories.
- Diagnostics tab: confirm buttons are visible at the current DPI/font scale.
- Logs tab: confirm UI log and profile log tails load and filtering works.
- Settings tab: confirm dependency status, Pixeldrain key controls, update check preference, startup, verbose logging, transfer notifications, and maintenance actions are present.

## Profile editor

- Open `Edit` on an unmounted profile.
- General tab: edit label, provider, remote, drive letter, mount mode, and auto-mount; save and confirm the profile card/tray text update.
- Bandwidth tab: set a per-profile bandwidth limit, then clear it back to inherited.
- Bandwidth schedule: enter `00:00=off,09:00=1M,18:00=off`; save and confirm the normalized value survives reopen.
- Schedule tab: set mount/unmount times and selected days; save and reopen.
- Watch tab: enable a real local folder in copy mode with a short quiet period; save and confirm the tray/profile watch label appears.
- Try saving an enabled watch folder with a missing path and confirm Pixelpipe warns instead of crashing.

## Provider setup

- Open `Add cloud remote`.
- Confirm provider list includes Pixeldrain, Google Drive, OneDrive, Dropbox, Box, MEGA, S3, WebDAV, SFTP, FTP / FTPS, custom existing remote, and rclone config terminal.
- For OAuth providers, confirm Pixelpipe opens rclone config and asks the user to return after configuration.
- For non-OAuth providers, confirm required fields prevent empty submissions.
- Do not submit real credentials unless intentionally testing that provider.

## Bandwidth and refresh

- Change global `Bandwidth limit` to `1 MB/s`, then back to `Unlimited`.
- Confirm mounted Pixelpipe-launched profiles accept the new limit without restarting.
- Confirm a per-profile bandwidth override takes precedence over the global setting.
- Use `Refresh usage now` and confirm storage, traffic, speed, or quota values refresh without freezing the menu.

## Watch-folder workflow

- Use copy mode first.
- Drop a non-empty file into the watched folder.
- Confirm it waits for the quiet period, uploads, increments uploaded count, and keeps the local file.
- Switch to move mode only with disposable test files.
- Confirm move mode removes the local file after successful upload.
- Try a bad remote target or missing local file path and confirm the failure is counted and logged.

## Schedule workflow

- Set a mount time one or two minutes in the future and confirm it fires while Pixelpipe is running.
- Set an unmount time one or two minutes later and confirm it fires once.
- Confirm schedule days exclude/include today correctly.
- Test a bandwidth schedule entry one or two minutes in the future and confirm the profile bandwidth changes.

## Setup helpers

- Open `Setup / dependencies`.
- Confirm disabled setup status text is readable and themed.
- Confirm help actions open the expected installer, rclone config terminal, or download prompt.
- Do not complete destructive or credential-changing setup flows unless intentionally testing them.

## Diagnostics and recovery

- Use `Open log folder` and confirm `pixelpipe-ui.log` appears after any UI helper error is logged.
- Confirm profile-specific rclone logs are still written separately.
- Use `Open settings backups folder` and confirm the folder opens or is created.
- Test stale-drive/orphan-rclone repair only with disposable mounts.

## Startup

- Toggle `Auto-mount at Windows startup` on and off.
- Confirm the check mark updates immediately.
- Confirm the registry startup entry uses the current executable path with `/automount` when enabled.

## Documentation spot-check

When user-facing behavior changed:

- README still gives the shortest correct first path.
- `docs/USER_GUIDE.md` explains the workflow without requiring JSON edits.
- `docs/CONFIGURATION.md` lists any new settings keys.
- `docs/FAQ.md` and `docs/TROUBLESHOOTING.md` answer the obvious new failure modes.
