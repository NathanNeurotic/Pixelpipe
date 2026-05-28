<img width="1774" height="887" src="https://github.com/user-attachments/assets/eccffc6c-2cce-404e-82fd-e6b72a483e33" />

# Pixelpipe

Pixelpipe is a small Windows tray app for mounting Pixeldrain and other rclone-compatible cloud remotes as Windows drives.

It started as a focused Pixeldrain filesystem tray tool and now acts as a polished rclone mount manager: mount, unmount, inspect usage, control bandwidth, auto-mount on startup, and recover when Windows/rclone gets weird.

## Current scope

Pixelpipe is **Pixeldrain-first**, but no longer Pixeldrain-only.

### Tier 1: Pixeldrain

Pixeldrain remains the fully integrated provider:

- First-run setup helper.
- Pixeldrain rclone remote configuration helper.
- Optional Pixeldrain API key prompt.
- DPAPI-encrypted PixelDrain API key storage.
- Storage usage display through rclone.
- Monthly / last-30-days transfer quota display through the Pixeldrain API.
- Mount / unmount / open drive / startup mount.
- rclone RC bandwidth controls.

### Tier 2: common rclone remotes

Pixelpipe can now manage and mount additional rclone remotes, including:

- Google Drive
- MEGA
- OneDrive
- Dropbox
- Box
- S3-compatible storage, including R2, B2, Wasabi, MinIO, and similar backends through rclone
- WebDAV / Nextcloud
- SFTP
- FTP

Provider-specific quota is not guaranteed for every backend. Generic storage usage is shown when `rclone about <remote> --json` supports it.

### Tier 3: custom rclone remotes

Any existing rclone remote can be imported into Pixelpipe and assigned a drive letter.

## Two ways to use it

Pixelpipe ships both interfaces side by side; pick whichever you prefer.

**Tray menu** (right-click the tray icon). Everything is here: per-profile mount controls, bandwidth, setup, diagnostics, tools. Status text updates live every few seconds while the menu is open.

**Main window** (`Open Pixelpipe window...` in the tray, or the new Setup wizard exits to it). Four tabs:

- *Profiles*: dashboard cards with live status pills, storage gauges, session/speed numbers, and Mount/Unmount buttons.
- *Diagnostics*: the same data you get from "Copy diagnostics", plus repair buttons.
- *Logs*: per-profile rclone log and the Pixelpipe UI log in a tail viewer.
- *Settings*: bandwidth, startup, verbose logging, re-run setup wizard, check for updates.

**Quick controls popup** (`Quick controls...` in the tray). Small always-on-top window with aggregate live speed, session traffic, bandwidth dropdown, and per-profile status. Designed to sit in a corner during active transfers.

**Setup wizard** runs on first launch and walks through rclone / WinFsp / your first rclone remote / optional API key. Re-runnable any time from the Settings tab or the Setup tray submenu.

## Features

- Multi-profile tray menu for multiple cloud remotes.
- Mount and unmount each remote independently.
- Primary profile double-click toggle.
- Add guided profiles for Pixeldrain, Google Drive, MEGA, OneDrive, Dropbox, Box, S3, WebDAV, SFTP, and custom rclone remotes.
- Import existing rclone remotes with `rclone listremotes`.
- Per-profile preflight checks for rclone, WinFsp, remote configuration, drive-letter availability, RC port availability, and backend storage response.
- Per-profile drive letters.
- Per-profile network/fixed drive mode.
- Per-profile startup auto-mount.
- Low-overhead and full-cache mount modes.
- Global bandwidth limit profiles, applied live through rclone RC.
- Live session traffic and current speed for Pixelpipe-launched mounts.
- Generic storage usage where the backend reports it.
- PixelDrain transfer quota display when an API key is configured.
- First-run setup for rclone, WinFsp, Pixeldrain remote, and optional API key.
- Portable rclone download fallback.
- winget install helpers for rclone and WinFsp.
- Themed dark tray menu.
- Diagnostics / repair window.
- Settings JSON under `%APPDATA%\Pixelpipe\settings.json`.
- Logs under `%LOCALAPPDATA%\Pixelpipe\logs\`.
- Rolling GitHub Actions build on every push.

## Requirements

Pixelpipe is for Windows 10/11.

Under the hood it uses:

- rclone
- WinFsp
- One or more configured rclone remotes

Pixeldrain filesystem access still requires Pixeldrain's filesystem feature. Pixelpipe does not turn normal public Pixeldrain file links into a writable filesystem.

## Quick start

Pixelpipe ships in two equivalent formats; pick the one that fits how you want to use it.

| Download | Use when |
| --- | --- |
| **`Pixelpipe.exe`** (or the `-Windows-x64.zip` bundle) | You want a single portable executable. Drop it anywhere — Desktop, a USB stick, a synced folder — and double-click. Nothing is written to Program Files; settings live under `%APPDATA%\Pixelpipe\`. |
| **`Pixelpipe-Setup.exe`** | You want Pixelpipe registered with Windows: Start Menu entry, optional Desktop shortcut, optional auto-start with Windows, Add/Remove Programs entry. Installs to `%LOCALAPPDATA%\Programs\Pixelpipe\` per-user (no admin rights required). |

Both produce the same tray app and read the same settings file. You can install with the setup, uninstall it, and switch to the portable exe (or vice versa) without losing your profile config.

Do **not** run Pixelpipe as Administrator unless you specifically need an elevated mount. Admin-mounted drives can be hidden from normal File Explorer.

On first launch, Pixelpipe checks:

1. rclone
2. WinFsp
3. configured rclone remotes
4. optional PixelDrain API key for quota display
5. startup preference

The default Pixeldrain profile uses:

```text
Remote: Pixeldrain:
Drive:  P:
Mode:   network drive
```

## Adding other services

Right-click the tray icon:

```text
Add cloud remote
```

Then choose one of the guided entries:

```text
Google Drive
MEGA
OneDrive
Dropbox
Box
S3 / R2 / B2 / Wasabi
WebDAV / Nextcloud
SFTP
Custom existing rclone remote
```

For OAuth or credential-heavy services, Pixelpipe opens `rclone config` rather than trying to reimplement every provider's login flow. Create the remote in rclone, then return to Pixelpipe and mount it.

## Importing existing rclone remotes

Right-click the tray icon:

```text
Import existing rclone remotes
```

Pixelpipe reads `rclone listremotes`, adds any missing remotes as profiles, assigns available drive letters, and lets you edit the result from:

```text
Manage remotes...
```

## API key and quota display

The PixelDrain API key is optional.

Without it:

- mounting can still work,
- rclone storage checks may still work,
- PixelDrain transfer quota will be unavailable.

With it:

- Pixelpipe stores the key encrypted for your Windows user using DPAPI,
- the key is used to query Pixeldrain transfer quota,
- the key is not written to plain text settings.

## Bandwidth limits

The tray menu includes:

```text
Bandwidth limit
- Unlimited
- 512 KB/s
- 1 MB/s
- 5 MB/s
- 10 MB/s
- 25 MB/s
- 50 MB/s
- 100 MB/s
- 250 MB/s
- Custom...
```

When remotes are mounted by Pixelpipe, bandwidth changes are applied live through rclone RC. If no remotes are mounted, the setting is saved for the next mount.

## Diagnostics

Use:

```text
Diagnostics / repair...
```

It shows:

- rclone availability
- WinFsp availability
- configured profile list
- provider, remote, drive, mount mode, and RC port
- whether each rclone remote exists
- current status, speed, session traffic, storage text
- log file paths
- recent rclone log tails

It also provides repair buttons for rclone, WinFsp, rclone config, stale drive cleanup, settings, logs, and restart.

## Command-line flags

```text
Pixelpipe.exe /automount        Mount every profile tagged AutoMount=true and show a balloon with the count. Used by the Windows Startup entry.
Pixelpipe.exe /smoketest-menu   Run the tray menu placement and theme sanity check, then exit 0/non-zero. Used by CI.
```

`/automount` is wired up automatically when you toggle `Auto-mount at Windows startup` in the tray menu. You normally don't need to invoke it by hand.

## Building from source

From the repository root:

```powershell
.\scripts\build-release.ps1
```

Or double-click:

```text
Build-Pixelpipe.bat
```

The build script compiles the WinForms app with the current repository icon at:

```text
assets\pixelpipe.ico
```

Do not replace that icon unless you intentionally want to change the app icon.

## Security notes

- Pixelpipe does not store provider passwords except the optional PixelDrain API key.
- The PixelDrain API key is encrypted with Windows DPAPI for the current Windows user.
- Other provider credentials remain in rclone's own config system.
- Pixelpipe does not need Administrator by default.
- The EXE may trigger SmartScreen until the project has enough reputation or signed releases.
