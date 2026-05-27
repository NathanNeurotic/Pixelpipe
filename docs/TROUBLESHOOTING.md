# Troubleshooting

Start with:

```text
Right-click Pixelpipe tray icon -> Diagnostics / repair...
```

Copy the diagnostics output when reporting a bug.

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
