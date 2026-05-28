# Changelog

## 0.3.1

Tray submenu placement, take 2.

Fixed:

- Tray submenus (Setup / dependencies, Tools / diagnostics, Bandwidth limit, Add cloud remote, profile submenus) were still opening at the top-left corner of the desktop on some setups instead of next to their parent item. The previous fix re-anchored via `dropDown.Location = ...` in the `DropDownOpened` event, but WinForms' submenu host appears to ignore post-show `Location` writes in this configuration. Repositioning now hooks both `DropDownOpening` (before the popup is shown) and `DropDownOpened`, forces a layout pass so the dropdown size is known, and as a final hammer calls `user32.SetWindowPos` directly with the computed coordinates. Detailed values are written to `pixelpipe-ui.log` on every submenu open so any remaining placement failure is diagnosable from the log.

## 0.3.0

Tray menu placement, refactor, and tests.

Added:

- PerMonitorV2 DPI awareness in the Windows manifest so the tray menu and submenus position correctly under mixed-DPI multi-monitor setups.
- `/smoketest-menu` non-interactive check verifies tray submenu placement math and dark-theme application without spawning the tray; gated by CI.
- Unit test runner `scripts\run-tests.ps1` over the pure helpers and tray menu placement math; gated by CI.
- Shared tray submenu placement and theming helpers in `src/TrayMenu.cs`.
- `docs/SMOKE_TEST.md` manual checklist for mount, unmount, bandwidth, setup, diagnostics, menu placement, and startup.
- `.editorconfig` pins CRLF on Windows-targeted files and standard indentation.
- `Tools / diagnostics` submenu that groups logs, settings, refresh, and update actions to keep the top-level tray menu shorter.
- UI log entries for previously silent failures in settings persistence, profile load/save, first-launch setup, mount post-launch state checks, mount health monitor, ClearApiKey, and DetectProviderForRemote.

Changed:

- `TrayContext` split into partial files by domain (`Pixelpipe.cs` core, plus `.Setup`, `.Profiles`, `.Mount`, `.Refresh`, `.Diagnostics`, `.Settings`, `.Helpers`); `Pixelpipe.cs` is now ~220 lines instead of 2,209.
- Tray submenus now re-anchor to their parent item and clamp inside the active screen, fixing the bug where they popped to the top-left of the desktop in WinForms' default style.
- Stuck-unmount fallback defaults to No, explains what Yes does, logs the choice, and silent paths no longer block on it.
- Menu-open refresh work is throttled to once per 30 seconds.
- `Build-Pixelpipe.bat` now delegates to `scripts\build-release.ps1` instead of duplicating the csc.exe invocation; build flags can no longer drift between the two.

Fixed:

- `NormalizeProvider("onedrive", ...)` returned `"drive"` because the substring check for `"drive"` ran before `"onedrive"`. OneDrive profiles would have been saved with `Provider="drive"` and displayed in the tray as `Google Drive`. The onedrive check now runs first.

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
