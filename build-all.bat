@echo off
REM Wrapper script for build-all.ps1
REM For users who prefer cmd or have PowerShell execution policy restrictions

setlocal

echo ========================================
echo NaturalVoiceSAPIAdapter Local Build
echo ========================================
echo.

REM Check if PowerShell is available
where powershell >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Error: PowerShell not found!
    echo Please install PowerShell or run build-all.ps1 manually.
    pause
    exit /b 1
)

REM Pass all arguments to the PowerShell script
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0build-all.ps1" %*

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build completed successfully!
pause
