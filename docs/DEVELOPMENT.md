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
src/Pixelpipe.Settings.cs     JSON + legacy registry settings persistence
src/Pixelpipe.Helpers.cs      Pure helpers, process capture, dialog primitives
src/TrayMenu.cs               Tray menu construction, theming, placement, smoke test
src/RemoteProfile.cs          Profile model
tests/TestRunner.cs           Hand-rolled console test runner over pure helpers
assets/pixelpipe.ico          App/tray icon
app.manifest                  asInvoker Windows manifest with PerMonitorV2 DPI
Build-Pixelpipe.bat           Double-click local build to Desktop (delegates to .ps1)
scripts/build-release.ps1     Release build to dist/
scripts/run-tests.ps1         Compile and run unit tests
scripts/build-installer.ps1   Optional Inno Setup build
installer/Pixelpipe.iss       Inno Setup installer script
.github/workflows/build.yml   Build + tests + smoke + rolling release CI
```

## Local build

```powershell
.\scripts\build-release.ps1
```

Output:

```text
dist\Pixelpipe.exe
```

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

`dist\Pixelpipe.exe /smoketest-menu` also runs the placement and theme smoke test without spawning the tray; CI gates on this too.

## Installer build

Install Inno Setup 6, then:

```powershell
.\scripts\build-release.ps1
.\scripts\build-installer.ps1
```

## Release workflow

Pushes to `main` or `master` update the `rolling` prerelease.

Pull requests only build artifacts.

## Design rules

- Do not run as Administrator by default.
- Do not hardcode a single drive letter.
- Do not force-kill rclone unless clean unmount failed and the user accepts the fallback.
- Do not poll the mounted drive path repeatedly.
- Keep API keys encrypted.
- Keep diagnostics copyable.
