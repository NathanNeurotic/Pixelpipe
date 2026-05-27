# Development

Pixelpipe is intentionally small and dependency-light.

## Project layout

```text
src/Pixelpipe.cs              Main WinForms tray app
assets/pixelpipe.ico          App/tray icon
app.manifest                  asInvoker Windows manifest
Build-Pixelpipe.bat           Double-click local build to Desktop
scripts/build-release.ps1     Release build to dist/
scripts/build-installer.ps1   Optional Inno Setup build
installer/Pixelpipe.iss       Inno Setup installer script
.github/workflows/build.yml   Build + rolling release CI
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
