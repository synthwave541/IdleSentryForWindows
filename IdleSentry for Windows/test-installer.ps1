# Native x64 smoke test; run only on an ephemeral Windows CI runner.
$ErrorActionPreference = 'Stop'
if (-not $env:RUNNER_TEMP) { throw 'This test requires an ephemeral GitHub Actions runner.' }
[xml]$project = Get-Content (Join-Path $PSScriptRoot 'IdleSentry.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$root = Join-Path $env:RUNNER_TEMP ("IdleSentry install smoke " + [guid]::NewGuid().ToString('N'))
$setup = Join-Path $PSScriptRoot "dist\IdleSentry-$version-win-x64-Setup.exe"
$marker = Join-Path $env:LOCALAPPDATA 'IdleSentry\installer-smoke-marker.txt'
New-Item (Split-Path $marker) -ItemType Directory -Force | Out-Null
Set-Content $marker 'preferences outside installation survive'
function Run-And-Wait([string]$file, [string[]]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Process failed: $file ($($process.ExitCode))" }
}
Run-And-Wait $setup @('--silent', '--installto', "`"$root`"")
$exe = Join-Path $root 'current\IdleSentry.exe'
if ((Get-Item $exe).VersionInfo.ProductVersion.Split('+')[0] -ne $version) { throw 'Wrong installed version.' }
if (-not (Test-Path (Join-Path $root 'IdleSentry.exe'))) { throw 'Stable launch stub is missing.' }
# Exercise the real fast-exit hook: this must finish before WPF, monitoring or the mutex.
Run-And-Wait $exe @('--veloapp-updated', $version)
if (Get-Process IdleSentry -ErrorAction SilentlyContinue) { throw 'An installer hook started monitoring.' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty $runKey -Name IdleSentry -Value "`"$(Join-Path $root 'IdleSentry.exe')`" --background" -PropertyType String -Force | Out-Null
Run-And-Wait (Join-Path $root 'Update.exe') @('--silent', 'uninstall')
if (Test-Path $exe) { throw 'Uninstall left the current application installed.' }
if ((Get-ItemProperty $runKey -Name IdleSentry -ErrorAction SilentlyContinue).IdleSentry) { throw 'Uninstall left an owned login startup entry.' }
if ((Get-Content $marker) -ne 'preferences outside installation survive') { throw 'Uninstall removed external preferences.' }
Remove-Item $marker
Write-Host 'PASS: native install, stable launch stub, fast update hook, uninstall, startup cleanup and preference preservation'
