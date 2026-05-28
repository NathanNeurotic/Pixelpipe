param([switch]$Optional)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot

# Note the ${env:NAME(x86)} syntax: in PowerShell, $env:ProgramFiles(x86) is
# parsed as $env:ProgramFiles followed by a literal (x86), which produces the
# wrong path. Use the explicit ${env:...} form to grab the actual env var.
$PfX86 = ${env:ProgramFiles(x86)}
$Pf    = $env:ProgramFiles
$ChocoBin = Join-Path ${env:ProgramData} 'chocolatey\bin\ISCC.exe'

$IsccCandidates = @(
  (Join-Path $PfX86 'Inno Setup 6\ISCC.exe'),
  (Join-Path $Pf 'Inno Setup 6\ISCC.exe'),
  $ChocoBin
) | Where-Object { $_ }

$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$Iscc) {
  # Fall back to PATH lookup so any future packaging works.
  $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if ($cmd) { $Iscc = $cmd.Source }
}

if (!$Iscc) {
  if ($Optional) { Write-Warning 'Inno Setup ISCC.exe not found; skipping installer build.'; exit 0 }
  throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or `choco install innosetup`.'
}

Write-Host "Using ISCC: $Iscc"

$Exe = Join-Path $Root 'dist\Pixelpipe.exe'
if (!(Test-Path $Exe)) { throw "Build Pixelpipe.exe first: $Exe" }

& $Iscc (Join-Path $Root 'installer\Pixelpipe.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with code $LASTEXITCODE" }

$Setup = Join-Path $Root 'dist\Pixelpipe-Setup.exe'
if (!(Test-Path $Setup)) { throw "ISCC ran but Pixelpipe-Setup.exe was not produced at $Setup" }
Write-Host "Built $Setup"
