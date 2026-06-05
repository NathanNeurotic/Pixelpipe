# Pixelpipe user guide

This guide is for normal use: getting a cloud drive mounted, understanding what Pixelpipe is telling you, and fixing common problems without needing to know the code.

If you only remember one thing: start from the tray icon. Almost every action is there.

## The mental model

Pixelpipe is a control panel for rclone mounts.

- rclone talks to the cloud provider.
- WinFsp makes the rclone mount appear as a Windows drive.
- Pixelpipe manages profiles, menus, logs, status, setup, and repair.

A **profile** is Pixelpipe's saved view of one rclone remote: label, provider, remote name, drive letter, mount mode, bandwidth choices, schedule, and optional watch folder.

A **remote** is the rclone configuration itself, such as `Pixeldrain:`, `GoogleDrive:`, or `Backups:`.

Pixelpipe profiles do not replace rclone remotes. If you export Pixelpipe profiles to another computer, the rclone remote still needs to exist there too.

## First launch

Run Pixelpipe normally. Avoid **Run as administrator** unless you deliberately need an elevated mount.

The first-run wizard checks:

1. rclone
2. WinFsp
3. whether at least one rclone remote exists
4. optional Pixeldrain API key for quota display

If rclone is missing, Pixelpipe can download a portable copy under:

```text
%USERPROFILE%\Apps\rclone\rclone.exe
```

If WinFsp is missing, Pixelpipe can start a winget install helper. A restart may be needed before mounts work.

## SmartScreen

Pixelpipe downloads are unsigned. Windows may show **"Windows protected your PC"** on first launch.

Click **More info**, then **Run anyway**. To verify the file before running it:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

Compare the result with the release's `SHA256SUMS.txt`.

## Add your first cloud remote

Right-click the tray icon and choose:

```text
Add cloud remote
```

Provider choices:

| Provider | What to expect |
| --- | --- |
| Pixeldrain | Pixelpipe asks for an API key and writes the Pixeldrain rclone remote. |
| Google Drive, OneDrive, Dropbox, Box | rclone opens its browser sign-in flow. Finish sign-in, return to Pixelpipe, then confirm. |
| MEGA | Pixelpipe asks for email and password, then writes the rclone remote. |
| S3 / R2 / B2 / Wasabi / DigitalOcean / Storj | Pixelpipe asks for provider, access key, secret key, region, and optional endpoint. |
| WebDAV / Nextcloud / SharePoint | Pixelpipe asks for server URL, vendor, username, and password or app password. |
| SFTP | Pixelpipe asks for host, port, username, and optional password. Empty password allows key-agent/default-key auth. |
| FTP / FTPS | Pixelpipe asks for host, port, username, optional password, and explicit TLS choice. |
| Custom existing rclone remote | Pixelpipe imports an already configured rclone remote. |

Secrets are not stored in Pixelpipe's `settings.json`. rclone credentials remain in rclone's own config system. The optional Pixeldrain quota key is stored separately with Windows DPAPI encryption.

## Import existing rclone remotes

If you already have rclone configured:

```text
Import existing rclone remotes
```

Pixelpipe runs `rclone listremotes`, adds missing remotes as profiles, and assigns free drive letters.

If the remote does not show up, open:

```text
Setup / dependencies -> Open rclone config in terminal
```

Confirm the remote exists there first.

## Mount and unmount

Each profile has these common actions:

| Action | Use when |
| --- | --- |
| `Mount - low overhead` | You want the normal, lighter mount mode. Start here. |
| `Mount - full cache` | You need heavier VFS caching for workloads that re-read or seek through files. This can use local disk. |
| `Unmount` | You are done with the drive or need to change profile settings. |
| `Open drive` | Open the mounted drive in File Explorer. |
| `Test profile` | Check dependencies, remote config, drive letter, RC port, and storage probe before mounting. |

If a drive letter is already in use, pick another letter in the profile editor.

## Profile editor

Open the main window, go to Profiles, then use the profile menu and choose `Edit`.

The editor has four tabs.

## General tab

Use this for the basics:

- Label: the name shown in Pixelpipe.
- Provider: the provider type, such as `pixeldrain`, `drive`, `s3`, or `custom`.
- rclone remote: the remote name, usually ending in `:`.
- Drive letter: the Windows drive letter.
- Mount as network drive: recommended for most cloud mounts.
- Auto-mount this profile at Pixelpipe startup: used when Pixelpipe starts with `/automount`.

You must unmount a profile before editing it.

## Bandwidth tab

Use the per-profile bandwidth limit when one remote needs a different limit from the global tray setting.

The bandwidth schedule lets a profile change limits at specific local times:

```text
00:00=off,09:00=1M,18:00=off
```

That example means:

- unlimited at midnight
- 1 MB/s at 09:00
- unlimited again at 18:00

Valid examples include `off`, `512K`, `1M`, `25M`, `250M`, and custom values like `1.5G`.

## Schedule tab

Use schedules when a remote should mount or unmount automatically.

Examples:

| Goal | Mount at | Unmount at | Days |
| --- | --- | --- | --- |
| Workday cloud drive | `08:30` | `18:00` | Mon-Fri |
| Overnight upload drive | `22:00` | `06:00` | all days |
| Mount only, no automatic unmount | `09:00` | blank | selected days |

Times are local 24-hour times. Pixelpipe checks schedules every 30 seconds, so it catches the minute without needing exact second precision.

## Watch tab

Watch folders are for simple automatic uploads.

When enabled, Pixelpipe watches one local folder. When a new or changed file becomes quiet for the configured quiet period, Pixelpipe uploads it with rclone.

| Setting | Meaning |
| --- | --- |
| Watch folder path | Local folder to monitor. Subfolders are not watched. |
| Remote subdir | Optional folder on the remote. Blank means remote root. |
| Mode | `move` deletes the local file after upload. `copy` keeps it. |
| Quiet period | How long to wait after the last write before uploading. Default is 5000 ms. |

Watch-folder notes:

- Zero-byte placeholder files are skipped.
- Pixelpipe uploads up to two files per profile at a time.
- Failed uploads retry with backoff, then stop after three attempts.
- In `move` mode, the local file is removed only after rclone reports success.

## Bandwidth menu

The global tray menu has:

```text
Bandwidth limit
```

This applies to Pixelpipe-launched mounts. If a mount is already running, Pixelpipe applies the change live through rclone RC. If nothing is mounted, the setting is saved for the next mount.

Per-profile bandwidth settings override the global value for that profile.

## Activity, logs, and diagnostics

Use the main window when you want more context than the tray menu can show:

| Tab | Use it for |
| --- | --- |
| Profiles | Current mount state and primary profile actions. |
| Activity | A timeline of mount, unmount, schedule, transfer, watch, and error events. |
| Diagnostics | Dependency status, profile details, preflight results, repair buttons. |
| Logs | Pixelpipe UI log and per-profile rclone logs. |
| Settings | Dependencies, Pixeldrain key, update check preference, startup, notifications, maintenance. |

For bug reports, use:

```text
Tools / diagnostics -> Copy diagnostics
```

Review the copied text before posting it publicly. Pixelpipe scrubs common secret patterns, but logs can still contain paths, remote names, or provider output.

## Settings and backups

Pixelpipe stores settings here:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs are here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Before profile removal and other destructive profile operations, Pixelpipe keeps timestamped backups under:

```text
%APPDATA%\Pixelpipe\backups\
```

Use:

```text
Tools / diagnostics -> Open settings backups folder
```

The app also keeps a rolling `settings.json.bak` beside the main settings file for recovery after a bad write.

## Updates

Pixelpipe does not silently auto-install updates.

When update checks are enabled, Pixelpipe checks GitHub releases at most once per day and may show a download item in the tray menu when a newer release exists. You can disable this in Settings. The manual `Check for updates` action opens the GitHub releases page.

## Good habits

- Run Pixelpipe normally, not elevated.
- Use `Test profile` before debugging a mount by guesswork.
- Prefer `Mount - low overhead` until you know you need full cache.
- Keep watch folders small and intentional.
- Use `copy` mode before trusting a new watch-folder workflow with important files.
- Keep rclone and WinFsp reasonably current.
- Include copied diagnostics when reporting bugs.
