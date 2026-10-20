<#
.SYNOPSIS
    Build and publish script for IdleSentry for Windows.
.DESCRIPTION
    Compiles and publishes IdleSentry for Windows with support for both x64 and ARM64.
#>

param(
    [ValidateSet("x64", "arm64", "all")]
    [string]$Arch = "x64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $ScriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  IdleSentry for Windows Build Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check for dotnet
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Error "The .NET SDK was not found in PATH. Please install .NET 8 SDK or add it to PATH."
    exit 1
}

$targets = @()
if ($Arch -eq "all") {
    $targets = @("win-x64", "win-arm64")
} elseif ($Arch -eq "arm64") {
    $targets = @("win-arm64")
} else {
    $targets = @("win-x64")
}

foreach ($rid in $targets) {
    Write-Host "`n--> Publishing for $rid ($Configuration, SelfContained: $SelfContained)..." -ForegroundColor Yellow

    $publishArgs = @(
        "publish",
        "IdleSentry.csproj",
        "-c", $Configuration,
        "-r", $rid,
        "/p:PublishSingleFile=true",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:EnableCompressionInSingleFile=true"
    )

    if ($SelfContained) {
        $publishArgs += "--self-contained"
        $publishArgs += "true"
    } else {
        $publishArgs += "--self-contained"
        $publishArgs += "false"
    }

    & dotnet @publishArgs

    if ($LASTEXITCODE -eq 0) {
        $srcDir = Join-Path $ScriptDir "bin\$Configuration\net8.0-windows\$rid\publish"
        $destDir = Join-Path $ScriptDir "publish\$rid"

        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }

        # Close running instance if any to prevent file lock
        Get-Process -Name "IdleSentry" -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$destDir*" } | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 200

        Copy-Item -Path "$srcDir\*" -Destination $destDir -Recurse -Force
        Write-Host "--> Successfully published $rid to: $destDir" -ForegroundColor Green
    } else {
        Write-Error "--> Failed to publish for $rid."
    }
}

Write-Host "`nAll builds completed successfully!" -ForegroundColor Green
