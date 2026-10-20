<# Creates a clean standalone Windows repository ZIP, without generated binaries or the macOS checkout. #>
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$testsDir = Join-Path (Split-Path -Parent $projectDir) 'IdleSentry.ChooserTests'
if (-not (Test-Path -LiteralPath $testsDir -PathType Container)) { throw 'The sibling IdleSentry.ChooserTests directory is required.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $projectDir 'IdleSentry.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$stageDir = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('IdleSentry-source-' + [guid]::NewGuid().ToString('N'))))
try {
    $sourceProject = Join-Path $stageDir 'IdleSentry for Windows'
    $sourceTests = Join-Path $stageDir 'IdleSentry.ChooserTests'
    New-Item -ItemType Directory -Path $sourceProject,$sourceTests -Force | Out-Null
    Get-ChildItem -LiteralPath $projectDir -File -Force | Where-Object {
        $_.Extension -in '.cs', '.xaml', '.csproj', '.ps1', '.bat', '.md', '.ico', '.png', '.txt' -or $_.Name -in 'LICENSE', '.gitignore'
    } | Copy-Item -Destination $sourceProject
    Copy-Item -LiteralPath (Join-Path $projectDir 'installer') -Destination $sourceProject -Recurse
    Get-ChildItem -LiteralPath $testsDir -File | Where-Object { $_.Extension -in '.cs', '.csproj' } | Copy-Item -Destination $sourceTests
    $repositoryDir = Split-Path -Parent $projectDir
    foreach ($name in @('README.md', 'LICENSE', '.gitignore', '.gitattributes', 'CONTRIBUTING.md')) {
        Copy-Item -LiteralPath (Join-Path $repositoryDir $name) -Destination $stageDir
    }
    foreach ($directory in @('docs', '.github')) {
        Copy-Item -LiteralPath (Join-Path $repositoryDir $directory) -Destination $stageDir -Recurse
    }
    $distDir = Join-Path $projectDir 'dist'
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    $archive = Join-Path $distDir "IdleSentry-$version-Windows-Source.zip"
    Get-ChildItem -LiteralPath $stageDir -Force | Compress-Archive -DestinationPath $archive -Force
    Write-Host $archive
}
finally {
    # Delete only the unique staging directory created directly under the system temp directory.
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ((Split-Path -Parent $stageDir).TrimEnd('\') -ne $tempParent -or (Split-Path -Leaf $stageDir) -notlike 'IdleSentry-source-*') {
        throw "Refusing to remove unexpected staging path: $stageDir"
    }
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
}
