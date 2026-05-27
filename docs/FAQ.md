# Pixelpipe FAQ

## Does Pixelpipe mount normal public PixelDrain file links?

No. Pixelpipe mounts a PixelDrain filesystem remote through rclone. Normal one-off public file links are not a writable drive.

## Why does Pixelpipe need WinFsp?

rclone mount uses WinFsp on Windows to expose a FUSE-like filesystem as a drive.

## Should I run Pixelpipe as Administrator?

No, not by default. Admin-mounted drives can be invisible to normal File Explorer. Run Pixelpipe normally unless you intentionally need an elevated mount.

## Why does the drive not appear in This PC?

Common causes:

- Pixelpipe was launched as Administrator.
- WinFsp is missing or needs a restart.
- The selected drive letter is already used.
- rclone exited immediately.
- the PixelDrain rclone remote is not configured.

Open `Diagnostics / repair...` from the tray menu and copy diagnostics.

## Why is quota unavailable?

Quota requires a PixelDrain API key. Mounting can still work without the API key if rclone is already configured.

## Where is the API key stored?

Pixelpipe encrypts it with Windows DPAPI for the current user and stores the protected value in:

```text
%APPDATA%\Pixelpipe\settings.json
```

## Does Pixelpipe auto-update?

No. It has a `Check for updates` menu item that opens the GitHub releases page. Silent auto-update is intentionally not included yet.
