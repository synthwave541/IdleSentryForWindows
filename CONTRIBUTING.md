# Contributing

Thank you for helping improve IdleSentry for Windows.

## Report an issue

Use the repository's Issues page. Include the app version, Windows version, PC
architecture (x64 or ARM64), reproduction steps, and expected versus observed
behavior. For app-discovery or closing issues, include the affected app's name
and whether it runs with administrator privileges.

Remove personal paths and sensitive content from screenshots, logs, and settings
before sharing them. Do not upload your real settings file by default.

## Develop and verify

Install .NET 8 SDK on Windows. Build and run checks from the repository root:

```powershell
dotnet run --project './IdleSentry.ChooserTests/IdleSentry.ChooserTests.csproj' -c Release
& './IdleSentry for Windows/build.ps1' -Arch all
```

The checks use temporary fixtures and sample application state for presentation
tests. They do not replace manual validation of app closing, tray behavior,
startup registration, or installation on a real PC.

Use disposable apps with no unsaved work when testing termination. Check small
window sizes, long app names, Windows accent changes, keyboard navigation, and
disabled animations when making UI changes.

## Submit a change

Keep pull requests focused. Describe the problem, what changes for users, and how
you verified the result. Include a screenshot for a visible UI change, using
sample data. Include relevant automated checks for behavior changes.

Commit source, tests, documentation, and installer scripts. Keep build output,
personal settings, signing certificates, and credentials out of commits. See
[publishing instructions](IdleSentry%20for%20Windows/PUBLISHING.md) for release
packaging.
