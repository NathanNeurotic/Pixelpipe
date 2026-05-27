# Security

## Supported versions

Pixelpipe is early-stage. Use the latest GitHub release or rolling release unless you are testing a specific commit.

## API key storage

Pixelpipe can store a PixelDrain API key for quota display and rclone remote setup.

The key is encrypted with Windows DPAPI for the current Windows user before it is written to Pixelpipe's settings file:

```text
%APPDATA%\Pixelpipe\settings.json
```

Do not post this file publicly. Even though the key value is DPAPI-protected, it is still account-related data.

## Logs

Logs are stored here:

```text
%LOCALAPPDATA%\Pixelpipe\logs\
```

Pixelpipe avoids intentionally writing raw API keys to logs. When reporting bugs, review logs before posting them publicly.

## Running as Administrator

Pixelpipe should run as a normal user. Running as Administrator can make mounted drives invisible to normal File Explorer and can complicate process cleanup.

The app manifest requests `asInvoker`, not admin elevation.

## Reporting vulnerabilities

Open a private security advisory on GitHub if available, or open an issue with minimal sensitive detail and request a private contact path.
