# Changelog

## 0.2.0

Added multi-remote rclone profile support.

Added:

- Multi-profile tray menu.
- Per-profile mount / unmount / open drive controls.
- Per-profile drive letters.
- Per-profile network/fixed mount mode.
- Per-profile startup auto-mount.
- Guided Add Cloud Remote entries for Pixeldrain, Google Drive, MEGA, OneDrive, Dropbox, Box, S3-compatible storage, WebDAV, and SFTP.
- Import existing remotes through `rclone listremotes`.
- Manage Remotes window.
- Generic rclone storage display for any backend that supports `rclone about`.
- Live rclone RC stats per Pixelpipe-launched mount.
- Bandwidth changes applied to all Pixelpipe-launched mounts.
- Expanded diagnostics with per-profile RC port, rclone remote status, and log tails.

Changed:

- Pixelpipe is now Pixeldrain-first instead of Pixeldrain-only.
- PixelDrain transfer quota remains Pixeldrain-specific and is shown only when a Pixeldrain profile and API key are configured.
- Existing `assets/pixelpipe.ico` remains the build icon and is intentionally not replaced.

## 0.1.0

Initial Pixelpipe project release.

Added:

- Windows tray mount/unmount app for PixelDrain through rclone.
- First-run setup checks for rclone, WinFsp, rclone remote, API key, drive letter, and startup preference.
- Portable rclone download helper.
- winget install helpers for rclone and WinFsp.
- Optional PixelDrain API key storage with Windows DPAPI.
- Storage usage, transfer quota, session traffic, current speed, and bandwidth status.
- Live bandwidth limit changes through rclone RC.
- Drive-letter selector.
- Network/fixed mount mode selector.
- Auto-remount option.
- Themed dark tray menu.
- Diagnostics / repair window.
- Settings JSON under `%APPDATA%\Pixelpipe\settings.json`.
- Logs under `%LOCALAPPDATA%\Pixelpipe\logs\`.
- Optional Inno Setup installer script.
- Rolling GitHub Actions release workflow.
