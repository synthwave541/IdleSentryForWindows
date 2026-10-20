@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo   IdleSentry for Windows - Quick Build
echo ========================================
echo.

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo Error: 'dotnet' command not found.
    echo Please ensure .NET 8 SDK is installed and added to your PATH.
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo Building IdleSentry Release...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Arch all -Configuration Release
if %ERRORLEVEL% neq 0 (
    echo Build failed.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build successful! Binaries are in publish\ folder.
pause
