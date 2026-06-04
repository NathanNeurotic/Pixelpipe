param(
  [string]$RclonePath,
  [switch]$SkipIfMissing
)

$ErrorActionPreference = 'Stop'

function Resolve-RclonePath {
  param([string]$ExplicitPath)
  if ($ExplicitPath) {
    if (!(Test-Path $ExplicitPath)) { throw "rclone not found: $ExplicitPath" }
    return (Resolve-Path $ExplicitPath).Path
  }

  $cmd = Get-Command rclone.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  $cmd = Get-Command rclone -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  if ($SkipIfMissing) {
    Write-Host 'rclone not found; skipping watch-folder local-backend smoke.'
    exit 0
  }
  throw 'rclone not found. Install rclone or pass -RclonePath.'
}

function Invoke-RcloneChecked {
  param(
    [string]$Exe,
    [string[]]$Arguments,
    [switch]$ExpectFailure
  )

  $stdout = [System.IO.Path]::GetTempFileName()
  $stderr = [System.IO.Path]::GetTempFileName()
  try {
    $proc = Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $code = $proc.ExitCode
    $stdoutText = Get-Content -Raw -Path $stdout -ErrorAction SilentlyContinue
    $stderrText = Get-Content -Raw -Path $stderr -ErrorAction SilentlyContinue
    if ($null -eq $stdoutText) { $stdoutText = '' }
    if ($null -eq $stderrText) { $stderrText = '' }
    $output = ($stdoutText + $stderrText).Trim()
  }
  finally {
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
  }
  if ($ExpectFailure) {
    if ($code -eq 0) { throw "Expected rclone failure, but command exited 0: $($Arguments -join ' ')" }
    return $output
  }
  if ($code -ne 0) {
    throw "rclone exited $code for '$($Arguments -join ' ')': $output"
  }
  return $output
}

$Rclone = Resolve-RclonePath $RclonePath
$Root = Join-Path ([System.IO.Path]::GetTempPath()) ("Pixelpipe-watch-smoke-" + [Guid]::NewGuid().ToString('N'))
$Watch = Join-Path $Root 'watch'
$Remote = Join-Path $Root 'remote'

try {
  New-Item -ItemType Directory -Force $Watch | Out-Null
  New-Item -ItemType Directory -Force $Remote | Out-Null

  $CopySrc = Join-Path $Watch 'copy.txt'
  $CopyDst = Join-Path $Remote 'copy.txt'
  Set-Content -Path $CopySrc -Value 'copy payload' -Encoding utf8
  Invoke-RcloneChecked $Rclone @('copyto', $CopySrc, $CopyDst) | Out-Null
  if (!(Test-Path $CopySrc)) { throw 'copyto removed the source file' }
  if ((Get-Content -Raw -Path $CopyDst).Trim() -ne 'copy payload') { throw 'copyto destination content mismatch' }

  $MoveSrc = Join-Path $Watch 'move.txt'
  $MoveDst = Join-Path $Remote 'move.txt'
  Set-Content -Path $MoveSrc -Value 'move payload' -Encoding utf8
  Invoke-RcloneChecked $Rclone @('moveto', $MoveSrc, $MoveDst) | Out-Null
  if (Test-Path $MoveSrc) { throw 'moveto left the source file behind' }
  if ((Get-Content -Raw -Path $MoveDst).Trim() -ne 'move payload') { throw 'moveto destination content mismatch' }

  $MissingSrc = Join-Path $Watch 'missing.txt'
  $MissingDst = Join-Path $Remote 'missing.txt'
  Invoke-RcloneChecked $Rclone @('copyto', $MissingSrc, $MissingDst) -ExpectFailure | Out-Null

  $TimeoutProc = Start-Process -FilePath $Rclone -ArgumentList @('rcd', '--rc-no-auth', '--rc-addr', '127.0.0.1:0') -WindowStyle Hidden -PassThru
  try {
    if ($TimeoutProc.WaitForExit(1000)) {
      throw "timeout probe exited early with code $($TimeoutProc.ExitCode)"
    }
    $TimeoutProc.Kill()
    $TimeoutProc.WaitForExit()
  }
  finally {
    if (!$TimeoutProc.HasExited) {
      try { $TimeoutProc.Kill() } catch {}
    }
  }

  Write-Host "watch-folder smoke passed using $Rclone"
}
finally {
  if (Test-Path $Root) {
    Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
  }
}
