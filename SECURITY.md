# Security

## Supported versions

Pixelpipe is early-stage. Use the latest GitHub release or rolling release unless you are testing a specific commit.

## Unsigned downloads

Pixelpipe builds are not code-signed. Windows SmartScreen may show **"Windows protected your PC"** and call the download an unrecognized app on first run.

That prompt is expected for unsigned open-source software. To verify a release before running it, compare the release's `SHA256SUMS.txt` with:

```powershell
Get-FileHash .\Pixelpipe.exe -Algorithm SHA256
```

## Secret storage

Pixelpipe has two kinds of secret-adjacent data:

| Data | Where it lives |
| --- | --- |
| Provider credentials for rclone remotes | rclone's own config system. |
| Optional Pixeldrain API key for quota display | `%APPDATA%\Pixelpipe\settings.json` as a Windows DPAPI-protected value. |

Pixelpipe does not store provider passwords in `settings.json`.

For non-OAuth provider setup forms, Pixelpipe writes rclone config entries directly and sends secret fields through rclone's obscure path over stdin. This keeps those secret values out of command-line arguments. rclone's config obfuscation is not a password manager; protect your Windows account and rclone config file accordingly.

The Pixeldrain quota key is encrypted with Windows DPAPI for the current Windows user before it is written as `PixeldrainApiKeyProtected`. Copying the settings file to another machine or user profile does not make that key usable there.

Do not post `settings.json` publicly. Even protected settings can contain account-related metadata such as profile labels, remote names, and local paths.

## Logs and diagnostics

Logs are stored here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Pixelpipe avoids intentionally writing raw API keys to logs. The Diagnostics copy action scrubs common token, password, and authorization-header patterns before placing text on the clipboard.

Scrubbing is a defense-in-depth helper, not a promise that every provider-specific secret format can be recognized. Raw logs and diagnostics may still include:

- local file paths
- drive letters
- remote names
- provider names
- rclone output
- error messages from third-party tools

Review diagnostics and logs before posting them publicly.

## Portable rclone download

When Pixelpipe downloads portable rclone, it uses a pinned rclone release URL and verifies the zip against rclone's published SHA-256 sums before extraction. If the checksum is missing or mismatched, Pixelpipe refuses to install that download.

## Running as Administrator

Pixelpipe should run as a normal user. Running as Administrator can make mounted drives invisible to normal File Explorer and can complicate process cleanup.

The app manifest requests `asInvoker`, not admin elevation.

## Reporting vulnerabilities

Open a private security advisory on GitHub if available.

If that is not available, open an issue with minimal sensitive detail and request a private contact path. Do not post API keys, rclone config contents, full logs, or private remote names publicly.
