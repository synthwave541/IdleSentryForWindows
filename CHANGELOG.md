# Release history

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
