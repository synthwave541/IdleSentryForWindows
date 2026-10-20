<# Builds per-user installers and portable ZIPs for GitHub Releases. #>
param(
    [ValidateSet('x64', 'arm64', 'all')][string]$Arch = 'all',
    [string]$CompilerPath,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerCandidates = @(
        $(if ($compilerCommand) { $compilerCommand.Source }),
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
    )
    $CompilerPath = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.3+ from https://jrsoftware.org/isdl.php, or supply -CompilerPath pointing to ISCC.exe.'
}
if (-not $SkipBuild) {
    & (Join-Path $projectDir 'build.ps1') -Arch $Arch
}
[xml]$project = Get-Content -LiteralPath (Join-Path $projectDir 'IdleSentry.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$targets = if ($Arch -eq 'all') { @('x64', 'arm64') } else { @($Arch) }
$distDir = Join-Path $projectDir 'dist'
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
foreach ($target in $targets) {
    $executable = Join-Path $projectDir "publish\win-$target\IdleSentry.exe"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing published executable: $executable" }
    if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -ne $version) {
        throw "Published $target version does not match $version. Run build.ps1 first."
    }
    & $CompilerPath "/DAppVersion=$version" "/DTargetArch=$target" "/O$distDir" (Join-Path $projectDir 'installer\IdleSentry.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $target." }
    Compress-Archive -LiteralPath @($executable, (Join-Path $projectDir 'LICENSE'), (Join-Path $projectDir 'THIRD-PARTY-NOTICES.txt')) `
        -DestinationPath (Join-Path $distDir "IdleSentry-$version-win-$target-Portable.zip") -Force
}
$assets = Get-ChildItem -LiteralPath $distDir -File | Where-Object { $_.Name -like "IdleSentry-$version-*" -and $_.Extension -in '.exe', '.zip' }
$hashes = foreach ($asset in $assets) { "$((Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($asset.Name)" }
[IO.File]::WriteAllLines((Join-Path $distDir 'SHA256SUMS.txt'), [string[]]$hashes)
Write-Host "Installers and portable ZIPs: $distDir"
