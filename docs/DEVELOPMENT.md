# Development

This is a single-file WinForms tray app intentionally kept simple.

## Main source

```text
src/Pixelpipe.cs
```

## Build

```powershell
.\scripts\build-release.ps1
```

or double-click:

```text
Build-Pixelpipe.bat
```

## Design rules

- Keep dependency checks bounded and user-confirmed.
- Do not poll `P:\` repeatedly; cloud mounts can block Explorer-style checks.
- Do not run by default as Administrator.
- Prefer normal-user startup through HKCU Run.
- Keep rclone RC bound to localhost.
- Do not log API keys.
