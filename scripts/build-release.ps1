param(
  [string]$OutDir,
  [string]$OutFile,
  [switch]$Tests
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $Root 'dist' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (-not $OutFile) { $OutFile = 'Pixelpipe.exe' }
$Out = Join-Path $OutDir $OutFile

# Avoid "file in use" if a previous Pixelpipe is still running.
try { Get-Process Pixelpipe -ErrorAction Stop | Stop-Process -Force } catch {}

# Stamp the CHANGELOG version into a generated AssemblyVersion.cs so
# Application.ProductVersion at runtime matches the released version.
& (Join-Path $PSScriptRoot 'generate-version.ps1')

$SrcGlob = Join-Path $Root 'src\*.cs'
$Ico = Join-Path $Root 'assets\pixelpipe.ico'
$Manifest = Join-Path $Root 'app.manifest'
$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $Csc)) { $Csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $Csc)) { throw 'csc.exe not found. Install .NET Framework Developer Pack or Visual Studio Build Tools.' }
$CommonArgs = @(
  '/nologo','/target:winexe','/platform:x64','/optimize+',
  "/out:$Out","/win32icon:$Ico","/win32manifest:$Manifest",
  '/reference:System.dll','/reference:System.Core.dll',
  '/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
  '/reference:System.Web.Extensions.dll','/reference:System.Security.dll',
  '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
  '/reference:System.Management.dll',
  '/reference:Microsoft.CSharp.dll',"/recurse:$SrcGlob"
)
& $Csc @CommonArgs
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with code $LASTEXITCODE" }
Write-Host "Built $Out"

if ($Tests) {
  $TestRunner = Join-Path $Root 'scripts\run-tests.ps1'
  if (Test-Path $TestRunner) {
    & $TestRunner -Exe $Out
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with code $LASTEXITCODE" }
  }
}
