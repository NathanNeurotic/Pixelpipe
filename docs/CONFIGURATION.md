# Pixelpipe configuration reference

Most users should change settings through Pixelpipe's tray menu or main window. This file is for people who need to inspect, back up, compare, or carefully edit the saved configuration.

Pixelpipe settings live here:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs live here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Settings writes are atomic. Pixelpipe writes a temporary file, replaces the main file, and keeps `settings.json.bak` beside it. Before destructive profile operations, Pixelpipe also writes timestamped backups under:

```text
%APPDATA%\Pixelpipe\backups\
```

## Safe editing rules

1. Unmount profiles before editing settings by hand.
2. Exit Pixelpipe before manual edits.
3. Keep a copy of `settings.json` before changing it.
4. Do not paste raw provider passwords into `settings.json`; rclone credentials belong in rclone's config.
5. Restart Pixelpipe after saving manual edits.

If a manual edit breaks the file, restore `settings.json.bak` or a file from the `backups` folder.

## Settings shape

Pixelpipe writes one JSON object. Most user-facing state is either a top-level preference or a profile inside `Profiles`.

```json
{
  "BandwidthLimit": "off",
  "FirstLaunchSetupDone": "1",
  "SkipMissingDepWizard": "0",
  "VerboseLogging": "0",
  "WelcomeBalloonShown": "1",
  "QuickControlPinned": "1",
  "UpdateCheckEnabled": "1",
  "LastUpdateCheckUtc": "2026-05-28T16:10:18.0000000Z",
  "AvailableUpdateVersion": "v0.7.0",
  "TransferNotificationsEnabled": "1",
  "PixeldrainApiKeyProtected": "<DPAPI base64; not the raw key>",
  "Profiles": [
    {
      "Id": "f8c2...",
      "Label": "Pixeldrain",
      "Provider": "pixeldrain",
      "Remote": "Pixeldrain:",
      "DriveLetter": "P:",
      "MountMode": "network",
      "AutoMount": false,
      "FullCache": false,
      "BandwidthLimit": "",
      "BandwidthScheduleEntries": "",
      "ScheduleEnabled": false,
      "ScheduleMountTime": "",
      "ScheduleUnmountTime": "",
      "ScheduleDays": "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
      "WatchFolderEnabled": false,
      "WatchFolderPath": "",
      "WatchFolderTargetDir": "",
      "WatchFolderMode": "move",
      "WatchFolderQuietMs": 5000
    }
  ]
}
```

Pixelpipe normalizes many fields while loading:

- remote names can be written with or without a trailing colon
- invalid drive letters fall back to a safe normalized value
- invalid mount modes become `network`
- invalid global bandwidth values become `off`
- invalid watch-folder modes become `move`
- missing schedule days mean all days

## Top-level keys

| Key | Values | Notes |
| --- | --- | --- |
| `BandwidthLimit` | `off`, `512K`, `1M`, `10M`, `1.5G`, etc. | Global bandwidth setting. Applied to Pixelpipe-launched mounts through rclone RC. Invalid values fold to `off` on load. |
| `FirstLaunchSetupDone` | `0` / `1` | Set to `1` after the first-run wizard completes. |
| `SkipMissingDepWizard` | `0` / `1` | Set when the user declines the missing-dependency wizard, so it does not reopen every launch. |
| `VerboseLogging` | `0` / `1` | When `1`, Pixelpipe writes extra UI helper/debug lines to `pixelpipe-ui.log`. |
| `WelcomeBalloonShown` | `0` / `1` | Controls the one-time welcome tray balloon. |
| `QuickControlPinned` | `0` / `1` | Whether the Quick controls popup stays on top. |
| `UpdateCheckEnabled` | `0` / `1` | When `1`, Pixelpipe checks the GitHub releases API at most once per day. When `0`, automatic checks are disabled; manual update links still work. |
| `LastUpdateCheckUtc` | ISO 8601 UTC timestamp | Last successful update-check time. |
| `AvailableUpdateVersion` | release tag, or empty | Set when a newer release is found; used to show a download item in the tray menu. |
| `TransferNotificationsEnabled` | `0` / `1` | When `1`, Pixelpipe shows a tray balloon when a transfer batch finishes and the moved delta is at least 10 MB. |
| `PixeldrainApiKeyProtected` | base64 DPAPI blob | Optional Pixeldrain API key encrypted for the current Windows user. Not the raw key. |
| `Profiles` | array | Saved remote profiles. |

## Per-profile fields

| Field | Values | Notes |
| --- | --- | --- |
| `Id` | UUID-style hex | Assigned automatically. Used to derive per-profile runtime state, including the rclone RC port. |
| `Label` | free text | Display name in the tray and main window. |
| `Provider` | `pixeldrain`, `drive`, `mega`, `onedrive`, `dropbox`, `box`, `s3`, `webdav`, `sftp`, `ftp`, `custom` | Provider type used for labels and capability hints. |
| `Remote` | `Name:` | rclone remote name. Pixelpipe normalizes the trailing colon. |
| `DriveLetter` | `A:` through `Z:` | Windows drive letter. Pixelpipe refuses profile edits while the profile is mounted. |
| `MountMode` | `network` / `fixed` | `network` is recommended and is the default. |
| `AutoMount` | `true` / `false` | Mounted when Pixelpipe starts with `/automount`. |
| `FullCache` | `true` / `false` | Remembers whether the profile last mounted in full-cache mode so scheduled/startup mounts can reuse it. |
| `BandwidthLimit` | empty, or valid bandwidth value | Empty means inherit the global `BandwidthLimit`. Non-empty overrides the global value for this profile. |
| `BandwidthScheduleEntries` | empty, or comma-separated `HH:mm=limit` entries | Time-based bandwidth transitions for this profile, such as `00:00=off,09:00=1M,18:00=off`. Invalid entries are dropped when saved through the UI. |
| `ScheduleEnabled` | `true` / `false` | Enables scheduled mount/unmount when at least one schedule time is set. |
| `ScheduleMountTime` | empty or `HH:mm` local time | Local time to mount. Empty disables the mount side. |
| `ScheduleUnmountTime` | empty or `HH:mm` local time | Local time to unmount. Empty disables the unmount side. |
| `ScheduleDays` | comma-separated subset of `Mon,Tue,Wed,Thu,Fri,Sat,Sun` | Days the schedule can fire. Empty is treated as all days for older profiles. |
| `WatchFolderEnabled` | `true` / `false` | Enables watch-folder upload for this profile. |
| `WatchFolderPath` | absolute local directory path | Folder to watch. Must exist. Subfolders are not watched. |
| `WatchFolderTargetDir` | empty or relative remote path | Remote subdirectory for uploaded files. Blank means remote root. Leading/trailing slashes are stripped. |
| `WatchFolderMode` | `move` / `copy` | `move` deletes the local file only after rclone reports upload success. `copy` keeps it. |
| `WatchFolderQuietMs` | integer, 500 to 600000 | Quiet period after the last file write before upload. Defaults to 5000 ms. |

## Schedules

Mount/unmount schedules are checked every 30 seconds against the local clock. Pixelpipe records a per-day firing key so the same minute does not run more than once.

Bandwidth schedules use the same timer. Each entry is parsed as:

```text
HH:mm=limit
```

Examples:

```text
09:00=1M
00:00=off,09:00=1M,18:00=off
```

Invalid entries are ignored rather than preventing the rest of the schedule from running.

## Watch-folder behavior

When a profile's watch folder is enabled:

- Pixelpipe creates one `FileSystemWatcher` for that folder.
- Only the folder itself is watched; subdirectories are not watched.
- Created, changed, and renamed files are queued.
- Zero-byte files are skipped.
- A file uploads only after its quiet period has elapsed.
- Up to two files upload per profile at a time.
- Failed uploads retry with 30 second, 2 minute, then 10 minute backoff.
- After three failed attempts, Pixelpipe drops the entry and records the failure.
- Uploads use `rclone copyto` in copy mode and `rclone moveto` in move mode.

## Provider capability table

The static provider table in `src/ProviderCapabilities.cs` controls fallback labels when a backend does not report storage or transfer data. Real data from rclone is still shown when a backend provides it.

| Provider | Storage quota | Transfer quota | Object count |
| --- | --- | --- | --- |
| `pixeldrain` | yes | yes, via Pixeldrain API | yes |
| `drive` | yes | no | yes |
| `mega` | yes | provider hint only | yes |
| `onedrive` | yes | no | yes |
| `dropbox` | yes | no | yes |
| `box` | yes | no | yes |
| `s3` | no | no | no |
| `webdav` | server-dependent | no | no |
| `sftp` | server-dependent | no | no |
| `ftp` | no | no | no |
| `custom` / unknown | best effort | no | best effort |

## Profile import and export

Use:

```text
Tools / diagnostics -> Export profiles to file...
Tools / diagnostics -> Import profiles from file...
```

Profile export includes editable Pixelpipe profile fields. It does not include:

- `PixeldrainApiKeyProtected`
- rclone passwords or tokens
- the rclone remote configuration itself
- runtime state such as speed, current session bytes, logs, errors, or watch queue counts

Import skips profiles with duplicate `Id` values. Label and drive-letter collisions are resolved automatically.

Example export shape:

```json
{
  "_pixelpipeExport": {
    "version": "0.9",
    "exportedAt": "2026-05-28T16:10:18.0000000Z",
    "appVersion": "0.16.2",
    "machine": "DESKTOP"
  },
  "profiles": [
    {
      "Id": "...",
      "Label": "...",
      "Provider": "...",
      "Remote": "...",
      "DriveLetter": "P:",
      "MountMode": "network",
      "AutoMount": false,
      "FullCache": false,
      "BandwidthLimit": "",
      "BandwidthScheduleEntries": "",
      "ScheduleEnabled": false,
      "ScheduleMountTime": "",
      "ScheduleUnmountTime": "",
      "ScheduleDays": "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
      "WatchFolderEnabled": false,
      "WatchFolderPath": "",
      "WatchFolderTargetDir": "",
      "WatchFolderMode": "move",
      "WatchFolderQuietMs": 5000
    }
  ]
}
```

## Provider setup wizards

`Add cloud remote` opens a provider-specific flow.

| Provider | Flow |
| --- | --- |
| Pixeldrain | Pixelpipe prompts for the API key, writes a Pixeldrain rclone remote, and stores the optional quota key with DPAPI. |
| Google Drive, OneDrive, Dropbox, Box | Pixelpipe opens `rclone config`; rclone handles OAuth and browser sign-in. Pixelpipe verifies the named remote afterward. |
| MEGA | Pixelpipe prompts for remote name, email, and password. |
| S3 / R2 / B2 / Wasabi / DigitalOcean / Storj | Pixelpipe prompts for remote name, provider, access key ID, secret access key, region, and optional endpoint. |
| WebDAV / Nextcloud / SharePoint | Pixelpipe prompts for remote name, server URL, vendor, username, and password/app password. |
| SFTP | Pixelpipe prompts for remote name, host, port, username, and optional password. |
| FTP / FTPS | Pixelpipe prompts for remote name, host, port, username, optional password, and explicit TLS. |
| Custom existing rclone remote | Pixelpipe imports a remote that already appears in `rclone listremotes`. |

For non-OAuth provider forms, Pixelpipe writes directly to rclone's config file and asks rclone to obscure secret fields over stdin. Secrets are not placed on the command line and are not saved in Pixelpipe's settings file.

For OAuth providers, rclone owns the login flow because OAuth, MFA, and browser callbacks vary by provider.

## DPAPI and the Pixeldrain API key

The optional Pixeldrain API key is stored in `PixeldrainApiKeyProtected` as a base64 DPAPI blob. DPAPI binds the encrypted value to the Windows user that wrote it.

Copying `settings.json` to another machine or another Windows user will not make that key usable. Pixelpipe will treat it as missing.

To clear the saved key, use the Pixeldrain setup flow and submit a blank value, or remove `PixeldrainApiKeyProtected` from `settings.json` while Pixelpipe is closed.

## Command-line flags

| Flag | Effect |
| --- | --- |
| `/automount` | Mount every profile with `AutoMount=true` after launch. Used by the Windows startup registry entry. |
| `/smoketest-menu` | Run tray-menu placement and dark-theme sanity checks, then exit. CI gates on this. |
