# Development

Pixelpipe is intentionally small and dependency-light.

## Project layout

```text
src/Program.cs                Entry point and Main()
src/Pixelpipe.cs              TrayContext shell (ctor, tray icon, exit, UI dispatch)
src/Pixelpipe.Setup.cs        First-launch setup and rclone/WinFsp/winget checks
src/Pixelpipe.Profiles.cs     Profile CRUD, manage-remotes window
src/Pixelpipe.Mount.cs        Mount/unmount, bandwidth, health monitor
src/Pixelpipe.Refresh.cs      Profile refresh, transfer quota, DPAPI API key
src/Pixelpipe.Diagnostics.cs  Diagnostics window, logs, startup toggle, updates
src/Pixelpipe.MainWindow.cs   Tabbed main window + per-profile dashboard cards
src/Pixelpipe.SetupWizard.cs  Multi-step first-launch wizard
src/Pixelpipe.QuickControl.cs Always-on-top quick controls popup
src/Pixelpipe.Schedule.cs     Schedule timer for mount/unmount + bandwidth changes
src/Pixelpipe.WatchFolder.cs  Watch-folder queueing and rclone upload commands
src/Pixelpipe.Settings.cs     JSON + legacy registry settings persistence
src/SettingsStore.cs          Settings read/write cache, backups, atomic disk I/O
src/RcloneClient.cs           rclone process wrapper
src/DependencyProbe.cs        Cached rclone / WinFsp probes
src/MountManager.cs           rclone mount argument builder + process start
src/Pixelpipe.Helpers.cs      Pure helpers, process capture, dialog primitives
src/TrayMenu.cs               Tray menu construction, theming, placement, smoke test
src/RemoteProfile.cs          Profile model
tests/TestRunner.cs           Hand-rolled console test runner over pure helpers
assets/pixelpipe.ico          App/tray icon
app.manifest                  asInvoker Windows manifest with PerMonitorV2 DPI
Build-Pixelpipe.bat           Double-click local build to Desktop (delegates to .ps1)
scripts/build-release.ps1     Release build to dist/
scripts/run-tests.ps1         Compile and run unit tests
scripts/watch-folder-smoke.ps1 Optional rclone local-backend watch smoke
scripts/build-installer.ps1   Optional Inno Setup build
installer/Pixelpipe.iss       Inno Setup installer script
.github/workflows/build.yml   Build + tests + smoke + rolling release CI
.github/workflows/codeql.yml  C# CodeQL code scanning
```

## Local build

```powershell
.\scripts\build-release.ps1
```

Output:

```text
dist\Pixelpipe.exe
```

SDK-style alternative:

```powershell
dotnet build -c Release Pixelpipe.csproj
```

The PowerShell script remains the canonical CI build. The `.csproj` path exists for IDEs, analyzers, and developer convenience.

## Double-click build

```text
Build-Pixelpipe.bat
```

Output:

```text
%USERPROFILE%\Desktop\Pixelpipe.exe
```

## Tests

```powershell
.\scripts\run-tests.ps1
```

The runner compiles `src\*.cs` + `tests\*.cs` together into `dist\Pixelpipe.Tests.exe`, runs every test, prints a per-test pass/fail line, and exits non-zero if anything failed. CI runs this on every push.

SDK-style alternative:

```powershell
dotnet run --project Pixelpipe.Tests.csproj -c Release
```

`dist\Pixelpipe.exe /smoketest-menu` also runs the placement and theme smoke test without spawning the tray; CI gates on this too.

`scripts\watch-folder-smoke.ps1` uses rclone's local backend to verify the commands Pixelpipe issues for watch-folder copy/move uploads, non-zero failures, and timeout detection. CI runs it with `-SkipIfMissing`; developer machines with rclone installed run the real smoke.

## Installer build

Install Inno Setup 6, then:

```powershell
.\scripts\build-release.ps1
.\scripts\build-installer.ps1
```

## Release workflow

Pushes to `main` or `master` run the read-only build job, then a separate publish job with `contents: write` creates a versioned release when the top `CHANGELOG.md` version is new and updates the `rolling` prerelease.

Pull requests only build artifacts.

## Design rules

- Do not run as Administrator by default.
- Do not hardcode a single drive letter.
- Do not force-kill rclone unless clean unmount failed and the user accepts the fallback.
- Do not poll the mounted drive path repeatedly.
- Keep API keys encrypted.
- Keep provider secrets out of command-line arguments.
- Keep diagnostics copyable.
- Keep beginner docs current when adding user-visible controls. `README.md` should stay approachable; deeper workflows belong in `docs/USER_GUIDE.md` and exact field shapes in `docs/CONFIGURATION.md`.
