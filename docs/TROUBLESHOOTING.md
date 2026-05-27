# Troubleshooting

## Cleanly stop a stuck rclone mount

Open Administrator PowerShell only for cleanup:

```powershell
Get-Process rclone -ErrorAction SilentlyContinue | Stop-Process -Force
cmd /c "net use P: /delete /y"
mountvol P: /D 2>$null
Remove-PSDrive P -Force -ErrorAction SilentlyContinue
```

Then restart the tray app normally, not as Administrator.

## Verify rclone config

```powershell
rclone listremotes
rclone about Pixeldrain: --json
rclone lsd Pixeldrain:
```

## Verify WinFsp

Check for:

```text
C:\Program Files (x86)\WinFsp\bin\winfsp-x64.dll
```

or reinstall:

```powershell
winget install -e --id WinFsp.WinFsp
```

## Explorer visibility

If `Test-Path P:\` is true but “This PC” does not show the drive, restart Explorer or use the tray’s `Open P:\` entry. Do not run the tray app elevated unless you intentionally want an elevated mount.
