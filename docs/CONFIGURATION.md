# Pixelpipe configuration

Pixelpipe stores editable app settings here:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs are stored here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

## Typical settings

```json
{
  "BandwidthLimit": "off",
  "DriveLetter": "P:",
  "RemoteName": "Pixeldrain:",
  "MountMode": "network",
  "AutoRemount": "0",
  "FirstLaunchSetupDone": "1"
}
```

## DriveLetter

Default:

```text
P:
```

Use a free drive letter. Pixelpipe refuses to change the drive letter while mounted.

## RemoteName

Default:

```text
Pixeldrain:
```

The trailing colon is optional in the settings UI. Pixelpipe normalizes it internally.

## MountMode

Supported values:

```text
network
fixed
```

`network` is the default because it tends to behave better in `This PC` with rclone/WinFsp cloud mounts.

## AutoRemount

Supported values:

```text
0
1
```

When enabled, Pixelpipe tries to remount if the rclone mount exits unexpectedly. It stops after repeated failures instead of looping forever.

## API key

The optional PixelDrain API key is stored as encrypted DPAPI data in the settings file under:

```text
PixeldrainApiKeyProtected
```

That value is not the raw API key. It can only be decrypted by the same Windows user profile that stored it.
