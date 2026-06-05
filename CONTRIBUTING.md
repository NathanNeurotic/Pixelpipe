# Contributing to Pixelpipe

Thanks for considering a contribution. Pixelpipe is intentionally small and dependency-light — the goal is a Windows tray app that mounts rclone remotes reliably, not a full sync engine. Pull requests that keep that scope are welcome.

## What you need locally

- Windows 10 or 11.
- .NET Framework 4.8 (the developer pack or Visual Studio Build Tools; `csc.exe` is what the canonical build script invokes).
- rclone and WinFsp installed if you want to manually exercise the mount flows (the unit tests do not need them).
- PowerShell 5+ (built into Windows).

That's enough for the canonical script build. The SDK-style `.csproj` files are also present for IDE/analyzer workflows if you have the .NET SDK installed, but CI still uses the PowerShell scripts.

## Build, test, smoke

```powershell
# Compile dist\Pixelpipe.exe
.\scripts\build-release.ps1

# Compile dist\Pixelpipe.Tests.exe and run all unit tests
.\scripts\run-tests.ps1

# Run the tray menu placement + theme smoke test (CI gates on this)
.\dist\Pixelpipe.exe /smoketest-menu

# Optional when rclone is installed: verify watch-folder copy/move command behavior
.\scripts\watch-folder-smoke.ps1
```

The first three should be clean before you open a PR. Run the watch-folder smoke when your change touches watch uploads, rclone command construction, process timeouts, or release/CI behavior.

Alternative SDK entry points:

```powershell
dotnet build -c Release Pixelpipe.csproj
dotnet run --project Pixelpipe.Tests.csproj -c Release
```

## Project layout

| Path | Purpose |
| --- | --- |
| `src/Program.cs` | Entry point, exception handlers, single-instance mutex |
| `src/Pixelpipe.cs` | `TrayContext` shell — fields, tray icon, balloon, UI dispatch, exit |
| `src/Pixelpipe.Setup.cs` | rclone / WinFsp / winget detection, portable rclone download |
| `src/Pixelpipe.Profiles.cs` | Profile CRUD, edit dialog, primary, remove |
| `src/Pixelpipe.Mount.cs` | Mount, unmount, bandwidth, taskkill fallback |
| `src/Pixelpipe.Refresh.cs` | Periodic refresh, transfer quota, DPAPI API key |
| `src/Pixelpipe.Diagnostics.cs` | Diagnostics text + per-profile preflight rendering |
| `src/Pixelpipe.Preflight.cs` | `Test profile` checks (rclone, WinFsp, RC port, etc.) |
| `src/Pixelpipe.Settings.cs` | JSON + legacy registry persistence, atomic write with .bak recovery |
| `src/Pixelpipe.Helpers.cs` | Pure helpers, process capture, dialog primitives, QuoteArg |
| `src/Pixelpipe.UpdateCheck.cs` | Once-per-day GitHub API check for newer releases |
| `src/Pixelpipe.MainWindow.cs` | Profiles / Diagnostics / Logs / Settings tabs |
| `src/Pixelpipe.QuickControl.cs` | Heads-up speed/traffic popup |
| `src/Pixelpipe.SetupWizard.cs` | First-run multi-step wizard |
| `src/Pixelpipe.Schedule.cs` | Scheduled mount/unmount and bandwidth transitions |
| `src/Pixelpipe.WatchFolder.cs` | Watch-folder queueing and rclone upload commands |
| `src/TrayMenu.cs` | Tray menu + theme + placement + smoke test |
| `tests/TestRunner.cs` | Hand-rolled console test runner |
| `scripts/build-release.ps1` | csc invocation for the tray app |
| `scripts/run-tests.ps1` | csc invocation for the test runner |
| `scripts/generate-version.ps1` | Stamps `src/AssemblyVersion.cs` from CHANGELOG |
| `scripts/build-installer.ps1` | Inno Setup installer build |
| `installer/Pixelpipe.iss` | Inno Setup script |
| `.github/workflows/build.yml` | CI: build → tests → smoke → installer → release |

## Coding conventions

- C# without any nullable-reference-types or modern syntax. The codebase targets .NET Framework 4.8 with csc.exe; keep it lean and old-friendly.
- Use the existing partial-class split: feature code goes into `Pixelpipe.<Area>.cs` files that all participate in `partial class TrayContext`.
- New colors go in `WindowTheme` (window family) or `TrayMenuTheme` (tray strip). Don't sprinkle `Color.FromArgb(...)` across files.
- New WinForms layouts use `TableLayoutPanel` / `FlowLayoutPanel` with `AutoSize`. No `Left = N; Top = N; Width = N;` pixel positions — they clip at non-default fonts/DPI. See `Pixelpipe.MainWindow.cs` for the pattern.
- Worker threads must iterate a profile snapshot (`SnapshotProfiles()`), never the live `profiles` list. UI mutations of `profiles` must take `profilesLock`.
- Background work items must be wrapped in `try { ... } catch (Exception ex) { LogUiIssue(...) }`. The global exception handlers in `Program.Main` are a backstop, not the primary defense.
- New settings keys go in `docs/CONFIGURATION.md`.
- User-visible workflows should be reflected in `README.md`, `docs/USER_GUIDE.md`, `docs/FAQ.md`, or `docs/TROUBLESHOOTING.md` as appropriate.

## Adding a feature

1. Branch off `main`. Branch name like `feature/short-description` or `fix/short-description`.
2. Write the feature in the relevant `Pixelpipe.*.cs` file. Prefer adding to an existing partial-class file over creating a new one.
3. If you add a pure helper, add a unit test in `tests/TestRunner.cs` and a `Run("YourTest", TestYourThing)` line in `Main()`.
4. Update `docs/USER_GUIDE.md`, `docs/CONFIGURATION.md`, and `docs/SMOKE_TEST.md` if the user workflow, settings shape, or manual test plan changes.
5. Bump `CHANGELOG.md` — add a new `## X.Y.Z` block at the top. Follow the existing Added/Changed/Fixed/Removed sections. The CI workflow auto-cuts a versioned release from this header on push to `main`.
6. Run all three of build / tests / smoke locally.
7. Open the PR. CI must pass before merge.

## Bug fixes

Same flow as features. If the bug is user-visible, add a one-line entry under `Fixed:` in the next CHANGELOG block; if it's an internal cleanup, `Changed:` is fine.

## Releases

Releases are auto-cut by `.github/workflows/build.yml` from the top entry in `CHANGELOG.md`. The workflow also:

- Stamps `installer/Pixelpipe.iss` with `MyAppVersion` from the same CHANGELOG entry.
- Stamps `src/AssemblyVersion.cs` so `Application.ProductVersion` at runtime matches the released version (used by the update check).
- Updates the `rolling` tag on every push.
- Mints a permanent `v<version>` GitHub release the first time a new version appears.

You don't need to tag manually. Just bump CHANGELOG, merge, and CI does the rest.

## Tray app constraints to keep in mind

- The app runs in `ApplicationContext`, not with a main `Form`. Closing the main window must not exit the app.
- The tray menu is a `ContextMenuStrip` whose visibility we have to track ourselves — never call `RebuildMenu` while the menu is visible (use `UpdateMenuLiveState` to edit existing items in place).
- `WriteAllTextAtomic` is required for `settings.json` writes; a power loss mid-write must leave a recoverable file.
- All forms use `AutoScaleMode = Dpi`. Don't add anything that hardcodes pixel sizes inside them.
- The `Local\Pixelpipe.TrayApp` mutex enforces one tray instance per Windows session.
