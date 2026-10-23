# Publishing IdleSentry for Windows

The Windows source repository is [synthwave541/IdleSentryForWindows](https://github.com/synthwave541/IdleSentryForWindows). The macOS app has its own repository and release process.

## Standalone GitHub releases

Standalone builds use .NET 8 and Velopack 1.2.161. Keep the package ID `Zennoris.IdleSentry` and the `win-x64` / `win-arm64` channels unchanged so existing installations can upgrade.

1. Increase `<Version>` in `IdleSentry.csproj`, update `CHANGELOG.md`, and review the source diff.
2. Run `dotnet run --project './IdleSentry.ChooserTests/IdleSentry.ChooserTests.csproj' -c Release` from the repository root. Verify the actual Windows interface and closing behavior using disposable apps.
3. Commit and push the reviewed source. Wait for the Windows build workflow to pass. It runs discovery, presentation and updater checks, builds both architectures, packages installers/feeds/source, and smoke-tests the native x64 installer and lifecycle hooks.
4. Prefer the workflow's `IdleSentry-Windows-release` artifact for publication, so the binaries and source archive correspond to the published commit. For local packaging, run `build.ps1 -Arch all`, then `build-installer.ps1 -Arch all -SkipBuild` and `package-source.ps1` from this directory. Stage new source files in Git before creating the source archive; the script uses the Git index as its file manifest.
5. Verify both update feeds, package identities, versions, architectures, file sizes, hashes and source archive contents. Regenerate `SHA256SUMS.txt` after the source archive is present. The workflow does this automatically.
6. Tag the verified commit `v<version>`. Create a draft GitHub release and upload the complete asset set below. Verify the uploaded asset sizes and SHA-256 digests before publishing it as the latest stable release.
7. Verify the public latest-release endpoint and both public updater feeds after publication. Update website download links separately when ready.

Publish both architectures together. The complete asset set is:

- `IdleSentry-<version>-win-x64-Setup.exe`
- `IdleSentry-<version>-win-arm64-Setup.exe`
- Both `Zennoris.IdleSentry-<version>-win-*-full.nupkg` packages
- Both `releases.win-*.json` feeds
- Both `assets.win-*.json` manifests and `RELEASES-win-*` compatibility feeds
- `IdleSentry-<version>-Windows-Source.zip`
- `SHA256SUMS.txt`

Preserve the generated package and feed filenames. New portable ZIPs are not published. Users install Setup.exe; updater packages and feeds are consumed by the app.

Existing standalone 1.0.15+ installations can check from About or the tray menu, download the update, then choose Install & Restart. Downloads do not install silently on launch. Preferences stay in `%LOCALAPPDATA%\IdleSentry`. The legacy 1.0.14 installer needs a one-time manual uninstall/reinstall; its uninstaller preserves preferences.

Local update rehearsals validate package discovery, integrity, application replacement, restart and preference preservation. Emulated x64 testing is not physical x64 testing; native x64 CI provides additional installer coverage. Public GitHub delivery can be verified only after publication.

## Microsoft Store packages

The Store build is a separate distribution channel. It uses package-owned settings, Windows startup tasks and Microsoft Store updates, with no Velopack dependency.

The Windows build workflow also produces an `IdleSentry-Microsoft-Store` artifact containing the unsigned combined bundle and its SHA-256 hash. This allows package compilation without consuming local VM storage. For local builds, run `build-store.ps1 -Arch all -Version <major.minor.patch.0>` on Windows with .NET 8 and Windows SDK 10.0.26100.0. Use a fresh local output directory. The script builds both architectures, generates shell assets and a PRI resource index, and runs MakeAppx validation for each MSIX and the combined bundle.

Upload the resulting unsigned `IdleSentry_<version>_all.msixbundle` to the existing IdleSentry product in Partner Center. Keep the reserved package name, publisher and startup-task identity unchanged. Leave the fourth version component as zero. Do not upload a standalone Setup.exe, a Velopack package, a test-signed package or private signing material to this MSIX submission.

Create an update submission, replace the previous package bundle, check that x64 and ARM64 validate at the new version, update the Store's release notes, and review submission options before submitting for certification. Microsoft signs approved Store packages. MakeAppx success alone is not Store certification or a Windows App Certification Kit pass.

See [Microsoft's upload instructions](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages) and [certification process](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-certification-process).

## Source and signing material

Commit source, tests, assets, dependency notices and build scripts. Keep `bin/`, `obj/`, `publish/`, `dist/`, personal settings, local support notes and signing credentials out of source commits and archives.

The standalone installers are unsigned and can show Windows SmartScreen reputation warnings. Checksums establish file integrity, not publisher identity. Store package signing is handled separately by Microsoft during publication.
