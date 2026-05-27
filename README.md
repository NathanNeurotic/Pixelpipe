<img width="1774" height="887" src="https://github.com/user-attachments/assets/eccffc6c-2cce-404e-82fd-e6b72a483e33" />


# Pixelpipe

Pixelpipe is a small Windows tray app for mounting your PixelDrain filesystem as a Windows drive through rclone.

It is built around one goal: make PixelDrain filesystem access feel like a normal drive, without keeping a console window open.

## Features

- Mount and unmount PixelDrain from the Windows system tray.
- First-run setup wizard for rclone, WinFsp, the PixelDrain rclone remote, drive letter, startup behavior, and optional API key.
- Dependency checks for rclone, WinFsp, and winget.
- Portable rclone download fallback when rclone is missing.
- Optional winget install paths for rclone and WinFsp.
- Optional PixelDrain API key storage for account usage and transfer quota display.
- API key is encrypted for the current Windows user with DPAPI.
- Live tray menu status for mount state, storage usage, transfer quota, session traffic, current speed, and bandwidth limit.
- Live rclone bandwidth control through rclone RC.
- Drive-letter selector. Defaults to `P:` but supports custom letters.
- Network-drive mount mode by default for better `This PC` visibility.
- Fixed-drive mount mode as an alternate option.
- Auto-remount option if rclone exits unexpectedly.
- Themed dark tray menu instead of the generic plain Windows menu look.
- Diagnostics / repair window with copyable diagnostics, dependency repair actions, stale drive cleanup, log access, and restart mount action.
- Normal-user startup support.
- GitHub Actions rolling release build on every push.
- Optional Inno Setup installer script.

## Requirements

Pixelpipe is for Windows 10/11.

Under the hood it uses:

- rclone
- WinFsp
- A configured rclone PixelDrain remote
- PixelDrain filesystem access

Pixelpipe does not turn normal public PixelDrain file links into a writable filesystem. It is meant for PixelDrain's filesystem feature as exposed through rclone's PixelDrain backend.

## Quick start

Download `Pixelpipe.exe` from the latest release or rolling release, then run it normally.

Do not run Pixelpipe as Administrator unless you specifically need an elevated mount. Admin-mounted drives can be hidden from normal File Explorer.

On first launch, Pixelpipe checks:

1. rclone
2. WinFsp
3. the `Pixeldrain:` rclone remote
4. optional PixelDrain API key for quota display
5. drive letter
6. startup preference

The default drive letter is:

```text
P:
```

## First-run setup behavior

If rclone is missing, Pixelpipe can:

- download the portable Windows rclone build into `%USERPROFILE%\Apps\rclone`, or
- attempt installation through winget.

If WinFsp is missing, Pixelpipe can attempt installation through winget. A restart may be required after installing WinFsp.

If winget is missing, Pixelpipe opens the Microsoft App Installer / winget help path instead of pretending it can silently repair Windows package management.

## API key and quota display

The PixelDrain API key is optional.

Without it:

- mounting can still work if your rclone remote is already configured;
- transfer quota display will be unavailable.

With it:

- Pixelpipe can configure the rclone remote;
- Pixelpipe can show transfer quota and recent transfer usage;
- the key is encrypted with Windows DPAPI for the current Windows user.

The encrypted key is stored in:

```text
%APPDATA%\Pixelpipe\settings.json
```

The raw key is not intentionally written to logs or plain config by Pixelpipe. rclone may store its own obscured remote credentials in rclone's config.

## Tray menu

The tray menu contains:

```text
Status
Storage usage
Transfer quota
Session traffic
Current speed
Bandwidth limit

Mount / Unmount
Open drive
Bandwidth limit profiles
Drive letter selector
Mount mode selector
Auto-remount toggle
PixelDrain API key controls
Setup / dependencies
Settings
Refresh usage
Open log
Open settings file
Diagnostics / repair
Copy diagnostics
Check for updates
Startup toggle
Exit
```

## Bandwidth limits

Built-in profiles include:

```text
Unlimited
1 MB/s
5 MB/s
10 MB/s
25 MB/s
50 MB/s
100 MB/s
250 MB/s
Custom...
```

When mounted, Pixelpipe changes the live rclone bandwidth limit through rclone RC. When unmounted, the value is saved and applied to the next mount.

## Settings and logs

Settings:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Main rclone log:

```text
%LOCALAPPDATA%\Pixelpipe\logs\rclone-mount.log
```

## Building locally

From the repo root:

```powershell
.\scripts\build-release.ps1
```

Or double-click:

```text
Build-Pixelpipe.bat
```

The double-click build writes:

```text
%USERPROFILE%\Desktop\Pixelpipe.exe
```

The release build writes:

```text
dist\Pixelpipe.exe
```

## Building the installer

Install Inno Setup 6, then run:

```powershell
.\scripts\build-release.ps1
.\scripts\build-installer.ps1
```

Expected installer output:

```text
dist\Pixelpipe-Setup.exe
```

## Rolling release CI

The GitHub Actions workflow builds on every push to `main` or `master`.

Push builds update the `rolling` prerelease and upload:

```text
Pixelpipe.exe
Pixelpipe-Windows-x64.zip
Pixelpipe-Setup.exe, if installer build succeeds
SHA256SUMS.txt
```

Pull requests build artifacts only and do not publish a release.

## Troubleshooting

Use:

```text
Right-click tray icon -> Diagnostics / repair...
```

That window can:

- check rclone;
- check WinFsp;
- check the rclone remote;
- copy diagnostics;
- open logs;
- configure the remote;
- clear stale drive mappings;
- restart the mount.

More details are in [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).

## Security notes

- Pixelpipe should run as your normal Windows user.
- Do not run it as Administrator by default.
- API key storage uses Windows DPAPI for the current user.
- Builds are unsigned unless you sign them yourself.
- Unsigned EXEs downloaded from GitHub may trigger SmartScreen.

## License

MIT. See [LICENSE](LICENSE).
