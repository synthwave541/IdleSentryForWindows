# Windows developer notes

For downloads, setup, screenshots, and usage, start with the
[repository README](../README.md).

## Application structure

The application targets .NET 8 and uses WPF, [WPF UI](https://github.com/lepoco/wpfui),
and [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon).

| File | Responsibility |
| --- | --- |
| `App.xaml.cs` | App lifecycle, single-instance activation, and tray menu |
| `AppState.cs` | Settings, inactivity detection, process tracking, and termination |
| `AppDiscovery.cs` | Local app discovery and executable resolution |
| `AppPickerWindow.xaml` | Searchable application chooser |
| `MainWindow.xaml` | Dashboard, navigation, rules, system preferences, and About |
| `NavigationPages.cs` | Cached page targets for Fluent navigation |
| `WindowsAccentWatcher.cs` | Windows accent resource updates |
| `Converters.cs` | View formatting and binding converters |

Application discovery reads Start menu and desktop shortcuts, App Paths
registrations, and visible running apps. Supported Electron updater shortcuts are
resolved to the application's executable. Discovery does not launch candidate
apps and does not search network paths.

Inactivity comes from Win32 `GetLastInputInfo`. Standard Quit requests a normal
window close, waits briefly, and then terminates processes that remain. Force
Quit terminates immediately. Both methods can lose unsaved work; do not describe
Standard Quit as a guarantee that data is saved.

Preferences are stored in `%LOCALAPPDATA%\IdleSentry\settings.json`, including
when running the portable executable. The app can create a current-user login
startup entry when enabled in its settings.

## Build

From this directory, with .NET 8 SDK installed on Windows:

```powershell
.\build.ps1 -Arch x64
.\build.ps1 -Arch arm64
# Or build both:
.\build.ps1 -Arch all
```

`build.bat` is a double-click entry point for the same build process. Published
self-contained executables go to `publish/win-x64/` and `publish/win-arm64/`.
The build script may stop an IdleSentry instance running from its output folder
before replacing that executable.

## Development checks

From the repository root:

```powershell
dotnet run --project './IdleSentry.ChooserTests/IdleSentry.ChooserTests.csproj' -c Release
```

The sibling test project checks discovery fixtures, navigation, accessibility
selection, badge placement, Windows accent bindings, and countdown presentation.
It uses sample state for UI checks and is excluded from application packages.
See [CONTRIBUTING.md](../CONTRIBUTING.md) for manual verification guidance.

The current dependency restore reports `NU1701` for H.NotifyIcon.Wpf 2.4.1's
framework assets. Builds and local checks pass, but tray behavior still needs
validation on each target Windows environment.

## Version and packaging

`<Version>` in `IdleSentry.csproj` is the source of the sidebar, About, executable,
and installer version. The initial Windows version, 1.0.10, aligns with the macOS
source used for this port. A macOS checkout is not required to build Windows.

Install [Inno Setup 6.3+](https://jrsoftware.org/isdl.php), then run:

```powershell
.\build-installer.ps1 -Arch all
```

Keep `build.ps1`, `build-installer.ps1`, `package-source.ps1`, and
`installer/IdleSentry.iss` in Git. Generated files in `bin/`, `obj/`, `publish/`,
and `dist/` are ignored. See [PUBLISHING.md](PUBLISHING.md) for the release process.

## License

Code is distributed under [Apache-2.0](LICENSE). Redistribution must also include
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for bundled dependencies.