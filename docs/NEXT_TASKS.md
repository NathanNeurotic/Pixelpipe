# Potential next tasks

Open follow-ups that still need a human or a real Pixelpipe install.

## Tray menu QA (manual only)

- Verify tray menu and submenus across multi-monitor layouts, mixed DPI scaling, and taskbars pinned to each screen edge. `/smoketest-menu` covers the math and theme but cannot click the real notification area.
- Capture real screenshots for `docs/assets/` now that the menu has multi-profile, setup, and `Tools / diagnostics` submenus.
- If submenu placement still misbehaves on unusual shells, replace the WinForms fallback with a small native tray popup anchor helper.

## Watch after rollout

- Watch the `Tools / diagnostics` grouping during real use. If common actions feel too buried, promote only those actions back to the top level.
- Watch the menu-open refresh throttle (30s). If users complain about stale data, lower it or invalidate on specific events.
