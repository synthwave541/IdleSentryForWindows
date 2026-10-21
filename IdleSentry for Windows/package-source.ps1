<# Creates a clean Windows source ZIP without local handoffs or generated binaries. #>
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$repositoryDir = Split-Path -Parent $projectDir
[xml]$project = Get-Content -LiteralPath (Join-Path $projectDir 'IdleSentry.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$stageDir = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('IdleSentry-source-' + [guid]::NewGuid().ToString('N'))))
try {
    # The Git index supplies the complete source manifest, excluding local support notes.
    $trackedPaths = & git -C $repositoryDir ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tracked source files.' }
    foreach ($relativePath in $trackedPaths) {
        if ($relativePath -like 'IdleSentry/*') { continue }
        $sourcePath = Join-Path $repositoryDir $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { continue }
        $destination = Join-Path $stageDir $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destination
    }
    $distDir = Join-Path $projectDir 'dist'
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    $archive = Join-Path $distDir "IdleSentry-$version-Windows-Source.zip"
    Get-ChildItem -LiteralPath $stageDir -Force | Compress-Archive -DestinationPath $archive -Force
    Write-Host $archive
}
finally {
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ((Split-Path -Parent $stageDir).TrimEnd('\') -ne $tempParent -or (Split-Path -Leaf $stageDir) -notlike 'IdleSentry-source-*') {
        throw "Refusing to remove unexpected staging path: $stageDir"
    }
    if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
}