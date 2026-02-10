#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Download SherpaOnnx dependencies for NaturalVoiceSAPIAdapter

.DESCRIPTION
    Downloads the required SherpaOnnx static libraries for building
    NaturalVoiceSAPIAdapter. Supports x86 (32-bit), x64 (64-bit), and ARM64 builds.
    Supports both Debug and Release configurations.

    Downloads from official GitHub releases at:
    https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.12.23

.PARAMETER Platforms
    Array of platforms to download. Valid values: "x64", "x86", "ARM64", "all"
    Default: "x64"

.PARAMETER Configuration
    Build configuration to download. Valid values: "Debug", "Release", "all"
    Default: "Release"

    Use "all" to download both Debug and Release for local development.

.PARAMETER Force
    Force re-download even if files already exist

.EXAMPLE
    .\download-sherpa-deps.ps1
    Downloads x64 Release dependencies only (for CI/CD)

.EXAMPLE
    .\download-sherpa-deps.ps1 -Platforms all -Configuration all
    Downloads Debug and Release for all platforms (for local development)

.EXAMPLE
    .\download-sherpa-deps.ps1 -Platforms x86,x64 -Configuration Debug
    Downloads Debug configuration for x86 and x64 only
#>

param(
    [ValidateSet("x64", "x86", "ARM64", "all")]
    [string[]]$Platforms = @("x64"),

    [ValidateSet("Debug", "Release", "all")]
    [string[]]$Configuration = @("Release"),

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

# SherpaOnnx version and GitHub release URL
$Version = "v1.12.23"
$GithubRelease = "https://github.com/k2-fsa/sherpa-onnx/releases/download/$Version"

# Platform mappings - all use -MT prefix
$PlatformConfigs = @{
    "x64" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-x64-static-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-x64-static-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-x64-static"
    }
    "x86" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-x86-static-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-x86-static-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-x86-static"
    }
    "ARM64" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-arm64-static-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-arm64-static-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-arm64-static"
    }
}

# Expand "all" platforms
if ($Platforms -contains "all") {
    $Platforms = @("x64", "x86", "ARM64")
}

# Expand "all" configurations
if ($Configuration -contains "all") {
    $Configuration = @("Debug", "Release")
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SherpaOnnx Dependencies Downloader" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow
Write-Host "Configuration(s): $($Configuration -join ', ')" -ForegroundColor Yellow
Write-Host "Downloading from: https://github.com/k2-fsa/sherpa-onnx/releases/tag/$Version" -ForegroundColor Yellow
Write-Host ""

$SuccessCount = 0
$TotalCount = $Platforms.Count * $Configuration.Count

foreach ($Platform in $Platforms) {
    $Config = $PlatformConfigs[$Platform]

    foreach ($BuildConfig in $Configuration) {
        $Url = $Config.UrlTemplate -replace "\{CONFIG\}", $BuildConfig
        $File = $Config.FileTemplate -replace "\{CONFIG\}", $BuildConfig
        $Dir = $Config.DirTemplate

        $DestFile = Join-Path $LibsDir $File
        $ExtractDir = Join-Path $LibsDir $Dir
        $ExtractDirWithConfig = Join-Path $LibsDir "$Dir-$BuildConfig"

        Write-Host "[$Platform / $BuildConfig]" -ForegroundColor Yellow
        Write-Host "  URL: $Url"
        Write-Host "  File: $File"

        # Check if already downloaded
        if ((Test-Path $DestFile) -and !$Force) {
            $FileSize = (Get-Item $DestFile).Length / 1MB
            Write-Host "  Status: Already exists ($([math]::Round($FileSize, 2)) MB)" -ForegroundColor Green
            $SuccessCount++
            continue
        }

        # Check if already extracted
        if ((Test-Path $ExtractDirWithConfig) -and !$Force) {
            Write-Host "  Status: Already extracted" -ForegroundColor Green
            $SuccessCount++
            continue
        }

        try {
            # Download
            Write-Host "  Downloading..." -ForegroundColor Cyan
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $Url -OutFile $DestFile -UseBasicParsing

            $DownloadedSize = (Get-Item $DestFile).Length / 1MB
            Write-Host "  Downloaded: $([math]::Round($DownloadedSize, 2)) MB" -ForegroundColor Green

            # Extract
            Write-Host "  Extracting..." -ForegroundColor Cyan
            tar -xjf $DestFile -C $LibsDir

            # The extracted directory will have -MT-Debug or -MT-Release suffix
            # Rename to keep directory names consistent with configuration suffix
            $extractedWithSuffix = Join-Path $LibsDir "$Dir-MT-$BuildConfig"
            if (Test-Path $extractedWithSuffix) {
                Move-Item -Path $extractedWithSuffix -Destination $ExtractDirWithConfig -Force
            }

            # Create a symlink or copy for the base directory (for backward compatibility)
            # If only one config exists, use that. Otherwise, prefer Release.
            if ($BuildConfig -eq "Release") {
                $BaseDir = Join-Path $LibsDir $Dir
                if (Test-Path $BaseDir) {
                    Remove-Item $BaseDir -Force -Recurse
                }
                # Create a junction (symbolic link for directories)
                cmd /c mklink /J "$BaseDir" "$ExtractDirWithConfig" | Out-Null
                Write-Host "  Created junction for backward compatibility" -ForegroundColor DarkGray
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
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary: $SuccessCount/$TotalCount downloads completed" -ForegroundColor Cyan
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
