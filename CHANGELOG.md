# Changelog

## 0.5.3

Fixed:

- Pixelpipe could "randomly" close. Two known causes:
  - The Maintenance group in the main window had an "Exit Pixelpipe" button right next to "Run setup wizard / Open log folder / Copy diagnostics". A misclick would fully quit the tray app, leaving the user wondering where the icon went. Removed; the only exit path is now the tray menu's Exit, where it sits next to the "stop all mounts first" prompts.
  - An unhandled exception on a worker thread would terminate the process without a log entry (default .NET WinForms behavior). `Program.Main` now registers `Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException` handlers that write the exception type, message, and stack trace to `pixelpipe-ui.log` before the runtime tears anything down, and `SetUnhandledExceptionMode(CatchException)` keeps UI-thread exceptions from killing the app entirely.
- Constructor's background work items (first-launch setup nudge, /automount delay) are now wrapped in try/catch as well, so a transient error there can't take the tray icon down.

Added:

- One-time welcome balloon: "Pixelpipe is in your system tray. Right-click the icon for the menu, or use Exit there to fully quit." Shows once per install on a non-/automount launch so users don't think closing the main window quit the app.

## 0.5.2

The main window now has every action the tray menu has, and the profile cards no longer clip.

Fixed:

- Profile-card status pill clipped its caption to "unmounte". Cause: the pill was inside a `TableLayoutPanel` cell whose `AutoSize` width was being squeezed by the percent-column sibling. Replaced with a plain `Panel` and `Dock.Right` so the pill claims exactly the width its text needs before the title gets the rest.
- Profile card cropped its bottom action buttons (only "Mount" and "Mount (full cache)" were visible). Cause: the buttons row wrapped to a second flow line that the parent didn't grow to include. Card now uses a vertical `FlowLayoutPanel` with `AutoSize` so it always grows to fit primary + secondary action rows.

Added (main window now has feature parity with the tray):

- **Status strip across the top of the Profiles tab**: live "Status: N/M mounted", "rclone: found/missing", "WinFsp: found/missing", transfer-quota line, and an admin warning chip when running elevated.
- **Add cloud remote ▾ dropdown**: same provider list as the tray submenu — Pixeldrain, Google Drive, MEGA, OneDrive, Dropbox, Box, S3/R2/B2/Wasabi, WebDAV/Nextcloud, SFTP, plus Custom existing rclone remote and Open rclone config terminal.
- **Import existing rclone remotes** button on the Profiles top bar.
- **Per-profile secondary action row**: Edit, Set primary, Auto-mount toggle (label updates to "Auto-mount: on/off"), Remove. Edit/Remove disable while the profile is mounted.
- **Settings tab reorganized into groups**:
  - *Dependencies*: rclone status + Download portable / Install via winget, WinFsp status + Install via winget, Open rclone config terminal.
  - *Pixeldrain quota*: primary remote configured/not, Configure Pixeldrain remote, API key Set/Clear, Open pixeldrain.com API keys page.
  - *Preferences*: bandwidth limit + Custom..., Auto-mount at Windows startup, Verbose logging.
  - *Maintenance*: Run setup wizard, Open log folder, Open settings file, Copy diagnostics, Check for updates, Exit Pixelpipe.
- Logs tab gains an Open log folder button next to Refresh.
- Diagnostics tab gains a "Clear stale primary drive" button matching the old Diagnostics window's action.

## 0.5.1

Fixed:

- Main window, quick-controls popup, and setup wizard were unusable — labels overlapped each other, buttons clipped their captions ("Mount all" / "Unmount all" / "Add cloud remote..." / "Refresh now" all truncated), profile cards collapsed onto themselves. Cause: hardcoded `Left`/`Top`/`Width` pixel coordinates that don't match how WinForms actually renders Segoe UI at the user's font and DPI scale.
- Rewrote all three windows with proper layout containers (`TableLayoutPanel`, `FlowLayoutPanel`, `AutoSize` labels and buttons, `MaximumSize` for word-wrap, `AutoScaleMode.Dpi`). Cards grow with their content; buttons grow with their captions; text never overlaps. Window contents now scale cleanly across font sizes and DPI scaling.

## 0.5.0

GUI windows. Pixelpipe is no longer tray-only; the tray menu and the new windows are full peers and you can do everything from either.

Added:

- **Main window** (`Open Pixelpipe window...` in the tray) with four tabs:
  - *Profiles*: one card per profile with a live status pill, drive letter, status line, storage gauge (parsed from the `(N%)` field), session traffic, current speed, and big Mount / Mount-full / Unmount / Open buttons. Plus top-bar Mount-all / Unmount-all / Manage remotes / Refresh now.
  - *Diagnostics*: the existing diagnostics text + buttons (Copy / Refresh / Open log folder / Open settings file / rclone config). Auto-refreshes while you're on the tab.
  - *Logs*: dropdown to pick `pixelpipe-ui.log` or any profile's rclone log, tail viewer.
  - *Settings*: bandwidth combobox + custom-bandwidth button, auto-mount-at-startup toggle, verbose-logging toggle, re-run-setup-wizard button, check-for-updates button.
- **Setup wizard** replaces the old MessageBox chain. Four steps (rclone → WinFsp → rclone remote → optional PixelDrain API key) with Skip/Back/Next/Cancel and a live "Current state" panel showing each dependency. Runs on first launch automatically (unless the user has previously checked "don't show again"); re-runnable from `Setup / dependencies → Run first-time setup wizard` or the Settings tab.
- **Quick controls popup** (`Quick controls...` in the tray): compact always-on-top window showing aggregate speed (large), aggregate session traffic, a bandwidth dropdown, and a one-line live entry per profile. Sized for a screen corner during active transfers.
- Tray menu: two new shortcuts at the top — `Open Pixelpipe window...` and `Quick controls...`.

Changed:

- Refresh worker now also pushes live state to the main window and quick controls in addition to the tray menu. All three update in place (no rebuilds while open).
- `Pixelpipe.Helpers.cs` gained shared `ParseBytesPerSec`, `ParseBytes`, and `ParseStoragePercent` helpers used by the main window and quick controls.
- Three new tests for the parse helpers (21 tests total, was 18).

## 0.4.3

Fixed:

- `Pixelpipe-Setup.exe` (Inno Setup installer) was silently missing from every release since the workflow started building it. Cause: `scripts/build-installer.ps1` looked for `ISCC.exe` under `"$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe"`, but in PowerShell `$env:ProgramFiles(x86)` parses as `$env:ProgramFiles` followed by a literal `(x86)`, not the `Program Files (x86)` env var. The script always missed ISCC, the `-Optional` flag silently swallowed the failure, and the workflow's `continue-on-error: true` hid it in CI. Fixed by using the explicit `${env:ProgramFiles(x86)}` syntax, also checking `${env:ProgramData}\chocolatey\bin\ISCC.exe`, and falling back to `Get-Command ISCC.exe`. The workflow's Build-installer step is no longer `continue-on-error`, so a real installer-build failure surfaces in CI; the choco-install step keeps `continue-on-error` since that's the network-flake step.
- The script now also fails if ISCC ran but did not produce `Pixelpipe-Setup.exe` at the expected path.

Changed:

- `README.md` Quick Start now surfaces both download options side by side: the portable `Pixelpipe.exe` / `-Windows-x64.zip` bundle, and `Pixelpipe-Setup.exe` for users who want Start-Menu / Add-Remove-Programs / per-user install under `%LOCALAPPDATA%\Programs\Pixelpipe\`. Both share the same `%APPDATA%\Pixelpipe\settings.json` so users can switch between them without losing config.

## 0.4.2

Changed:

- Tray menu now updates in place while it's open. Status lines (rclone / WinFsp / quota, per-profile status / storage / traffic / speed / last error), bandwidth checkmarks, startup checkmark, and the Mount / Unmount / Mount-all / Unmount-all enabled state all refresh live as the background data fetch completes; no more flash, no more "have to close and reopen to see fresh values".
- The previous v0.4.1 fix deferred a full `RebuildMenu` while the menu was visible. We now go further: `RebuildMenu` builds the menu once and holds references to every dynamic item; `UpdateMenuLiveState` edits `.Text` / `.Enabled` / `.Checked` / `.Visible` on those references each refresh tick. Structural rebuilds (profile add/remove/reorder, startup-state changes) still happen on the next close-and-reopen.

## 0.4.1

Fixed:

- Tray context menu would flash closed and reopen every ~7 seconds while it was visible. Cause: the periodic refresh timer called `RebuildMenu()` (which clears and re-adds `menu.Items`) regardless of whether the menu was on screen. Pixelpipe now defers the rebuild until the menu is closed; the Opening handler still rebuilds before display so the next open shows fresh data.

## 0.4.0

Full audit pass: thread safety, resource leaks, UX papercuts, and release-pipeline cleanup.

Added:

- `Mount all` and `Unmount all` actions when more than one profile exists.
- `Tools / diagnostics` window now auto-refreshes every 5 seconds and has a `Verbose logging` checkbox that toggles `[debug]` lines (menu placement, refresh) in `pixelpipe-ui.log`.
- Custom-bandwidth dialog validates its input against `^(off|\d+(\.\d+)?[KMG]?)$` instead of passing arbitrary text to rclone.
- `/automount` now writes a balloon ("Pixelpipe auto-mounted N profile(s)") and a UI-log entry, so silent startup mounts are no longer invisible.
- New unit tests for `Program.HasArg`, `TrayContext.ProfilePortFor`, `TrayContext.IsValidBandwidth`, `TrayContext.ScrubSecrets`, and the `box` vs `dropbox` provider distinction. Test count: 18 (was 13).
- `docs/CONFIGURATION.md` rewritten to match the actual settings schema, including the per-profile array, DPAPI key field, `SkipMissingDepWizard`, `VerboseLogging`, and the `/automount` and `/smoketest-menu` flags. `README.md` now documents the flags too.

Changed:

- `UnmountProfile` no longer blocks the UI thread on its 2-second clean-unmount wait. The unmount sequence now runs on a ThreadPool worker and posts the result back to the UI when done; the menu stays responsive during unmount.
- `refreshing` and `dependencyRefreshing` are now `Interlocked.CompareExchange`-guarded ints. The previous `if (flag) return; flag = true;` pattern could race when the timer and a menu-open both kicked off work simultaneously.
- Worker threads now iterate a snapshot of the profile list under a lock instead of touching the live `List<RemoteProfile>`. The previous code could throw `InvalidOperationException: Collection was modified` when the UI added or removed a profile mid-refresh.
- `rclonePath` is now `volatile` and `RemoteConfigured` reads from a 30-second cache of `rclone listremotes`, so a momentary rclone timeout no longer reports configured remotes as missing.
- `MountAutoProfiles` no longer falls back to silently mounting `profiles[0]` when no profile is tagged `AutoMount=true`. If no profile is tagged, Pixelpipe shows a balloon explaining that and does nothing.
- Force-unmount now calls `taskkill /F /T /PID <rclone pid>` first so any WinFsp child process the rclone parent spawned also dies; the in-process `Kill()` is kept as a last-ditch fallback.
- Tray tooltip shows `Pixelpipe (N/M mounted)` or `Pixelpipe (none mounted)` instead of a single boolean.
- "Running as Administrator" warning shows once per process instead of on every mount.
- First-launch wizard intro is now one line; declining writes `SkipMissingDepWizard=1` so it doesn't reopen every launch while a dependency is missing.
- `Tools / diagnostics → Open settings file` now uses ShellExecute so the user's default JSON handler opens, instead of hard-coding `notepad.exe`.
- `FindRclonePath` no longer hard-codes a specific rclone version. It now globs `C:\Program Files\rclone-v*-windows-*\rclone.exe` and prefers the highest version.
- Default drive letter for the Box guided remote is now `K:` instead of `B:`.
- Synthesized fallback tray icon no longer leaks its HICON. `Diagnostics / repair` and `Manage remotes` windows are now disposed when closed.
- `MessageBox.Show` calls that previously echoed raw rclone output now point users at `pixelpipe-ui.log` and write the raw output there; if it's ever shown in a dialog, `ScrubSecrets()` masks `api_key=`/`token=`/`password=` style assignments and long alphanumeric runs.
- UI log gains `[info]`, `[warn]`, `[error]`, `[debug]` level tags. `[debug]` is gated on the `VerboseLogging` setting.

Fixed:

- `Pixelpipe.Helpers.cs`: removed the instance wrapper methods (`FormatBytes`, `NormalizeDriveLetter`, etc.) that just delegated to their `*Value` static twins. The statics are now the only versions and are named without the `Value` suffix; callers and tests use them directly.
- `Pixelpipe.Helpers.cs`: removed the duplicate `HasArg` wrapper on `TrayContext`; constructors and tests call `Program.HasArg` directly.
- `FirstFreePreferredDrive` no longer probes the caller's preferred drive twice in a row.

CI / release:

- Workflow has a `concurrency` group so two quick pushes don't race on the `rolling` tag.
- `actions/checkout` runs with `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` to silence the Node 20 deprecation annotation.
- Artifact retention reduced to 14 days (we have permanent releases for anything worth keeping).
- Workflow now sets `installer/Pixelpipe.iss`'s `MyAppVersion` from `CHANGELOG.md` before invoking ISCC, so Add/Remove Programs no longer shows the stale `0.1.0` from the original commit.
- `scripts/build-release.ps1` and `scripts/run-tests.ps1` kill any leftover `Pixelpipe.exe` / `Pixelpipe.Tests.exe` before invoking csc so a wedged previous run doesn't trip "file in use".

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
