# Changelog

## 0.13.1

Audit follow-up batch two — responsiveness + remaining security hygiene + x64. Six findings closed.

Fixed:

- **PERF-1 (dependency probes on UI thread).** `RcloneAvailable()` and `WinFspInstalled()` used to run their full probes (`File.Exists`, registry lookups, and on a cold path a 3 s blocking `rclone version` subprocess) every single time they were called — and they were called from `ApplyLiveState` / `UpdateMenuLiveState` on every ~7 s refresh tick. Likely root cause of the periodic micro-freezes the changelog has chased. Now both return cached booleans with a 30 s TTL; the existing `RefreshDependencyStatusAsync` worker is the one place that does the real probes (`ProbeRcloneAvailableSync` / `ProbeWinFspInstalledSync`) and publishes the cached values via `BeginUi`. UI callers never touch disk.
- **PERF-2 (provider wizards on UI thread).** `CreateRemoteAndProfile` did the rclone.conf write + listremotes round-trip synchronously from the dialog OK handler. Moved to a worker thread; profile creation and the success/failure dialog marshal back via `BeginUi`. Mount async path from v0.12.1 is now matched by the wizard path.
- **PERF-4 (regex churn on hot paths).** Hoisted `ExtractLong`, `ExtractDouble`, `ParseBytes`, `ParseBytesPerSec`, `ParseStoragePercent`, `ScrubSecrets`, and the Activity-tab log-line regex to `static readonly Regex` with `RegexOptions.Compiled`. Each refresh tick was rebuilding these on every profile.
- **SEC-2 (RC port unauthenticated).** Pixelpipe used `--rc-no-auth` on every mount. Now generates a 24-byte URL-safe base64 token at startup (random via `RandomNumberGenerator`) and passes `--rc-user pixelpipe --rc-pass <token>` to the mount launch and every subsequent rc client call (stats, mount/unmount, core/quit, core/bwlimit). Token is in memory only.
- **SEC-4 (`TerminateOtherInstances` killed by image name).** Now compares each candidate's `MainModule.FileName` against our own before killing. An unrelated `Pixelpipe.exe` (a dev build elsewhere, a sample named the same) is left alone.
- **ARCH-4 (`anycpu` build for an x64-only app).** Build now pins `/platform:x64`. App already assumed 64-bit (WinFsp probe checks `winfsp-x64.dll`, rclone download is `windows-amd64`). Removes 32-bit edge cases including potential `long`-tearing on a 32-bit host (groundwork for ARCH-3 in v0.13.2).

Added:

- **One new unit test** (`GenerateRcAuthToken` — length, uniqueness, URL-safe charset). 52 tests total.

## 0.13.0

External code audit follow-up — correctness, security, and supply-chain. Five findings closed.

Fixed:

- **BUG-1 (success judged by text-scan).** `RunRcloneCapture` / `RunProcessCapture` used to return only concatenated stdout+stderr text; `Process.ExitCode` was never read, and `LooksLikeRcloneError` declared empty output to be "success". `rclone moveto` and `copyto` print nothing on success, so a watch-folder upload whose rclone got killed (timeout, OS, anything) was silently marked uploaded and — in `move` mode — the local file was deleted without ever actually uploading. Capture helpers now return a `ProcessResult { ExitCode, StdOut, StdErr, TimedOut, LaunchError }`. Watch-folder upload treats `ExitCode != 0 || TimedOut` as failure and routes the entry through the existing retry / drop machinery.
- **BUG-2 (pipe-buffer dead-stall).** The same capture helpers called `ReadToEnd()` after `WaitForExit`. A child that wrote more than the ~64 KB pipe buffer to either stream would block on the write, never exit, and trigger a spurious timeout with empty output (feeding straight into BUG-1). Drain stdout and stderr asynchronously via `BeginOutputReadLine` / `BeginErrorReadLine` before waiting.
- **SEC-1 (secrets on argv).** Provider wizards used to run `rclone config create NAME TYPE access_key_id AKIA... secret_access_key VALUE ... --non-interactive`, putting the secret in the process command line for the few seconds rclone ran — any other user-level process could read it via `Win32_Process.CommandLine` (Pixelpipe itself does this for its orphan scan). Now Pixelpipe writes the section directly to rclone.conf and obscures each secret field by piping the plaintext to `rclone obscure -` over **stdin**. Same on-disk result; nothing sensitive on argv.
- **SEC-3 (unverified rclone download).** The portable installer hit `rclone-current-windows-amd64.zip` and ran the result with no integrity check — a compromised mirror or TLS-intercepting proxy could deliver a tampered binary. Pinned to `v1.71.1` (bumped together with future upgrades), download the matching `SHA256SUMS` file, parse the expected hash for our zip, compute SHA-256 of the downloaded zip, and refuse to extract on mismatch.
- **BUG-4 (Run-key null deref).** `StartupEnabled` used `Registry.CurrentUser.OpenSubKey(...)` then `.GetValue` without a null check. On a profile without the Run key the NRE was swallowed and reported as "startup disabled" — technically correct but masking the real cause. Null-check the key and log inside the catch.

Added:

- **Four new unit tests** for the new pure helpers: `IsSecretField`, `MergeRcloneConfigSection`, `ParseSha256ForFile`, `ProcessResultSucceeded`. 51 tests total.

## 0.12.1

Direct fixes for the freeze / linger symptoms — no more whack-a-mole. Five proactive changes that close known classes of UI-thread block:

Fixed:

- **`MountProfile` no longer blocks the UI thread.** The slow validation checks (`RcloneAvailable`, `RemoteConfigured`, `DriveLetterInUse`) used to run synchronously on the menu-click handler, freezing the UI for several seconds per mount (and N × that for "Mount all"). They now run on a worker thread; dialogs and the actual `rclone mount` spawn happen on the UI thread when the worker reports back via `BeginUi`.
- **Orphan-rclone scan menu action** also moves to a worker thread. The WMI `Win32_Process` lookup can take a few seconds on a stressed system; previously that froze the menu click.
- **Hung previous-instance recovery** via a named pipe (`Pixelpipe.TrayApp.WakePipe`). On launch, if the single-instance mutex is held, the new process sends a `WAKE` over the pipe with a 2 s timeout. Healthy holder acks and the new process exits silently (with the holder showing its main window). Hung holder doesn't respond — the new process terminates every other `Pixelpipe.exe`, re-acquires the mutex, and proceeds. Replaces the old "Pixelpipe is already running" dead-end when the holder was wedged.
- **UI thread heartbeat.** A `[info] heartbeat` line every 30 s, surfaced through the new Activity tab so a freeze leaves a visible "heartbeats stopped at 14:32" gap. Free observability if a future freeze happens.
- **Refresh deadman.** If `refreshingFlag` stays set for more than 90 s the heartbeat tick force-resets it, so a hung worker can't permanently swallow subsequent refresh requests for the rest of the session.

Added:

- **Tools / diagnostics → "Test UI responsiveness"** — measures round-trip through `BeginUi` and shows a "responded in N ms" / "did NOT respond within 5 s" dialog. Useful when the user suspects a freeze and wants to confirm.

## 0.12.0

Bandwidth schedule, Activity tab, and timestamped settings backups. Three quality-of-life features extending systems we already have.

Added:

- **Per-profile bandwidth schedule.** Each profile gains `BandwidthScheduleEntries` — a comma-separated list of `HH:mm=limit` transitions (e.g. `"00:00=off,09:00=1M,18:00=off"`) applied by the same 30 s schedule timer that fires mount/unmount. Each transition overrides `BandwidthLimit` until the next one fires. Empty disables the schedule. Editable in the Edit-profile dialog under the bandwidth group; invalid tokens are dropped at save time so a typo can't silently misbehave.
- **Activity tab in the main window** (between Profiles and Diagnostics). Parses `pixelpipe-ui.log` and renders the last 300 events as a readable timeline: `2026-05-28 14:30:15  Mount  Pixeldrain mounted on P:`. Category dropdown filters to Mount / Unmount / Schedule / Transfer / Watch / Orphan / Backup / Update / Startup / Warning / Error / Other. Auto-refreshes whenever the tab is visible.
- **Timestamped settings backups before destructive operations.** Removing a profile, or importing a profile, now writes `%APPDATA%\Pixelpipe\backups\settings-YYYYMMDD-HHMMSS-<reason>.json` before mutating `settings.json`. Last 20 backups retained; older ones pruned. Tools / diagnostics → "Open settings backups folder" opens the directory. Existing atomic-write `.bak` (overwritten every save) is unchanged.
- **Four new unit tests** (`ParseBandwidthSchedule`, `ClassifyActivity`, `FormatActivityEvents`, `ParseActivityLog`). 47 tests total, all green.

## 0.11.4

Orphan-rclone prevention and recovery. User report: a previous Pixelpipe (or rclone) session left an `rclone.exe` process alive holding a drive letter, so the next launch couldn't mount and showed "drive in use" forever.

Added:

- **Win32 Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.** Every rclone process Pixelpipe spawns is now assigned to this job. When Pixelpipe exits for ANY reason — clean Exit, crash, Task Manager kill, sign-out, OOM, debugger detach — Windows closes the job handle and forcibly terminates every rclone in it. This is the bulletproof Windows-native way to prevent orphans from this point forward.
- **Startup orphan scan** (`StartupOrphanCheck`). On launch Pixelpipe lists running `rclone.exe` processes via WMI, matches their command lines against the drive letters of our profiles, and prompts to kill any matches: *"Pixelpipe found N orphan rclone processes from a previous session. Kill them?"*. Covers users upgrading from a pre-v0.11.4 install whose orphans aren't in any job.
- **Tools / diagnostics → "Find / kill orphan rclone processes"** for manual triggering at any time.
- **"Drive in use" dialog is now three buttons**: *Yes* (find and kill the orphan rclone for this drive, then mount), *No* (try to mount anyway — usually fails immediately, kept for advanced users), *Cancel*. After a kill, re-checks the drive letter and bails with a clear message if something else (Explorer window, third-party app) is still holding it.
- `System.Management.dll` added to the build/test reference set for WMI command-line lookup.
- New unit test `CommandLineMentionsDrive` (43 tests total).

## 0.11.3

Settings tab layout fix.

Fixed:

- **Settings tab groups collapsed to ~30 px wide.** `MakeGroup` (GroupBox) used `AutoSizeMode = GrowAndShrink` and its inner `MakeKeyValueGrid` used `Dock = DockStyle.Top` with `AutoSize + GrowAndShrink` and two AutoSize columns. WinForms could resolve the circular "GroupBox sizes to child / child fills parent" dependency to "as narrow as possible", which clipped every Settings row to its first character.
- `MakeGroup` now uses `GrowOnly` and a `MinimumSize = (960, 0)` floor.
- `MakeKeyValueGrid` switches the value column to `Percent 100` so the editor / button row stretches to the GroupBox width.

## 0.11.2

Second hotfix for the v0.11.0 menu issue. v0.11.1 added a defensive fallback for cases where `RebuildMenu` threw, but the underlying complaint turned out to be different: on Windows 11 the default NotifyIcon → ContextMenuStrip auto-show path can stop responding to right-click entirely (most reliably when the icon lives in the "Show hidden icons" overflow). Double-click still worked; right-click silently did nothing.

Fixed:

- **Wire `NotifyIcon.MouseUp` ourselves.** When the right button comes up and the menu isn't already showing, Pixelpipe now calls `menu.Show(Cursor.Position)` explicitly. The existing `ContextMenuStrip` assignment stays so systems where the default path works don't double-show (the `menu.Visible` check skips the manual call in that case).

## 0.11.1

Hotfix for a v0.11.0 startup regression where a single exception in the constructor could leave the tray icon visible with a completely empty context menu, which read as "the app is dead" even when only the menu-build path had failed.

Changed:

- **`OnMenuOpening` wraps `RebuildMenu` in try/catch** and falls back to a minimum-viable menu (`BuildEmergencyMenu`) showing the error, plus actions to open the log folder, open the main window, view the settings file, retry the rebuild, and exit. The user can always read what went wrong and quit cleanly.
- **Each constructor step from the post-tray-creation point onward** (`timer.Start`, `StartScheduleTimer`, `StartWatchFolders`, `RebuildMenu`) is wrapped so a failure in any one path can't prevent the next one. If `RebuildMenu` itself fails at startup, `BuildEmergencyMenu` runs so the right-click menu is never empty.

## 0.11.0

Per-profile watch folder: drop a file into a local directory, Pixelpipe uploads it to the remote and (optionally) deletes the local copy.

Added:

- **Watch folder per profile.** Each profile gains `WatchFolderEnabled`, `WatchFolderPath`, `WatchFolderTargetDir`, `WatchFolderMode` (`move` / `copy`), and `WatchFolderQuietMs`. A `FileSystemWatcher` per enabled profile, plus a single 3-second drain timer, queues newly seen files and uploads each via `rclone moveto` (default — deletes the local copy after success) or `rclone copyto` (keeps it). Up to two parallel uploads per profile. Failed uploads retry with 30 s / 2 m / 10 m back-off, then drop after three attempts (the failure is recorded on the profile).
- **Edit-profile dialog** gets a new "Watch folder (auto-upload)" group with a folder picker, target-subdir field, move/copy dropdown, and quiet-period input. Enabling a watch folder with a missing or non-existent path raises a warning at save time.
- **Live watch counters** on the profile card, the tray menu profile submenu, and Diagnostics: `"Watch (move): N queued, M uploading, K uploaded"` plus a one-line "last:" message. Hidden when the profile has watch disabled.
- **Export/import** round-trips the new fields, so a profile with a watch folder configured on machine A imports with `WatchFolderEnabled=true` but uses whatever `WatchFolderPath` was set (the path lives on disk on each machine).
- **Three new unit tests** (`NormalizeWatchMode`, `BuildWatchUploadArgs`, `ComputeWatchNextRetryUtc`). 42 tests total, all green.

Changed:

- **`SaveProfiles`** now calls `ReconcileAllWatchers` after writing so adding, editing, or removing a profile picks up immediately rather than waiting for the next process restart.

## 0.10.0

In-app provider setup wizards: nine new "Add cloud remote" entries that build the rclone remote and the Pixelpipe profile in one flow, without ever opening `rclone config` in a terminal (except OAuth, where the browser dance still happens there).

Added:

- **`ShowProviderForm` dialog**: a generic labeled, validated, theme-consistent form for collecting provider credentials. Supports text, password (`UseSystemPasswordChar`), and dropdown fields, each with optional inline help text. Required fields are starred and the OK button refuses an empty submit. Used by every new wizard below.
- **`ConfigureS3RemoteWizard`** for AWS S3, Wasabi, Cloudflare R2, Backblaze (S3-API), DigitalOcean Spaces, Linode, Storj. Provider chooser tunes endpoint/signing; access key + secret + region + optional endpoint. Runs `rclone config create … s3 …`, verifies, and auto-creates a profile on success.
- **`ConfigureWebDAVRemoteWizard`** for Nextcloud, ownCloud, SharePoint, Infinite Scale, generic. URL + vendor + user + (DPAPI-handled-by-rclone) password.
- **`ConfigureSFTPRemoteWizard`** with host + port (default 22) + user + optional password (empty falls back to ssh-agent / default key).
- **`ConfigureFTPRemoteWizard`** with host + port + user + password + optional `explicit_tls` for FTPS servers.
- **`ConfigureMegaRemoteWizard`** with MEGA email + password.
- **`ConfigureOAuthRemoteWizard`** for Drive / OneDrive / Dropbox / Box: takes a remote name in-app, opens the `rclone config` terminal so the user completes the browser sign-in, then verifies the remote shows up in `rclone listremotes` and auto-creates the profile.
- **`BuildRcloneConfigCreateArgs` pure helper** shared by every wizard for argument quoting and field ordering. Covered by a new unit test that exercises empty/single/multi-field cases plus values with whitespace and embedded quotes.
- One new unit test (`BuildRcloneConfigCreateArgs`). 39 tests total, all green.

Changed:

- **Tray menu "Add cloud remote"** and the main-window split button now route each entry to its dedicated wizard. The old `AddGuidedRcloneRemote` "type the name, we'll open rclone config" path is gone for the nine first-class providers; "Custom existing rclone remote..." and "Open rclone config terminal" remain for advanced use.

## 0.9.0

Portability and visibility release: each profile now reports stats appropriate to its provider, you can move profiles between machines as JSON, and the Logs tab can filter to a substring.

Added:

- **`ProviderCapabilities` table.** Each backend declares what kinds of metrics it can actually report: storage quota (used/total/free), transfer quota, and file count. Profile cards, the tray submenu, and Diagnostics now show "Storage: not applicable for S3-compatible buckets" or "Transfer quota: not applicable for Google Drive" instead of "0" or "unavailable" when the provider genuinely doesn't surface that number. Where rclone *does* return numbers (Drive, OneDrive, Dropbox, Box, sometimes WebDAV/SFTP), those still display normally.
- **Per-profile transfer quota.** `RemoteProfile.TransferQuotaText` is populated per-profile based on the provider capability. For Pixeldrain profiles it carries the live API quota; for providers without a transfer quota concept it carries the "not applicable" note. The tray profile submenu, Diagnostics, and the main-window cards all show it. The global tray quota line still shows the Pixeldrain quota (since the API key is global).
- **Per-profile object count.** When `rclone about` returns `objects`, profile cards and the tray submenu show "Objects: 12,453". Hidden for providers that don't report it.
- **Profile import/export.** Tools / diagnostics → "Export profiles to file…" writes a JSON file (one default name per day) containing every profile's settings. "Import profiles from file…" opens a checklist of new profiles in the file; profiles whose `Id` already exists are skipped, drive-letter and label collisions auto-resolve against the live state. **Encrypted secrets (DPAPI-protected API keys, rclone passwords) are NOT exported** because DPAPI is per-Windows-user; importers re-enter them on the new machine. The Profiles tab also gets Export / Import buttons.
- **Logs tab substring filter.** A "Filter:" textbox in the Logs tab keeps only lines containing the typed substring (case-insensitive) from whichever log is currently selected. Useful for narrowing the UI log to one profile by typing its label, or pulling all `[FAIL]` lines out of a long rclone log.
- **Six new unit tests:** `ComputeStoragePercent`, `FilterLogText`, `ProviderCapabilitiesDefaults`, `BuildProfilesExportJson`, `TryParseProfilesExportJson`, `PlanProfileImport`. 38 tests total, all green.

Changed:

- **`ApplyAboutToProfile`** replaces the inline `about` parsing in `RefreshProfile`. It consults the provider capabilities first, skips `rclone about` entirely for providers that can't report storage or files, and stores `StorageUsedBytes / StorageTotalBytes / StorageFreeBytes / ObjectCount` on the profile in addition to the human text. Profile cards now compute the storage progress-bar percentage from those raw bytes (via `ComputeStoragePercent`) instead of regex-parsing the display string.
- **`RemoteProfile`** constructor seeds `StorageText` and `TransferQuotaText` from the provider's capability defaults so a fresh profile already says something sensible before the first refresh.

## 0.8.0

Three real features: per-profile bandwidth overrides, scheduled mount/unmount per profile, and transfer-complete notifications.

Added:

- **Per-profile bandwidth limits.** Each profile gains a `BandwidthLimit` field. Empty (the default) means *inherit the global setting*; any valid value (`off`, `512K`, `1M`, `1.5G`, …) overrides it for just that mount. Editable from the per-profile Edit dialog via a dropdown that shows the global value in the "(inherit global: …)" slot. Live changes use rclone RC just like the global limit, but the global setter now only touches mounted profiles that don't have their own override.
- **Scheduled mount / unmount per profile.** Each profile gains `ScheduleEnabled`, `ScheduleMountTime` (`HH:mm`, local), `ScheduleUnmountTime` (optional), and `ScheduleDays` (default all seven). A new 30-second timer (`Pixelpipe.Schedule.cs`) checks the schedule and triggers `MountProfile` / `UnmountProfile` at the right time, throttled to once per day-key so it never re-fires within the same minute. Mount/unmount triggered by the schedule shows a balloon and logs to `pixelpipe-ui.log`. Setup via the Edit dialog's new "Scheduled mount / unmount" group: HH:mm fields for mount and unmount, plus day-of-week checkboxes.
- **Transfer-complete notifications.** When rclone's live stats show an active transfer (`transferring > 0`), Pixelpipe latches the starting byte count. When the transfer batch returns to zero, it fires a balloon `<profile>: transfer finished — N MB moved`, but only if the delta is ≥ 10 MB (so VFS background syncs and trivial dir listings don't spam). Toggle in Settings → Preferences → "Notify when a transfer batch finishes". Setting key: `TransferNotificationsEnabled` (default `1`).
- **Five new unit tests**: `IsNewer` (from v0.7.0), `ScheduleAllowsDay`, `TryNormalizeScheduleTime`, `ScheduleTimeMatches`. 32 tests total, all green.

Changed:

- `SetBandwidth` (the global setter) now only pushes the new rate to mounted profiles whose `BandwidthLimit` is empty. Profiles with a per-profile override keep their value. The applied-vs-saved balloon text spells this out.
- `MountProfile` resolves the effective limit through a new helper `EffectiveBandwidthFor(profile)` so the launch path and live RC push always agree.
- `RemoteProfile` JSON gains `BandwidthLimit`, `ScheduleEnabled`, `ScheduleMountTime`, `ScheduleUnmountTime`, `ScheduleDays`. All optional; older settings files load unchanged with sensible defaults.

## 0.7.0

Auto-update notification and repo hygiene.

Added:

- **Auto-update notification.** On tray-menu open Pixelpipe checks `api.github.com/repos/NathanNeurotic/Pixelpipe/releases/latest` at most once per 24 h. If the latest tag is newer than the running build, a balloon fires once ("Pixelpipe vX.Y.Z is available. Open the tray menu to download.") and a "Pixelpipe vX.Y.Z available — download" item appears at the top of the tray menu. Clicking it opens the releases page and clears the indicator. No silent self-update.
- **`UpdateCheckEnabled` setting** (default `1`) with a toggle in Settings → Preferences → "Check GitHub for new releases". Set to `0` to disable the periodic check entirely; the manual "Check for updates" menu item still works.
- **`LastUpdateCheckUtc` and `AvailableUpdateVersion` settings** persist the check state so the indicator survives an app restart without re-fetching.
- **`Application.ProductVersion` now matches the released version.** `scripts/generate-version.ps1` reads the top `## X.Y.Z` from `CHANGELOG.md` and writes a fresh `src/AssemblyVersion.cs` before every csc invocation. The generated file is gitignored. Both `build-release.ps1` and `run-tests.ps1` regenerate it so test runs match what's released.
- **`.github/dependabot.yml`** configures weekly bumps for GitHub Actions versions so `actions/checkout` / `actions/upload-artifact` updates come in as PRs instead of needing a manual bump every few months.
- **`CONTRIBUTING.md`** describing the workflow (branch off main → write feature + tests → bump CHANGELOG → CI auto-cuts release), build commands, project layout, and the conventions that have shaken out across the v0.5 / v0.6 refactors (TableLayoutPanel layouts, snapshot pattern for worker threads, WindowTheme over hardcoded colors, try/catch on every ThreadPool delegate).
- **New unit test** `IsNewer`: covers plain semver bumps, the `v` prefix, padding (`0.7.0` vs `0.7.0.0`), per-component comparison (`0.6.10 > 0.6.9`), and unparseable inputs falling back to `false`. 29 tests total, all green.

## 0.6.1

A "tighten the screws" release after the v0.6.0 audit. No new features; small refinements across thread safety, UX, and CI.

Added:

- **Quick controls window has a "Pin on top" checkbox.** Default ON (the popup is meant as a heads-up overlay during transfers), but you can untick it to send the window behind other applications. Persisted as `QuickControlPinned` in `settings.json`.
- **Preflight reports are now visible in the Diagnostics tab.** When you run `Test profile`, the full `[OK]/[WARN]/[FAIL]` report plus the timestamp it ran are stored on the profile and rendered in `BuildDiagnosticsText`, indented under the per-profile block. The report also still goes to `pixelpipe-ui.log`.
- **New unit tests** `PreflightShortSummary` and `IndentLines`. 28 tests total, all green.

Changed:

- **Centralized window palette** into a single `WindowTheme` static class. `MainWindow`, `ProfileCard`, `QuickControlWindow`, `SetupWizard`, `MakeDialog`, `PromptForValue`, `ChooseFromList`, `PromptForApiKey`, `EditProfile`, the diagnostics/logs boxes, the bandwidth combo — all now reference the same constants. Setup wizard had a slightly different muted/button palette that was visually inconsistent next to the main window; it now matches.
- **`UpdateMenuLiveState` snapshots the profile list once** at the top instead of re-reading `profiles.Count` from multiple call sites. Tightens consistency with the snapshot pattern used everywhere else in worker paths.
- **`GetPrimaryProfile` now holds the lock through the whole operation** instead of releasing it between the existence check and the `profiles[0]` return.
- **`ProfileLabelExists` and `HasProfileForRemote` snapshot the profile list** before iterating. UI-thread-only callers in practice, but consistent with the rest of the codebase.
- **`MainWindow` constructor no longer double-builds** profile cards. `RebuildProfileCards` already ends with `ApplyLiveState`, so the constructor's explicit `ApplyLiveState()` call was redundant.
- **CI bumped to `actions/checkout@v6` and `actions/upload-artifact@v7`** (latest majors at release time). The `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` shim is kept as belt-and-suspenders.

Fixed:

- **Misleading comment** above `InsertWindowShortcuts` claimed the call was wired indirectly from `RebuildMenu`. It's wired directly; comment now describes what the method does and where it's called from.

## 0.6.0

A reliability and consolidation release. Single-instance protection, atomic settings writes, profile preflight, hardened argument quoting, and layout-managed dialogs. Per-profile `Test profile` now wired into both the tray and the main window.

Added:

- **Single-instance protection.** A named mutex (`Local\Pixelpipe.TrayApp`) prevents a second tray icon. Launching the EXE again while Pixelpipe is running pops "Pixelpipe is already running in the system tray." (silent when called with `/automount`).
- **Atomic settings writes.** `settings.json` is written through `.tmp` + rename, with the previous file preserved as `.bak`. On load, if the main file is unreadable Pixelpipe transparently falls back to `.bak` and logs a `[warn]` line.
- **Profile preflight (`Test profile`).** New `src/Pixelpipe.Preflight.cs` runs a battery of checks (rclone presence + version, WinFsp, remote configured in rclone, drive letter free, RC port free, storage probe) and emits a per-line report with `[OK] / [WARN] / [FAIL]` states. Triggered from the tray submenu and from the new `Test` button on each main-window profile card.
- **Bandwidth normalization.** `NormalizeBandwidthLimit` accepts `off` / `OFF` / `1m` / `1M` / `1.5G` and folds invalid input to `off` on load and save.
- **Tests.** Five new unit tests (`QuoteArg`, `NormalizeBandwidthLimit`, `WriteAllTextAtomic`, `PreflightFormatting`, `FirstNonEmptyLine`). 26 total, all green.

Changed:

- **`QuoteArg` follows Microsoft's `CommandLineToArgvW` rules** (backslash doubling before `"`, trailing-backslash handling). Replaces the previous naive escape that mishandled certain label/remote values. `MountProfile`, `UnmountProfile`, and `Pixelpipe.Diagnostics` now use it for every interpolated path, drive letter, and remote name.
- **All in-process dialogs (`EditProfile`, `PromptForValue`, `ChooseFromList`, `PromptForApiKey`)** rebuilt on `TableLayoutPanel` + `AutoSize`. No more fixed pixel coordinates that clipped buttons at higher DPI.
- **Preflight now logs the full report** to `pixelpipe-ui.log` (level matches the worst state in the report) so users can refer back to it after dismissing the dialog. `RemoteProfile.LastError` gets a one-line summary (the first `[FAIL]` line, or first `[WARN]` if no failures) instead of the previous meta-message.
- **`WelcomeBalloonShown` setting** documented in `docs/CONFIGURATION.md`.

Removed:

- **Dead `Quote(string)` alias** in `Pixelpipe.Helpers.cs`. Its single caller (`ToggleStartup`) now uses `QuoteArg` directly.

## 0.5.5

Changed:

- Tray menu's `Diagnostics / repair...` and `Manage remotes...` items now open the main window on the matching tab (Diagnostics / Profiles) instead of the legacy modal dialogs. The modal versions still used hardcoded `Left = 12; Top = 12; Width = N;` pixel positions and clipped their button captions ("Refresh", "Copy", "Install rclone", "Install WinFsp", "Clear stale", etc.) plus the verbose-logging checkbox at the user's font/DPI. The tabbed views in the main window have the same actions, real layout containers, auto-refresh, and don't clip.

## 0.5.4

Fixed:

- Profile card was missing its title and "MOUNTED / unmounted" pill. The previous version put both in a Panel with `Dock = Left` / `Dock = Right` children — `Panel.AutoSize` doesn't measure docked children well and collapsed the header to zero height, so the title vanished. Header rewritten as a `TableLayoutPanel` with two `AutoSize` columns (title in col 0, pill in col 1), which `AutoSize` handles correctly.
- Profile card was clipping the `Unmount` button on the right and wrapping `Open` to its own row. Card was 460 px wide but four `AutoSize` buttons needed ~540 px. Card minimum width is now 560 px, the action rows have `WrapContents = false` so they stay on one line, and the long "Mount (cache)" button label is shortened to "Full cache".
- Card body switched from `FlowLayoutPanel` to `TableLayoutPanel` for the vertical stack. The TLP's `AutoSize` reliably measures the docked title and pill row; the flow panel's was the indirect cause of both the missing header and the wrapped buttons.

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
