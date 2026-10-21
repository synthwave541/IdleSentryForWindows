<# Builds Velopack installers and architecture-specific GitHub update feeds. #>
param(
    [ValidateSet('x64', 'arm64', 'all')][string]$Arch = 'all',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $projectDir 'build.ps1') -Arch $Arch }
[xml]$project = Get-Content -LiteralPath (Join-Path $projectDir 'IdleSentry.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$targets = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }
$distDir = Join-Path $projectDir 'dist'
$toolDir = Join-Path $projectDir 'obj\release-tools'
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
if (-not (Test-Path (Join-Path $toolDir 'vpk.exe'))) {
    & dotnet tool install vpk --version 1.2.161 --tool-path $toolDir
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the pinned Velopack CLI.' }
}
foreach ($target in $targets) {
    $publishDir = Join-Path $projectDir "publish\win-$target"
    $executable = Join-Path $publishDir 'IdleSentry.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing published executable: $executable" }
    $publishedVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion.Split('+')[0]
    if ($publishedVersion -ne $version) { throw "Published $target version does not match $version. Run build.ps1 first." }
    Copy-Item (Join-Path $projectDir 'LICENSE'), (Join-Path $projectDir 'THIRD-PARTY-NOTICES.txt') -Destination $publishDir -Force
    # A distinct package ID keeps settings in %LOCALAPPDATA%\IdleSentry outside the installation tree.
    & (Join-Path $toolDir 'vpk.exe') pack --packId Zennoris.IdleSentry --packVersion $version `
        --packDir $publishDir --mainExe IdleSentry.exe --packTitle IdleSentry --packAuthors Zennoris `
        --runtime "win-$target" --channel "win-$target" --icon (Join-Path $projectDir 'IdleSentry.ico') `
        --delta None --outputDir $distDir --skip-updates --yes
    if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed for $target." }
    $setup = Get-ChildItem $distDir -Filter "*win-$target*Setup.exe" | Select-Object -First 1
    if (-not $setup) { throw "Missing $target Setup." }
    Move-Item -LiteralPath $setup.FullName -Destination (Join-Path $distDir "IdleSentry-$version-win-$target-Setup.exe") -Force
    # Keep the framework's package and releases JSON filenames exactly as generated.
    # Portable archives are intentionally omitted: users should install Setup for updates and login startup.
    Get-ChildItem $distDir -Filter "*win-$target*Portable.zip" | Remove-Item
}
$assets = Get-ChildItem -LiteralPath $distDir -File | Where-Object { $_.Extension -in '.exe', '.zip', '.nupkg', '.json' }
$hashes = foreach ($asset in $assets) { "$((Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($asset.Name)" }
[IO.File]::WriteAllLines((Join-Path $distDir 'SHA256SUMS.txt'), [string[]]$hashes)
Write-Host "Installers and update feeds: $distDir"
