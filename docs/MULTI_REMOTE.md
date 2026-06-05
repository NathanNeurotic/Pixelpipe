# Multi-remote support

Pixelpipe uses rclone as its provider layer. That keeps Pixelpipe focused on the Windows tray experience: profiles, drive letters, mounting, unmounting, status, logs, diagnostics, bandwidth, schedules, and recovery.

It should not grow a separate cloud SDK for every provider. rclone already handles provider protocols, OAuth, config formats, and many backend-specific quirks.

## Support tiers

### Tier 1: Pixeldrain

Pixeldrain has the deepest Pixelpipe integration:

- first-run setup path
- Pixeldrain profile defaults
- Pixeldrain rclone remote helper
- optional Pixeldrain API-key prompt
- DPAPI-protected quota API key storage
- storage display through rclone when available
- transfer-quota display through the Pixeldrain API
- mount, unmount, bandwidth, schedules, watch folders, diagnostics, and logs

Pixeldrain filesystem access still requires Pixeldrain's filesystem feature. Pixelpipe does not turn public file links into a writable drive.

### Tier 2: guided rclone providers

Pixelpipe includes guided profile creation for:

- Google Drive
- OneDrive
- Dropbox
- Box
- MEGA
- S3-compatible storage, including AWS S3, Cloudflare R2, Backblaze B2 through S3, Wasabi, DigitalOcean, Linode, Storj, and similar services
- WebDAV / Nextcloud / ownCloud / SharePoint
- SFTP
- FTP / FTPS

For these providers, Pixelpipe manages the profile and mount experience. Provider login and provider-specific credentials still belong to rclone.

OAuth providers use rclone's interactive config flow because browser login, MFA, and provider callbacks are better handled by rclone directly.

Non-OAuth provider forms write the rclone config entry for you. Secret fields are sent through rclone's obscure path over stdin and are not saved in Pixelpipe's settings file.

### Tier 3: custom rclone remotes

Any remote returned by `rclone listremotes` can be imported into Pixelpipe.

Custom remotes get the same mount, unmount, drive-letter, bandwidth, schedule, watch-folder, log, and diagnostics features. Provider-specific quota labels may be best effort.

## What Pixelpipe manages

For every profile, Pixelpipe can manage:

- label and provider type
- rclone remote name
- drive letter
- network/fixed mount mode
- low-overhead vs full-cache mount mode
- startup auto-mount
- global and per-profile bandwidth limits
- per-profile bandwidth schedules
- scheduled mount/unmount times
- watch-folder uploads
- status, storage text, session traffic, and speed
- profile preflight checks
- per-profile rclone log files

Pixelpipe mainly tracks mounts it launched itself. It can detect and help clean up some stale/orphan rclone processes, but externally launched rclone mounts are not fully managed state.

## Quota expectations

Quota data is not standardized across rclone providers.

| Provider | What to expect |
| --- | --- |
| Pixeldrain | Storage through rclone when available; transfer quota through the Pixeldrain API when an API key is saved. |
| Google Drive, OneDrive, Dropbox, Box | Storage/object data can appear when `rclone about` reports it. Transfer quota is not generally shown. |
| MEGA | Storage can appear; transfer limits are best checked in the MEGA web account. |
| S3-compatible storage | Bucket quota is usually not reported as account storage. |
| WebDAV / Nextcloud / SharePoint | Depends on server support. Some servers report quota; others do not. |
| SFTP | Depends on server/statfs support. |
| FTP / FTPS | Usually no quota reporting. |
| Custom | Best effort based on what rclone reports. |

When a provider cannot report quota, Pixelpipe should say so plainly instead of showing misleading zeroes.

## Recommended drive letters

Pixelpipe tries to pick sensible defaults, but any free letter can work.

| Provider | Suggested drive |
| --- | --- |
| Pixeldrain | `P:` |
| Google Drive | `G:` |
| MEGA | `M:` |
| OneDrive | `O:` |
| Dropbox | `D:` |
| Box | `K:` |
| S3 / R2 / B2 / Wasabi | `R:` |
| WebDAV / Nextcloud / SharePoint | `W:` |
| SFTP | `S:` |
| FTP / FTPS | `F:` |
| Custom | `Z:` |

If a drive does not appear, try a less common letter such as `X:` or `Z:` and make sure Pixelpipe is not running as Administrator.

## Known limitations

- Pixelpipe is a mount manager, not a two-way sync engine.
- Live speed/session traffic requires a Pixelpipe-launched mount with rclone RC enabled.
- Live bandwidth changes require a Pixelpipe-launched mount.
- Drive visibility can depend on Windows privilege context.
- Full-cache mode can consume local disk space under rclone's VFS cache.
- Watch folders do not watch subdirectories.
- Exporting Pixelpipe profiles does not export rclone remotes or provider credentials.
- Provider capability labels are best-effort defaults; the backend's actual `rclone about` response wins when it provides real data.
