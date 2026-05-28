# Pixelpipe configuration

Pixelpipe stores editable settings here:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs go here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

## Settings file shape

Pixelpipe writes one JSON object with a per-profile array. Pixelpipe normalizes most fields when it loads the file, so trailing colons on remote names and unset fields are tolerated.

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
  "PixeldrainApiKeyProtected": "<DPAPI base64; not the raw key>",
  "Profiles": [
    {
      "Id": "f8c2…",
      "Label": "Pixeldrain",
      "Provider": "pixeldrain",
      "Remote": "Pixeldrain:",
      "DriveLetter": "P:",
      "MountMode": "network",
      "AutoMount": false,
      "FullCache": false,
      "BandwidthLimit": "",
      "ScheduleEnabled": false,
      "ScheduleMountTime": "",
      "ScheduleUnmountTime": "",
      "ScheduleDays": "Mon,Tue,Wed,Thu,Fri,Sat,Sun"
    }
  ]
}
```

Pixelpipe writes `settings.json` atomically (`.tmp` write → rename, keeping the previous file as `.bak`). If the main file is unreadable on next launch — e.g. truncated by a power loss mid-write — Pixelpipe transparently loads `settings.json.bak` and logs a `[warn]` line.

## Top-level keys

| Key | Values | Notes |
| --- | --- | --- |
| `BandwidthLimit` | `off`, `512K`, `1M`, `10M`, `1.5G`, … | Applied to Pixelpipe-launched mounts live through rclone RC. Validated against `^(off\|\d+(\.\d+)?[KMG]?)$`; invalid values fold to `off` on load. |
| `FirstLaunchSetupDone` | `0` / `1` | Pixelpipe sets to `1` after the wizard runs once. |
| `SkipMissingDepWizard` | `0` / `1` | Set to `1` if you decline the wizard while dependencies are missing, so it doesn't re-open every launch. Re-run from `Setup / dependencies → Run first-time setup wizard`. |
| `VerboseLogging` | `0` / `1` | Toggle in Settings → Preferences. When `1`, Pixelpipe writes `[debug]` lines for menu placement and refresh timing to `pixelpipe-ui.log`. |
| `WelcomeBalloonShown` | `0` / `1` | Set to `1` after the one-time welcome balloon ("Pixelpipe is in your system tray…") fires on a non-`/automount` launch. Delete or reset to `0` to make it show again. |
| `QuickControlPinned` | `0` / `1` | Whether the Quick controls popup stays on top of other windows. Toggled by the "Pin on top" checkbox inside the popup. |
| `UpdateCheckEnabled` | `0` / `1` | When `1` (default), Pixelpipe checks the GitHub releases API once per tray-menu open (throttled to once per 24 h). When `0`, never checks; you can still use `Check for updates` to open the releases page manually. Toggle from Settings → Preferences → "Check GitHub for new releases". |
| `LastUpdateCheckUtc` | ISO 8601 UTC | Timestamp of the last successful update check. Pixelpipe uses this to throttle to one check per day. |
| `AvailableUpdateVersion` | tag string like `v0.7.0`, or empty | Set when an update check finds a newer release; surfaces a "Pixelpipe vX.Y.Z available — download" item at the top of the tray menu. Cleared when the user opens the releases page from that item, or when a later check shows no update. |
| `TransferNotificationsEnabled` | `0` / `1` | When `1` (default), Pixelpipe shows a balloon (`<profile>: transfer finished — N MB moved`) when an rclone transfer batch ends, but only if the delta is ≥ 10 MB. Toggle from Settings → Preferences → "Notify when a transfer batch finishes". |
| `PixeldrainApiKeyProtected` | base64 DPAPI blob | Encrypted by DPAPI for the current Windows user. Not the raw API key. Only decryptable by the same Windows account that wrote it. |
| `Profiles` | array | Each entry is a remote profile (see below). |

## Per-profile fields

| Field | Values | Notes |
| --- | --- | --- |
| `Id` | UUID-style hex | Assigned automatically; controls the rclone RC port for that profile. |
| `Label` | free text | Display name in the tray. |
| `Provider` | `pixeldrain`, `drive`, `mega`, `onedrive`, `dropbox`, `box`, `s3`, `webdav`, `sftp`, `ftp`, `custom` | Mostly cosmetic; Pixelpipe normalizes whatever is in the file. |
| `Remote` | `Name:` | The rclone remote name with trailing colon. |
| `DriveLetter` | `A:` – `Z:` | Pixelpipe refuses to change a profile's drive letter while it is mounted. |
| `MountMode` | `network` / `fixed` | `network` shows under `This PC → Network locations`; `fixed` shows under drives. `network` is the default. |
| `AutoMount` | `true` / `false` | When the user starts Pixelpipe with `/automount`, profiles with `true` get mounted. |
| `FullCache` | `true` / `false` | Records the last mount mode used so auto-remount picks the same one. `Mount – full cache` sets it to `true`; `Mount – low overhead` sets it to `false`. |
| `BandwidthLimit` | empty, or `off` / `512K` / `1M` / `1.5G` / … | Per-profile bandwidth override. Empty (default) means inherit the global `BandwidthLimit`. Any valid value is passed to `rclone mount --bwlimit` at launch and to `rc core/bwlimit` on live changes for this profile only. Editable from the per-profile Edit dialog. |
| `ScheduleEnabled` | `true` / `false` | When `true` and at least one of `ScheduleMountTime` / `ScheduleUnmountTime` is set, Pixelpipe automatically mounts and/or unmounts this profile at the scheduled local time on each `ScheduleDays` day. |
| `ScheduleMountTime` | empty or `HH:mm` (24-hour, local) | Local time at which to mount the profile. Empty disables the mount side of the schedule. |
| `ScheduleUnmountTime` | empty or `HH:mm` (24-hour, local) | Local time at which to unmount the profile. Empty disables the unmount side of the schedule. |
| `ScheduleDays` | comma-separated subset of `Mon,Tue,Wed,Thu,Fri,Sat,Sun` | Days the schedule fires on. Defaults to all seven. Empty value is treated as "all days" for backwards compatibility. |

## Command-line flags

| Flag | Effect |
| --- | --- |
| `/automount` | Pixelpipe mounts every profile with `AutoMount=true` ~5 s after launch and shows a balloon with the count. Set by the Windows Startup registry entry when you toggle `Auto-mount at Windows startup` in the tray. |
| `/smoketest-menu` | Runs the tray-menu placement-math and dark-theme sanity check, then exits. Exit code 0 means OK. CI gates on this; you can run it yourself any time. |

## Provider capability table

Pixelpipe ships a static table (`src/ProviderCapabilities.cs`) describing what each backend can report:

| Provider | Storage quota | Transfer quota | Object count |
| --- | --- | --- | --- |
| `pixeldrain` | yes | yes (via Pixeldrain API) | yes |
| `drive` (Google Drive) | yes | no | yes |
| `onedrive` | yes | no | yes |
| `dropbox` | yes | no | yes |
| `box` | yes | no | yes |
| `mega` | yes | (hint only — read on the MEGA web account) | yes |
| `s3` (S3, R2, B2, Wasabi) | no | no | no |
| `webdav` | no (server-dependent) | no | no |
| `sftp` | yes (when `statfs` works) | no | no |
| `ftp` | no | no | no |
| `custom` / unknown | yes (best-effort) | no | yes |

When a flag is `no`, the UI shows "not applicable for this provider" or a provider-specific hint instead of "0" or "unavailable". When a flag is `yes` but the backend doesn't respond (e.g. a WebDAV server that doesn't implement quota), the line shows "not reported by backend".

## Profile import / export

`Tools / diagnostics → Export profiles to file…` writes a JSON file containing every profile's editable fields. `Import profiles from file…` reads such a file and lets you pick a profile to add.

- Encrypted secrets (`PixeldrainApiKeyProtected`, rclone passwords inside rclone config) are **not** included — DPAPI binds them to the writing Windows account, so they're useless on another machine.
- rclone remote configuration itself is **not** exported. Pixelpipe only manages the *display profile*; the underlying `rclone config` file still needs to be in place on the target machine (or you'll re-create the remote there).
- Profiles whose `Id` already exists in the receiving Pixelpipe are skipped. Drive letter and label collisions are resolved automatically by picking the next free letter and suffixing the label.
- Export file shape:

```json
{
  "_pixelpipeExport": { "version": "0.9", "exportedAt": "2026-…Z", "appVersion": "0.9.0", "machine": "DESKTOP" },
  "profiles": [ { "Id": "…", "Label": "…", "Provider": "…", "Remote": "…", "DriveLetter": "…", "MountMode": "network", "AutoMount": false, "FullCache": false, "BandwidthLimit": "", "ScheduleEnabled": false, "ScheduleMountTime": "", "ScheduleUnmountTime": "", "ScheduleDays": "Mon,Tue,Wed,Thu,Fri,Sat,Sun" } ]
}
```

## Provider setup wizards

`Add cloud remote` in the tray menu and main window opens a per-provider wizard that drives `rclone config create` directly for non-OAuth backends:

| Provider | Wizard fields |
| --- | --- |
| Google Drive / OneDrive / Dropbox / Box | Remote name (browser sign-in happens in the `rclone config` terminal; Pixelpipe verifies after) |
| MEGA | Remote name, email, password |
| S3 / R2 / B2 / Wasabi / DigitalOcean / Storj | Remote name, provider, access key ID, secret access key, region, optional endpoint |
| WebDAV / Nextcloud / SharePoint | Remote name, server URL, vendor, user, password (or app password) |
| SFTP | Remote name, host, port (default 22), user, optional password (empty → ssh-agent / default key) |
| FTP / FTPS | Remote name, host, port, user, password, `explicit_tls` toggle |
| Pixeldrain | Existing API-key flow |

After the wizard finishes:

- Pixelpipe runs `rclone config create … --non-interactive` with the values.
- It then checks `rclone listremotes` and only adds the Pixelpipe profile when the new remote shows up.
- On failure, the rclone output is shown (scrubbed of obvious secrets) and the profile is **not** created so the user can correct the inputs and re-run.

Secret handling: Pixelpipe never persists the password / secret it collected. Each wizard passes it once to `rclone config create`, which stores it obfuscated in `rclone.conf`. Pixelpipe's `settings.json` carries no rclone credentials.

## DPAPI and the API key

The optional PixelDrain API key is stored in `PixeldrainApiKeyProtected` as a base64 DPAPI blob. DPAPI binds the encryption to the Windows account. Copying the settings file to another machine or another Windows user will silently fail to decrypt the key (Pixelpipe will treat the key as missing).

To clear the saved API key, open `Setup / dependencies → Configure Pixeldrain remote` and submit a blank value, or delete the `PixeldrainApiKeyProtected` entry from the settings file.
