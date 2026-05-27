$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $Root 'dist'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$Out = Join-Path $OutDir 'Pixelpipe.exe'
$Src = Join-Path $Root 'src\Pixelpipe.cs'
$Ico = Join-Path $Root 'assets\pixelpipe.ico'
$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $Csc)) { $Csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $Csc)) { throw 'csc.exe not found. Install .NET Framework Developer Pack or Visual Studio Build Tools.' }
& $Csc /nologo /target:winexe /platform:anycpu /optimize+ /out:$Out /win32icon:$Ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll $Src
Write-Host "Built $Out"
