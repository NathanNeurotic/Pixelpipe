# Pixelpipe FAQ

## Windows says "Windows protected your PC" / "unrecognized app" — is Pixelpipe safe?

Yes. Pixelpipe's downloads are **not code-signed** (a code-signing certificate is a recurring paid cost the project doesn't carry yet), so Windows SmartScreen shows a blue *"Windows protected your PC"* warning the first time you run `Pixelpipe.exe` or `Pixelpipe-Setup.exe`. Every unsigned open-source app trips this; it is not a malware detection.

To run it: click **More info**, then **Run anyway**. You only do this once per downloaded file.

Want to verify the file is genuine first? Each release includes `SHA256SUMS.txt`; compare it with:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

A signed build would remove the prompt entirely, but that needs a paid certificate the project doesn't have yet.

## Does Pixelpipe mount normal public PixelDrain file links?

No. Pixelpipe mounts a PixelDrain filesystem remote through rclone. Normal one-off public file links are not a writable drive.

## Why does Pixelpipe need WinFsp?

rclone mount uses WinFsp on Windows to expose a FUSE-like filesystem as a drive.

## Should I run Pixelpipe as Administrator?

No, not by default. Admin-mounted drives can be invisible to normal File Explorer. Run Pixelpipe normally unless you intentionally need an elevated mount.

## Why does the drive not appear in This PC?

Common causes:

- Pixelpipe was launched as Administrator.
- WinFsp is missing or needs a restart.
- The selected drive letter is already used.
- rclone exited immediately.
- the PixelDrain rclone remote is not configured.

Open `Diagnostics / repair...` from the tray menu and copy diagnostics.

## Why is quota unavailable?

Quota requires a PixelDrain API key. Mounting can still work without the API key if rclone is already configured.

## Where is the API key stored?

Pixelpipe encrypts it with Windows DPAPI for the current user and stores the protected value in:

```text
%APPDATA%\Pixelpipe\settings.json
```

## Does Pixelpipe auto-update?

No. It has a `Check for updates` menu item that opens the GitHub releases page. Silent auto-update is intentionally not included yet.
