#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Download SherpaOnnx dependencies for NaturalVoiceSAPIAdapter

.DESCRIPTION
    Downloads the required SherpaOnnx static libraries for building
    NaturalVoiceSAPIAdapter. Supports x86 (32-bit), x64 (64-bit), and ARM64 builds.

.PARAMETER Platforms
    Array of platforms to download. Valid values: "x64", "x86", "ARM64", "all"
    Default: "x64"

.PARAMETER Force
    Force re-download even if files already exist

.EXAMPLE
    .\download-sherpa-deps.ps1
    Downloads x64 dependencies only

.EXAMPLE
    .\download-sherpa-deps.ps1 -Platforms all
    Downloads x86, x64, and ARM64 dependencies

.EXAMPLE
    .\download-sherpa-deps.ps1 -Platforms x86,x64 -Force
    Re-downloads x86 and x64 platforms
#>

param(
    [ValidateSet("x64", "x86", "ARM64", "all")]
    [string[]]$Platforms = @("x64"),

    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Script directory (should be NaturalVoiceSAPIAdapter)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# SherpaOnnx libs are in the project directory: SherpaOnnx/libs
$LibsDir = Join-Path $ScriptDir "SherpaOnnx\libs"

# Create libs directory if it doesn't exist
if (!(Test-Path $LibsDir)) {
    New-Item -ItemType Directory -Path $LibsDir -Force | Out-Null
}

# SherpaOnnx version and base URL
$Version = "v1.12.23"
$BaseUrl = "https://sourceforge.net/projects/sherpa-onnx.mirror/files"

# Platform mappings - x86 uses -MT-Release suffix
$PlatformConfigs = @{
    "x64" = @{
        Url = "$BaseUrl/$Version/sherpa-onnx-$Version-win-x64-static.tar.bz2/download"
        File = "sherpa-onnx-$Version-win-x64-static.tar.bz2"
        Dir = "sherpa-onnx-$Version-win-x64-static"
    }
    "x86" = @{
        Url = "$BaseUrl/$Version/sherpa-onnx-$Version-win-x86-static-MT-Release.tar.bz2/download"
        File = "sherpa-onnx-$Version-win-x86-static-MT-Release.tar.bz2"
        Dir = "sherpa-onnx-$Version-win-x86-static"
    }
    "ARM64" = @{
        Url = "$BaseUrl/$Version/sherpa-onnx-$Version-win-arm64-static.tar.bz2/download"
        File = "sherpa-onnx-$Version-win-arm64-static.tar.bz2"
        Dir = "sherpa-onnx-$Version-win-arm64-static"
    }
}

# Expand "all" to all platforms
if ($Platforms -contains "all") {
    $Platforms = @("x64", "x86", "ARM64")
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SherpaOnnx Dependencies Downloader" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$SuccessCount = 0
$TotalCount = $Platforms.Count

foreach ($Platform in $Platforms) {
    $Config = $PlatformConfigs[$Platform]
    $DestFile = Join-Path $LibsDir $Config.File
    $ExtractDir = Join-Path $LibsDir $Config.Dir

    Write-Host "[$Platform]" -ForegroundColor Yellow
    Write-Host "  URL: $($Config.Url)"
    Write-Host "  File: $($Config.File)"

    # Check if already downloaded
    if ((Test-Path $DestFile) -and !$Force) {
        $FileSize = (Get-Item $DestFile).Length / 1MB
        Write-Host "  Status: Already exists ($([math]::Round($FileSize, 2)) MB)" -ForegroundColor Green
        $SuccessCount++
        continue
    }

    # Check if already extracted
    if ((Test-Path $ExtractDir) -and !$Force) {
        Write-Host "  Status: Already extracted" -ForegroundColor Green
        $SuccessCount++
        continue
    }

    try {
        # Download
        Write-Host "  Downloading..." -ForegroundColor Cyan
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $Config.Url -OutFile $DestFile -UseBasicParsing

        $DownloadedSize = (Get-Item $DestFile).Length / 1MB
        Write-Host "  Downloaded: $([math]::Round($DownloadedSize, 2)) MB" -ForegroundColor Green

        # Extract
        Write-Host "  Extracting..." -ForegroundColor Cyan
        tar -xjf $DestFile -C $LibsDir

        # For x86, rename the extracted directory to remove -MT-Release suffix
        # This keeps directory names consistent: sherpa-onnx-v1.12.23-win-x86-static
        if ($Platform -eq "x86") {
            $extractedDir = Join-Path $LibsDir "sherpa-onnx-$Version-win-x86-static-MT-Release"
            if (Test-Path $extractedDir) {
                Move-Item -Path $extractedDir -Destination (Join-Path $LibsDir $Config.Dir) -Force
            }
        }

        Write-Host "  Status: Complete" -ForegroundColor Green
        $SuccessCount++
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        if (Test-Path $DestFile) {
            Remove-Item $DestFile -Force
        }
    }

    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary: $SuccessCount/$TotalCount platforms completed" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($SuccessCount -eq $TotalCount) {
    Write-Host ""
    Write-Host "Dependencies ready! You can now build NaturalVoiceSAPIAdapter." -ForegroundColor Green
    exit 0
}
else {
    Write-Host ""
    Write-Host "Some dependencies failed to download. Please check the errors above." -ForegroundColor Red
    exit 1
}
