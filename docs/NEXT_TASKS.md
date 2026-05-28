# Potential next tasks

These are follow-up refinements noticed while fixing tray submenu placement.

## Tray menu QA

- Verify tray menu and submenus on multiple monitor layouts, mixed DPI scaling, and taskbars pinned to each screen edge.
- Capture real screenshots for `docs/assets/` now that the menu has multi-profile and setup submenus.
- If submenu placement still misbehaves on unusual shells, replace the WinForms fallback with a small native tray popup anchor helper.

## Menu code cleanup

- Watch the new `Tools / diagnostics` grouping during real use. If common actions feel too buried, promote only those actions back to the top level.

## Reliability

- Consider adding a fuller automated UI harness if tray-menu regressions continue; the current smoke mode verifies placement math and theme application but cannot click the Windows notification area.

## Completed from this list

- Added shared tray menu item helpers for action, enabled, and checked states.
- Added lightweight UI helper logging for tray menu theme, submenu positioning, UI dispatch, and tray balloon failures.
- Added `docs/SMOKE_TEST.md` for mount, unmount, bandwidth, setup, diagnostics, menu placement, and startup checks.
- Moved tray menu construction, theming, renderer, and placement helpers into `src/TrayMenu.cs`.
- Updated build scripts to compile all `src/*.cs` files.
- Throttled menu-open refresh work so repeated tray opens do not queue profile refreshes every time.
- Added `/smoketest-menu` for non-interactive tray menu placement checks.
- Improved stuck unmount fallback messaging, logging, and silent-path behavior.
- Grouped diagnostics, logs, settings, refresh, and update actions under `Tools / diagnostics` to keep the top-level tray menu shorter.
- Expanded `/smoketest-menu` to verify tray menu theme application as well as submenu placement math.
