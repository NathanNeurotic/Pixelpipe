# Documentation audit notes

This note records the documentation pass so future maintainers can see what was verified and what should stay in sync.

## Verified against source

The pass checked the public docs against:

- `src/RemoteProfile.cs`
- `src/Pixelpipe.Settings.cs`
- `src/SettingsStore.cs`
- `src/Pixelpipe.ProviderWizards.cs`
- `src/Pixelpipe.Schedule.cs`
- `src/Pixelpipe.WatchFolder.cs`
- `src/Pixelpipe.Diagnostics.cs`
- `src/Pixelpipe.Preflight.cs`
- `src/ProviderCapabilities.cs`
- build and CI scripts under `scripts/` and `.github/workflows/`

## Corrections made

- Documented FTP / FTPS anywhere provider lists previously stopped at SFTP.
- Added watch-folder workflows to the README, user guide, configuration reference, troubleshooting, and smoke checklist.
- Added per-profile bandwidth schedules to user and configuration docs.
- Updated provider setup documentation to match the current direct rclone-config write path for non-OAuth providers.
- Removed the stale troubleshooting claim about a curl fallback for Pixeldrain quota checks.
- Clarified update behavior: Pixelpipe checks for releases and opens downloads, but does not silently auto-install.
- Clarified that rclone credentials live in rclone config, while the optional Pixeldrain quota key is DPAPI-protected in Pixelpipe settings.
- Updated contributor docs to mention both the canonical PowerShell build and SDK-style project alternatives.
- Added settings backup and diagnostics recovery guidance.
- Made the README a shorter start page and moved deeper workflow detail into `docs/USER_GUIDE.md`.

## Documentation ownership rules

- New user-visible controls should update `README.md` only if they change the first-run or core workflow.
- Workflow details belong in `docs/USER_GUIDE.md`.
- Exact settings keys and JSON shape belong in `docs/CONFIGURATION.md`.
- Provider support expectations belong in `docs/MULTI_REMOTE.md`.
- Failure modes belong in `docs/TROUBLESHOOTING.md` and quick answers in `docs/FAQ.md`.
- Build, CI, and code layout changes belong in `CONTRIBUTING.md` and `docs/DEVELOPMENT.md`.
- Manual QA changes belong in `docs/SMOKE_TEST.md`.
- Secret handling changes must update `SECURITY.md`.

## Verification checklist

Before merging future documentation changes:

```powershell
rg -n "curl fallback|SFTP, and custom|no SDK install|rclone config create|auto-update|nine first-class" README.md docs CONTRIBUTING.md SECURITY.md -g "!docs/DOCUMENTATION_AUDIT.md"
rg -n "WatchFolder|BandwidthScheduleEntries|TransferNotificationsEnabled|FTP / FTPS" README.md docs CONTRIBUTING.md SECURITY.md -g "!docs/DOCUMENTATION_AUDIT.md"
```

The first command should not find stale claims. The second should find the current feature references in the expected docs.
