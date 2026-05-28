@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "OUT=%USERPROFILE%\Desktop\Pixelpipe.exe"
set "SRC=%~dp0src\*.cs"
set "ICO=%~dp0assets\pixelpipe.ico"
set "MANIFEST=%~dp0app.manifest"

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not defined CSC for /f "delims=" %%I in ('where csc.exe 2^>nul') do if not defined CSC set "CSC=%%I"

if not defined CSC (
  echo Could not find csc.exe.
  echo Install the .NET Framework Developer Pack or Visual Studio Build Tools, then run this again.
  pause
  exit /b 1
)

dir /b "%SRC%" >nul 2>nul
if errorlevel 1 (
  echo Missing source files: "%SRC%"
  pause
  exit /b 1
)

if not exist "%ICO%" (
  echo Missing icon file: "%ICO%"
  pause
  exit /b 1
)

echo Building Pixelpipe.exe...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%" /win32icon:"%ICO%" /win32manifest:"%MANIFEST%" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll /recurse:"%SRC%"

if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Created:
echo "%OUT%"
echo.
echo Run normally, not as Administrator.
echo On first launch, use Setup / dependencies to install rclone, WinFsp, configure PixelDrain, and save an optional quota API key.
echo.
pause
