# Release history

## 1.0.14 — 2026-10-21

- Open a scrollable closing queue from the sidebar countdown, ordered by next deadline.
- Keep the list available when keyboard or mouse input resets inactivity.
- Truncate long names in the compact card, with a full-name tooltip and a separate additional-app count.
- Preserve scroll position across timer updates and hide the queue when monitoring pauses or no monitored apps remain running.
- Match the macOS source version at 1.0.14. Release downloads are published separately from source pushes.

## 1.0.13 — 2026-10-20

- Show the number of currently running monitored apps in a red circular sidebar
  badge, and hide the badge completely when none are running.
- Keep the badge compact with a true 16-pixel circle and centered white count.

- Fix the crash after closing a monitored app when its notification was sent
  before the programmatically constructed tray icon had been registered.
- Explicitly create and retain the tray icon for windowed and background starts,
  retry registration when Explorer is unavailable, and isolate notification failures.
- Serialize process scans, publish completed app states on the UI thread, and
  recheck process exit before clearing a countdown.
- Use one grace period for all processes belonging to an app, instead of waiting
  separately for each browser helper process.
- Stop monitoring and release the tray icon on explicit exit.
- Accept SDK commit metadata when validating executable versions for installers.

## 1.0.10 — 2026-10-20

Initial Windows release of IdleSentry, based on the macOS implementation.

### Application

- Search installed and running apps by name, with icons, multiple selection,
  refresh, and a manual executable fallback.
- Configure inactivity timeouts from 10 seconds to 60 minutes and choose
  Standard Quit or Force Quit.
- Keep monitoring in the system tray, pause or resume it, and optionally start
  at Windows login.
- Store preferences locally without accounts or telemetry.

### Interface

- Fluent dashboard with Windows accent colors and an animated sidebar selection
  line inset inside the selected row.
- One amber sidebar countdown shared by every page.
- Full-width app cards, running-status indicators, and an app-count badge.
- Aligned dashboard actions and tooltips for truncated app paths.
- Keyboard and accessibility navigation, with selection motion respecting the
  Windows animation preference.

### Distribution

- Self-contained x64 and ARM64 builds, per-user installers, and portable ZIPs.
- Clean source archive with tests, build scripts, and installer source.
- SHA-256 checksums and Windows CI covering development checks and both builds.

Both quit strategies can lose unsaved work. Binaries are currently unsigned.
See the README and release notes for requirements and app-discovery limitations.
