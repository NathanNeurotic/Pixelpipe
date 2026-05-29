# Troubleshooting

Start with:

```text
Right-click Pixelpipe tray icon -> Diagnostics / repair...
```

Copy the diagnostics output when reporting a bug.

## "Windows protected your PC" appears when I launch the download

Expected — Pixelpipe's builds are **not code-signed**, so Windows SmartScreen flags `Pixelpipe.exe` and `Pixelpipe-Setup.exe` as an *unrecognized app* on first run. It is not a malware detection; every unsigned open-source app does this.

```text
Click "More info"  ->  Click "Run anyway"
```

You only do this once per downloaded file. To confirm the download is genuine before running, compare it against the release's `SHA256SUMS.txt`:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

Removing the prompt for good requires a paid code-signing certificate the project doesn't have yet.

## rclone is missing

Use one of these from the tray menu:

```text
Setup / dependencies -> Download portable rclone now
Setup / dependencies -> Install/update rclone with winget
```

Portable rclone is installed to:

```text
%USERPROFILE%\Apps\rclone\rclone.exe
```

## WinFsp is missing

Use:

```text
Setup / dependencies -> Install WinFsp with winget
```

After WinFsp installation, restart Windows if mounting still fails.

## winget is missing

Pixelpipe opens the Microsoft winget/App Installer help path. Install App Installer from Microsoft, then reopen Pixelpipe.

## P: does not show up

Check these first:

1. Make sure Pixelpipe is not running as Administrator.
2. Open Diagnostics / repair and confirm WinFsp is installed.
3. Confirm the selected drive letter is not already used.
4. Try `Drive letter -> X:` or `Z:`.
5. Keep `Mount mode -> Network drive` selected.
6. Check the rclone log from the tray menu.

## rclone gets stuck

Use:

```text
Unmount Pixelpipe
Diagnostics / repair -> Clear stale drive
```

If Windows refuses to release a wedged WinFsp mount, restart Windows.

## Quota shows unavailable

Check:

1. PixelDrain API key is saved.
2. API key is valid.
3. Windows can reach `pixeldrain.com` over HTTPS.
4. Your account exposes quota fields through the API.

Pixelpipe also has a curl fallback for older .NET TLS behavior.

## Bandwidth limit does not change live

Live bandwidth changes require the current rclone mount to be running with RC enabled. Pixelpipe starts its own mounts with RC enabled. If an old manually-started rclone process is running, unmount it and remount from Pixelpipe.
