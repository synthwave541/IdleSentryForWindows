<#
.SYNOPSIS
Build the native Microsoft Store channel, independent of Velopack releases.
.DESCRIPTION
Stages source on a local disk so UNC checkouts work. Outputs unsigned MSIX packages
and a bundle for Partner Center. Local testing uses separate test-signed copies.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64', 'all')][string]$Arch = 'all',
    [string]$Version = '1.0.17.0',
    [string]$OutputDirectory,
    [string]$WindowsSdkVersion = '10.0.26100.0'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+\.0$') {
    throw 'Use a four-part Store version with a nonzero major and a final revision of 0.'
}
$parsedVersion = [version]$Version
if ($parsedVersion.Major -gt 65535 -or $parsedVersion.Minor -gt 65535 -or $parsedVersion.Build -gt 65535) {
    throw 'Store version components must be at most 65535.'
}
$buildId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
if (!$OutputDirectory) { $OutputDirectory = Join-Path $env:LOCALAPPDATA "IdleSentryStoreBuilds\$buildId" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.StartsWith('\\')) { throw 'Choose a local output directory; canonical source remains in the shared checkout.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'OutputDirectory must be new to prevent stale payloads or overwritten artifacts.' }
$nativeArch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$makeappx = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$WindowsSdkVersion\$nativeArch\makeappx.exe"
if (!(Test-Path -LiteralPath $makeappx)) { throw "Windows SDK MakeAppx not found: $makeappx" }
$makepri = Join-Path (Split-Path -Parent $makeappx) 'makepri.exe'
if (!(Test-Path -LiteralPath $makepri)) { throw "Windows SDK MakePri not found: $makepri" }
$null = Get-Command dotnet -ErrorAction Stop
$source = Join-Path $OutputDirectory 'source'
$packages = Join-Path $OutputDirectory 'packages'
$null = New-Item -ItemType Directory -Path $source, $packages -Force
# The WPF project has top-level sources/resources; do not copy old bin/obj/publish trees.
Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object { $_.Extension -in '.cs', '.xaml', '.csproj', '.ico', '.png' } |
    Copy-Item -Destination $source
$repoRoot = Split-Path -Parent $PSScriptRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'LICENSE')
$manifestTemplate = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'store\AppxManifest.xml') -Raw
Add-Type -AssemblyName System.Drawing
$targets = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }
function Invoke-MakeAppx([string[]]$Arguments, [string]$LogName) {
    $log = Join-Path $OutputDirectory $LogName
    & $makeappx @Arguments *> $log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $log -Tail 30 | Write-Host
        throw "MSIX validation/packing failed. See $log"
    }
}
foreach ($target in $targets) {
    $payload = Join-Path $OutputDirectory "payload-$target"
    & dotnet publish (Join-Path $source 'IdleSentry.csproj') -c Release -r "win-$target" --self-contained true `
        '-p:DistributionChannel=Store' '-p:PublishSingleFile=false' '-p:IncludeNativeLibrariesForSelfExtract=false' `
        '-p:EnableCompressionInSingleFile=false' "-p:Version=$($parsedVersion.ToString(3))" -o $payload
    if ($LASTEXITCODE -ne 0) { throw "Store publish failed for $target." }
    if (Get-ChildItem -LiteralPath $payload -Filter '*Velopack*') { throw 'A Store payload must not include Velopack.' }
    Get-ChildItem -LiteralPath $payload -Filter '*.pdb' | Remove-Item
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $payload
    $assets = Join-Path $payload 'Assets'
    $null = New-Item -ItemType Directory -Path $assets
    $image = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'IdleSentry.png'))
    try {
        $iconAssets = @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))
        # Windows uses target-size, unplated resources in the taskbar and Alt+Tab.
        # BackgroundColor="transparent" alone still permits an accent-color plate.
        foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 128, 256)) {
            $iconAssets += ,@("Square44x44Logo.targetsize-$size", $size)
            $iconAssets += ,@("Square44x44Logo.targetsize-${size}_altform-unplated", $size)
            $iconAssets += ,@("Square44x44Logo.targetsize-${size}_altform-lightunplated", $size)
        }
        foreach ($asset in $iconAssets) {
            $size = [int]$asset[1]
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $scale = [Math]::Min($size / $image.Width, $size / $image.Height)
                $w = [int]($image.Width * $scale); $h = [int]($image.Height * $scale)
                $graphics.DrawImage($image, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
                $bitmap.Save((Join-Path $assets ($asset[0] + '.png')), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $image.Dispose() }
    $manifest = $manifestTemplate.Replace('@VERSION@', $Version).Replace('@ARCH@', $target)
    [IO.File]::WriteAllText((Join-Path $payload 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
    # Qualifier filenames must be indexed; merely adding PNGs does not make them
    # available to the shell's resource selection.
    $priConfig = Join-Path $OutputDirectory "priconfig-$target.xml"
    & $makepri createconfig /cf $priConfig /dq en-US /o *> (Join-Path $OutputDirectory "makepri-config-$target.log")
    if ($LASTEXITCODE -ne 0) { throw "PRI configuration failed for $target." }
    # Index shell assets only. .NET satellite DLLs are resolved by .NET, not MRT;
    # indexing them would create unrelated language PRI files and fallback warnings.
    $priInput = Join-Path $OutputDirectory "pri-input-$target"
    $null = New-Item -ItemType Directory -Path $priInput
    Copy-Item -LiteralPath $assets -Destination $priInput -Recurse
    & $makepri new /pr $priInput /mn (Join-Path $payload 'AppxManifest.xml') /cf $priConfig /of (Join-Path $payload 'resources.pri') /o *> (Join-Path $OutputDirectory "makepri-$target.log")
    if ($LASTEXITCODE -ne 0) { throw "PRI generation failed for $target." }
    $package = Join-Path $packages "IdleSentry_${Version}_$target.msix"
    Invoke-MakeAppx -Arguments @('pack', '/d', $payload, '/p', $package, '/h', 'SHA256', '/o') -LogName "makeappx-$target.log"
}
$bundle = Join-Path $OutputDirectory "IdleSentry_${Version}_$Arch.msixbundle"
Invoke-MakeAppx -Arguments @('bundle', '/d', $packages, '/p', $bundle, '/bv', $Version, '/o') -LogName 'makeappx-bundle.log'
Write-Host "Store bundle: $bundle"
Write-Host 'Unsigned submission artifacts created. Build success does not establish certification readiness.'
