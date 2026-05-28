# Pixelpipe smoke test checklist

Use this after changes to the tray menu, mount workflow, setup helpers, or diagnostics.

## Environment

- Run the non-interactive placement check first:

```powershell
.\dist\Pixelpipe.exe /smoketest-menu
```

It should exit with code `0`. This verifies tray submenu placement math and dark-theme application without launching the tray app.

- Run Pixelpipe normally, not as Administrator.
- Confirm the tray icon appears and the tooltip updates between mounted and unmounted states.
- Open `Diagnostics / repair...` and confirm it shows rclone, WinFsp, profile count, settings file, log directory, UI log, and per-profile log tails.

## Tray menu

- Open the main tray menu from the Windows notification area.
- Open each submenu: profile, `Add cloud remote`, `Bandwidth limit`, `Setup / dependencies`, and `Tools / diagnostics`.
- Confirm each submenu opens next to its parent row, stays on-screen, uses the dark theme, and does not appear at the top-left of the desktop.
- Repeat with the taskbar at bottom, top, left, and right when possible.
- Repeat on each monitor when using multiple monitors or mixed DPI scaling.

## Profile workflow

- Confirm each profile shows remote, drive, provider, status, storage, traffic, speed, and log tail when available.
- Use `Test profile` and confirm the preflight report checks rclone, WinFsp, remote configuration, drive letter, RC port, and storage probe.
- Mount the primary profile with `Mount - low overhead`.
- Open the mounted drive from the menu.
- Unmount the profile and confirm the menu state returns to unmounted.
- If Pixelpipe asks whether to stop a stuck rclone process, confirm the dialog defaults to `No`, explains that `Yes` only stops the Pixelpipe-started rclone process for that profile, and records a UI log entry.
- Repeat with `Mount - full cache` if the remote is suitable for cache mode.

## Bandwidth and refresh

- Change `Bandwidth limit` to `1 MB/s`, then back to `Unlimited`.
- Confirm mounted profiles accept the new limit without restarting.
- Use `Refresh usage now` and confirm storage, traffic, speed, or quota values refresh without freezing the menu.

## Setup helpers

- Open `Setup / dependencies`.
- Confirm disabled setup status text is readable and uses the dark theme.
- Confirm help actions open the expected installer, rclone config, or documentation prompt.
- Do not complete destructive or credential-changing setup flows unless intentionally testing them.

## Diagnostics and logs

- Use `Copy diagnostics` and paste into a scratch editor to confirm it contains the UI log tail.
- Use `Open log folder` and confirm `pixelpipe-ui.log` appears after any UI helper error is logged.
- Confirm profile-specific rclone logs are still written separately.

## Startup

- Toggle `Auto-mount at Windows startup` on and off.
- Confirm the check mark updates immediately.
- Confirm the registry startup entry uses the current executable path with `/automount` when enabled.
