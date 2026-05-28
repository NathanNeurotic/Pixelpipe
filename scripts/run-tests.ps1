$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $Root 'dist'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$Out = Join-Path $OutDir 'Pixelpipe.Tests.exe'

# Avoid "file in use" if a previous test run is still alive.
try { Get-Process Pixelpipe.Tests -ErrorAction Stop | Stop-Process -Force } catch {}
$SrcGlob = Join-Path $Root 'src\*.cs'
$TestsGlob = Join-Path $Root 'tests\*.cs'
$Manifest = Join-Path $Root 'app.manifest'
$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $Csc)) { $Csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $Csc)) { throw 'csc.exe not found. Install .NET Framework Developer Pack or Visual Studio Build Tools.' }

$Args = @(
  '/nologo','/target:exe','/platform:anycpu','/optimize+',
  "/out:$Out","/win32manifest:$Manifest",
  '/main:Pixelpipe.Tests.TestRunner',
  '/reference:System.dll','/reference:System.Core.dll',
  '/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',
  '/reference:System.Web.Extensions.dll','/reference:System.Security.dll',
  '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
  '/reference:Microsoft.CSharp.dll',
  "/recurse:$SrcGlob","/recurse:$TestsGlob"
)
& $Csc @Args
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with code $LASTEXITCODE" }

Write-Host "Running $Out"
& $Out
if ($LASTEXITCODE -ne 0) { throw "Tests failed with code $LASTEXITCODE" }
