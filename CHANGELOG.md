# Changelog

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
