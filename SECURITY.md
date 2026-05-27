# Security Policy

## Secrets

Do not commit PixelDrain API keys, rclone config files, logs, or screenshots containing account data.

The app stores the optional quota API key in the current user's registry using Windows DPAPI. It does not write the plaintext API key to the app log.

## Network behavior

The app contacts:

- `https://downloads.rclone.org/` when downloading portable rclone.
- `https://pixeldrain.com/api/` for account/quota display when an API key is configured.
- local rclone RC on `127.0.0.1` while a mount is active.

## Reporting issues

When opening an issue, use `Copy diagnostics` from the tray menu, but review the text before posting it publicly.
