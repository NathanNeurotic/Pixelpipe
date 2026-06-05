# Pixelpipe FAQ

## What is Pixelpipe?

Pixelpipe is a Windows tray app for mounting rclone-compatible cloud remotes as Windows drives. It manages profiles, drive letters, mounts, logs, diagnostics, bandwidth, schedules, and watch-folder uploads.

It uses rclone for provider access and WinFsp for Windows drive mounting.

## Is Pixelpipe only for Pixeldrain?

No. Pixeldrain is the most integrated provider, but Pixelpipe can also manage Google Drive, MEGA, OneDrive, Dropbox, Box, S3-compatible storage, WebDAV / Nextcloud / SharePoint, SFTP, FTP / FTPS, and custom rclone remotes.

Pixeldrain gets extra polish: an API-key helper, encrypted quota-key storage, and Pixeldrain transfer-quota display.

## Windows says "Windows protected your PC". Is Pixelpipe safe?

Pixelpipe's downloads are not code-signed, so Windows SmartScreen may call them unrecognized apps on first run. That is expected for unsigned open-source software and is not, by itself, a malware detection.

To run it:

1. Click **More info**.
2. Click **Run anyway**.

To verify the file first, compare the release's `SHA256SUMS.txt` with:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

## Should I run Pixelpipe as Administrator?

No, not by default.

Admin-mounted drives can be hidden from normal File Explorer and can complicate cleanup. Run Pixelpipe normally unless you specifically need an elevated mount.

## Why does Pixelpipe need rclone?

rclone is the provider layer. It already knows how to talk to cloud services, handle provider-specific quirks, and store provider credentials.

Pixelpipe's job is to make those remotes easier to mount and manage from Windows.

## Why does Pixelpipe need WinFsp?

On Windows, `rclone mount` uses WinFsp to expose a FUSE-like filesystem as a drive letter.

If WinFsp is missing, Pixelpipe can help start an install through winget. A restart may be needed after installation.

## Does Pixelpipe mount normal public Pixeldrain file links?

No. Pixelpipe mounts a Pixeldrain filesystem remote through rclone. One-off public file links are not a writable drive.

## Where are settings and logs stored?

Settings:

```text
%APPDATA%\Pixelpipe\settings.json
```

Logs:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Settings backups:

```text
%APPDATA%\Pixelpipe\backups\
```

## Where are passwords stored?

Pixelpipe does not store provider passwords in `settings.json`.

Provider credentials are stored by rclone in rclone's config system. For non-OAuth provider forms, Pixelpipe writes rclone config entries directly and sends secret fields through rclone's obscure path over stdin so they are not placed on the command line.

The optional Pixeldrain quota API key is stored separately in `settings.json` as a Windows DPAPI-protected value for the current Windows user.

## Why is quota unavailable?

Quota behavior depends on the provider.

- Pixeldrain transfer quota needs a saved Pixeldrain API key.
- Generic storage usage needs the backend to answer `rclone about`.
- Some providers, especially S3-like buckets, FTP, or some WebDAV servers, do not expose quota in a way rclone can report.

Unavailable quota does not always mean mounting is broken.

## Does Pixelpipe sync files?

No. Pixelpipe mounts remotes as drives.

It also has a watch-folder upload feature, but that is a simple "new local file goes to remote" workflow. It is not a two-way sync engine.

## What is full-cache mode?

Full-cache mode uses rclone's fuller VFS cache behavior. It can help with workloads that seek, re-read, or stream files in ways cloud backends do not handle smoothly.

Start with low-overhead mode. Use full cache only when you need it, and remember it can consume local disk space.

## Can Pixelpipe change bandwidth while a drive is mounted?

Yes, for mounts launched by Pixelpipe. Pixelpipe starts rclone with RC enabled, then uses RC to apply live bandwidth changes.

If the mount was started manually outside Pixelpipe, live bandwidth controls may not work. Unmount the manual process and mount from Pixelpipe.

## Does Pixelpipe install updates automatically?

No. Pixelpipe does not silently download or install updates.

When update checks are enabled, Pixelpipe checks GitHub releases at most once per day and can show a tray-menu download item when a newer release exists. The manual `Check for updates` action opens the GitHub releases page.

## Can I move settings to another computer?

You can export/import Pixelpipe profiles, but the rclone remotes still need to exist on the target computer.

The Pixeldrain API key is DPAPI-bound to the Windows user that saved it, so copying `settings.json` does not copy a usable API key.

## What should I include in a bug report?

Use:

```text
Tools / diagnostics -> Copy diagnostics
```

Then include:

- what you clicked
- what you expected
- what happened instead
- the copied diagnostics
- whether Pixelpipe was running as Administrator

Review copied text before posting publicly. Pixelpipe scrubs common secret patterns, but diagnostics can still contain local paths, remote names, and provider output.
