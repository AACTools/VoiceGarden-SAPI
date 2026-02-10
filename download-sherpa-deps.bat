@echo off
REM Wrapper script for download-sherpa-deps.ps1
REM For users who prefer cmd or have PowerShell execution policy restrictions

setlocal

echo ========================================
echo SherpaOnnx Dependencies Downloader
echo ========================================
echo.

REM Check if PowerShell is available
where powershell >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Error: PowerShell not found!
    echo Please install PowerShell or run download-sherpa-deps.ps1 manually.
    pause
    exit /b 1
)

REM Pass all arguments to the PowerShell script
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0download-sherpa-deps.ps1" %*

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Download failed with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Press any key to exit...
pause >nul
