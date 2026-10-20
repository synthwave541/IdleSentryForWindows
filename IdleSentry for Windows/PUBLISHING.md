# Publishing IdleSentry for Windows

The Windows source repository is
[synthwave541/IdleSentryForWindows](https://github.com/synthwave541/IdleSentryForWindows).
The macOS counterpart is maintained separately in
[synthwave541/IdleSentry](https://github.com/synthwave541/IdleSentry).

## Source versus release files

Commit application code, assets, tests, documentation, dependency notices, build
scripts, and `installer/IdleSentry.iss`. The installer scripts are source; users
run the compiled Setup executable.

Do not commit `bin/`, `obj/`, `publish/`, `dist/`, personal settings, local editor
files, signing material, or credentials. The root `.gitignore` excludes these,
as well as a local macOS checkout named `IdleSentry/` beside the Windows project.
That local checkout is not part of the Windows repository.

## Prepare a release

Run commands below from this project's directory:

1. Set `<Version>` in `IdleSentry.csproj` to the intended release version.
2. Run the development checks from the repository root:
   `dotnet run --project './IdleSentry.ChooserTests/IdleSentry.ChooserTests.csproj' -c Release`.
3. Run `./package-source.ps1` to create a clean source ZIP containing the root
   README, documentation, workflow, application source, and tests.
4. With .NET 8 SDK and Inno Setup 6.3+ installed, run
   `./build-installer.ps1 -Arch all`. This builds x64 and ARM64 executables,
   compiles installers, creates portable ZIPs, and generates `SHA256SUMS.txt`.
5. Verify the packages, commit the reviewed source, and create a GitHub release
   targeting that commit. Use `v<version>` for a regular release and mark it
   **Latest** so GitHub displays the version in the repository's Releases panel.
   Use a `-preview.n` suffix and the prerelease flag only for preview releases;
   GitHub does not allow prereleases to be marked Latest.

Run the source-packaging step before the installer step so the source ZIP is
included in the checksum file. The source ZIP and GitHub's automatically
produced source archives serve the same purpose; the former is also covered by
our release checksum list.

## Release assets

Attach these files from `dist/` to the GitHub release:

- `IdleSentry-<version>-win-x64-Setup.exe`
- `IdleSentry-<version>-win-arm64-Setup.exe`
- `IdleSentry-<version>-win-x64-Portable.zip`
- `IdleSentry-<version>-win-arm64-Portable.zip`
- `IdleSentry-<version>-Windows-Source.zip`
- `SHA256SUMS.txt`

The CI workflow runs the development checks and publishes self-contained builds
as workflow artifacts. It does not install Inno Setup, sign binaries, or publish
GitHub releases automatically. Release packages are built with the scripts above.

## Installer behavior

The installer supports Windows 10 build 19041 and newer. It installs for the
current user under `%LOCALAPPDATA%\Programs\IdleSentry`, adds a Start menu shortcut,
and registers an uninstaller. A desktop shortcut is optional. .NET is included;
users do not need to install the SDK or runtime.

Quit the tray app before upgrading or uninstalling. Personal preferences are
retained after uninstall. A login-startup entry is removed only when it points
to the installed copy. Portable packages use the same local settings location.

Before a stable release, check installation, launch, discovery, idle closing,
activity reset, tray controls, login startup, upgrade, and uninstall on both x64
and ARM64 Windows machines. Automated checks and cross-compilation do not replace
these device tests.

## Signing

The current scripts produce unsigned binaries. Windows may show publisher or
reputation warnings. A future release can configure SignTool and Inno Setup with
an appropriate signing certificate and timestamp service. Keep private keys and
passwords out of Git. Checksums establish file integrity, not publisher identity.

References: [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases),
[Inno Setup](https://jrsoftware.org/isinfo.php).
