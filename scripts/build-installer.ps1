param([switch]$Optional)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$IsccCandidates = @(
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$Iscc) {
  if ($Optional) { Write-Warning 'Inno Setup ISCC.exe not found; skipping installer build.'; exit 0 }
  throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or choco install innosetup.'
}
$Exe = Join-Path $Root 'dist\Pixelpipe.exe'
if (!(Test-Path $Exe)) { throw "Build Pixelpipe.exe first: $Exe" }
& $Iscc (Join-Path $Root 'installer\Pixelpipe.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with code $LASTEXITCODE" }
