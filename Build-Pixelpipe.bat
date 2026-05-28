@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell is required to build Pixelpipe but was not found in PATH.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-release.ps1" -OutDir "%USERPROFILE%\Desktop"
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Created:
echo "%USERPROFILE%\Desktop\Pixelpipe.exe"
echo.
echo Run normally, not as Administrator.
echo On first launch, use Setup / dependencies to install rclone, WinFsp, configure PixelDrain, and save an optional quota API key.
echo.
pause
