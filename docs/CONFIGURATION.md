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
      "FullCache": false
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

## Command-line flags

| Flag | Effect |
| --- | --- |
| `/automount` | Pixelpipe mounts every profile with `AutoMount=true` ~5 s after launch and shows a balloon with the count. Set by the Windows Startup registry entry when you toggle `Auto-mount at Windows startup` in the tray. |
| `/smoketest-menu` | Runs the tray-menu placement-math and dark-theme sanity check, then exits. Exit code 0 means OK. CI gates on this; you can run it yourself any time. |

## DPAPI and the API key

The optional PixelDrain API key is stored in `PixeldrainApiKeyProtected` as a base64 DPAPI blob. DPAPI binds the encryption to the Windows account. Copying the settings file to another machine or another Windows user will silently fail to decrypt the key (Pixelpipe will treat the key as missing).

To clear the saved API key, open `Setup / dependencies → Configure Pixeldrain remote` and submit a blank value, or delete the `PixeldrainApiKeyProtected` entry from the settings file.
