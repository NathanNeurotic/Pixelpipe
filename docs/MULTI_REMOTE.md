# Multi-remote support

Pixelpipe uses rclone as the provider layer. That means Pixelpipe should not implement separate mount engines for every cloud service. It should manage rclone remotes cleanly, keep Pixeldrain-specific polish where it matters, and avoid pretending every backend exposes the same quota data.

## Support tiers

### Tier 1 — Pixeldrain

Pixeldrain has first-class support:

- setup helper
- Pixeldrain API key prompt
- rclone remote creation helper
- storage display
- monthly / last-30-days transfer quota display
- DPAPI-encrypted API key storage

### Tier 2 — known rclone providers

Pixelpipe includes guided profile creation for:

- Google Drive
- MEGA
- OneDrive
- Dropbox
- Box
- S3-compatible storage
- WebDAV / Nextcloud
- SFTP

For these providers, Pixelpipe manages the mount, drive letter, startup behavior, diagnostics, logs, and bandwidth control. Provider login/configuration remains delegated to `rclone config`.

### Tier 3 — custom rclone remotes

Any rclone remote returned by `rclone listremotes` can be imported into Pixelpipe and mounted as a drive.

## Why rclone config is still used

OAuth, MFA, app-specific passwords, S3 endpoint variations, WebDAV server quirks, and provider-specific rclone options vary significantly. Reimplementing all of that in Pixelpipe would add a lot of fragile code and increase credential-handling risk.

Pixelpipe's safer role is:

```text
configure remotes through rclone
mount them cleanly from tray
show useful status and logs
recover when Windows/rclone gets stuck
```

## Pixeldrain quota behavior

PixelDrain transfer quota is provider-specific. Pixelpipe only shows it when:

1. at least one profile is marked as Pixeldrain, and
2. a PixelDrain API key is configured.

Other backends may expose storage usage through `rclone about`, but monthly transfer quotas are not standardized across rclone providers.

## Recommended profile defaults

| Provider | Suggested drive |
| --- | --- |
| Pixeldrain | `P:` |
| Google Drive | `G:` |
| MEGA | `M:` |
| OneDrive | `O:` |
| Dropbox | `D:` |
| S3 / R2 / B2 | `R:` |
| WebDAV / Nextcloud | `W:` |
| SFTP | `S:` |
| Custom | `Z:` |

## Known limitations

- Pixelpipe tracks mounts that it launched itself. It does not fully manage externally launched rclone mount processes.
- Live speed/session traffic requires rclone RC and therefore only applies to Pixelpipe-launched mounts.
- Drive visibility can still depend on Windows privilege context. Normal-user launch is recommended.
- Full-cache mode may consume local disk space under rclone's VFS cache.
