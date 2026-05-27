# Pixelpipe

A small Windows tray utility that mounts your PixelDrain filesystem as a drive through rclone, with live storage/traffic status, transfer quota display, bandwidth limit controls, startup auto-mount, and first-launch dependency setup.

## Features

- Mount/unmount your PixelDrain rclone remote `Pixeldrain:` as `P:\` from the system tray.
- First-launch setup wizard for rclone, WinFsp, PixelDrain remote configuration, and optional API key storage.
- Downloads portable rclone to `%USERPROFILE%\Apps\rclone\rclone.exe` when rclone is missing.
- Uses winget for optional rclone/WinFsp installation paths.
- Prompts for a PixelDrain API key for quota display and stores it encrypted with Windows DPAPI for the current Windows user.
- Shows storage usage, monthly transfer quota, session traffic, current speed, and bandwidth limit in the tray menu.
- Supports live rclone bandwidth limit changes through rclone RC.
- Uses `--network-mode` so the mount is more likely to show in Windows “This PC”.
- Normal-user startup support. Do not run as Administrator unless you intentionally want an elevated-only mount.

## Requirements

- Windows 10/11.
- PixelDrain filesystem access. PixelDrain’s rclone backend is for the paid/prepaid filesystem feature, not normal free one-off PixelDrain links.
- WinFsp for Windows filesystem mounting.
- rclone with PixelDrain backend support.

The app can help install/configure the pieces, but WinFsp is a system filesystem driver and may require UAC/admin approval.

## Quick start

1. Download or build `Pixelpipe.exe`.
2. Run it normally. Do **not** run as Administrator.
3. On first launch, follow the setup prompts.
4. Right-click the tray icon and choose `Mount Pixelpipe - low overhead`.
5. Open `P:\` from the tray menu or from Windows Explorer.

## Building from source

Double-click:

```bat
Build-Pixelpipe.bat
```

The output is written to:

```text
%USERPROFILE%\Desktop\Pixelpipe.exe
```

Or from PowerShell:

```powershell
.\scripts\build-release.ps1
```

The project intentionally uses classic .NET Framework WinForms and `csc.exe` so it can build on a stock Windows machine without a heavyweight project system.

## First-launch setup behavior

On first run, the app checks:

- `rclone.exe`
- WinFsp
- the `Pixeldrain:` rclone remote
- optional PixelDrain API key for quota display

If rclone is missing, the app can download the official portable Windows AMD64 rclone zip and copy `rclone.exe` to:

```text
%USERPROFILE%\Apps\rclone\rclone.exe
```

If WinFsp is missing, the app offers to install it through winget:

```powershell
winget install -e --id WinFsp.WinFsp --accept-package-agreements --accept-source-agreements
```

If winget is missing, the app opens Microsoft’s winget/App Installer help and Store page.

## PixelDrain API key

The API key is used for two separate things:

1. rclone remote configuration for your PixelDrain filesystem.
2. Optional transfer quota display in the tray menu.

When saved for quota display, the key is encrypted with Windows DPAPI using `DataProtectionScope.CurrentUser`. That means the stored secret is tied to the current Windows user profile.

## Bandwidth limits

The tray menu can set live limits such as:

- Unlimited
- 1 MB/s
- 5 MB/s
- 10 MB/s
- 25 MB/s
- 50 MB/s
- 100 MB/s
- 250 MB/s

The running rclone mount is started with rclone Remote Control enabled on localhost, and the app sends `core/bwlimit` updates to that local rclone instance.

## Troubleshooting

### P:\ does not appear in This PC

Make sure the tray app was started normally, not as Administrator. Elevated mounts can be invisible to normal Explorer.

Then use:

```powershell
Test-Path P:\
net use
Get-Process rclone
```

Also check the tray menu:

```text
Open rclone log
Copy diagnostics
```

### Mount fails immediately

Common causes:

- WinFsp is missing.
- `P:` is already in use.
- `Pixeldrain:` is not configured in rclone.
- `rclone.exe` is set to “Run this program as administrator” in Compatibility settings.
- PixelDrain filesystem plan/API key does not have filesystem access.

### The app asks to install dependencies but nothing happens

Open `Setup / dependencies` from the tray menu and run the installers manually. WinFsp installation may need UAC approval.

## Security notes

- The app does not bundle rclone or WinFsp binaries.
- Portable rclone download uses the official rclone current Windows AMD64 zip URL.
- API key storage uses Windows DPAPI for the current Windows user.
- rclone RC is bound to `127.0.0.1` only.
- No telemetry is included.

## License

MIT. See `LICENSE`.
