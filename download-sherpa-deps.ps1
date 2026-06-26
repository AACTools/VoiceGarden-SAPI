#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Download SherpaOnnx dependencies for VoiceGardenSAPIAdapter

.DESCRIPTION
    Downloads the required SherpaOnnx static libraries for building
    VoiceGardenSAPIAdapter. Supports x86 (32-bit), x64 (64-bit), and ARM64 builds.
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

# Script directory (should be VoiceGardenSAPIAdapter)
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

# Platform mappings - use shared libraries for dynamic linking
$PlatformConfigs = @{
    "x64" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-x64-shared-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-x64-shared-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-x64-shared"
    }
    "x86" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-x86-shared-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-x86-shared-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-x86-shared"
    }
    "ARM64" = @{
        UrlTemplate = "$GithubRelease/sherpa-onnx-$Version-win-arm64-shared-MT-{CONFIG}.tar.bz2"
        FileTemplate = "sherpa-onnx-$Version-win-arm64-shared-MT-{CONFIG}.tar.bz2"
        DirTemplate = "sherpa-onnx-$Version-win-arm64-shared"
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

        # Save the original Dir value for later use (gets modified during extraction)
        $OriginalDir = $Dir

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

            # Check if Invoke-WebRequest auto-decompressed the bz2 (leaves .tar file)
            $tarFile = $DestFile
            if ($DestFile -like "*.tar.bz2") {
                $possibleTar = $DestFile -replace '\.bz2$', ''
                if (Test-Path $possibleTar) {
                    $tarFile = $possibleTar
                }
            }

            # Extract using tar decompression
            Write-Host "  Extracting..." -ForegroundColor Cyan
            try {
                # Clean up any existing extraction directory first
                $possibleDirs = @(
                    (Join-Path $LibsDir "$Dir-MT-$BuildConfig"),
                    (Join-Path $LibsDir "$Dir-$BuildConfig"),
                    $ExtractDirWithConfig
                )
                foreach ($dir in $possibleDirs) {
                    if (Test-Path $dir) {
                        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
                    }
                }

                # Try 7-Zip first (it handles tar.bz2 natively)
                $sevenZipPaths = @(
                    "C:\Program Files\7-Zip\7z.exe",
                    "C:\Program Files (x86)\7-Zip\7z.exe"
                )
                $sevenZip = $null
                foreach ($path in $sevenZipPaths) {
                    if (Test-Path $path) {
                        $sevenZip = $path
                        break
                    }
                }

                if ($sevenZip) {
                    # 7-Zip needs two steps for tar.bz2: first extract bz2, then tar
                    if ($tarFile -like "*.tar.bz2") {
                        # First extract bz2 to tar
                        & $sevenZip x $tarFile "-o$LibsDir" -y | Out-Null
                        # Then extract the tar file
                        $intermediateTar = $tarFile -replace '\.bz2$', ''
                        if (Test-Path $intermediateTar) {
                            & $sevenZip x $intermediateTar "-o$LibsDir" -y | Out-Null
                            Remove-Item $intermediateTar -Force -ErrorAction SilentlyContinue
                        }
                    }
                    else {
                        # Just a tar file, extract directly
                        & $sevenZip x $tarFile "-o$LibsDir" -y | Out-Null
                    }
                }
                else {
                    # Fallback to Windows native tar (not Git Bash tar)
                    $winTar = "C:\Windows\System32\tar.exe"
                    if (Test-Path $winTar) {
                        & $winTar -xf $tarFile -C $LibsDir
                    }
                    else {
                        throw "No suitable extraction tool found (7-Zip or Windows tar)"
                    }
                }
            }
            catch {
                $errMsg = $_.Exception.Message
                Write-Host "  Warning: Extraction had issues: ${errMsg}" -ForegroundColor Yellow
            }

            # Clean up the tar file
            if (Test-Path $tarFile) {
                Remove-Item $tarFile -Force -ErrorAction SilentlyContinue
            }
            # Also clean up the original .tar.bz2 if it exists
            if (Test-Path $DestFile) {
                Remove-Item $DestFile -Force -ErrorAction SilentlyContinue
            }

            # The extracted directory will have -MT-Debug or -MT-Release suffix
            # Rename to keep directory names consistent with configuration suffix
            $extractedWithSuffix = Join-Path $LibsDir "$OriginalDir-MT-$BuildConfig"
            if (Test-Path $extractedWithSuffix) {
                try {
                    # Get contents instead of the folder itself to avoid nesting
                    $contents = Get-ChildItem -Path $extractedWithSuffix
                    if ($contents.Count -eq 1 -and $contents[0].PSIsContainer) {
                        # The contents are in a nested subdirectory, move those up
                        Move-Item -Path "$($contents[0].FullName)\*" -Destination $ExtractDirWithConfig -Force
                        Remove-Item $extractedWithSuffix -Recurse -Force
                    } else {
                        Move-Item -Path $extractedWithSuffix -Destination $ExtractDirWithConfig -Force -ErrorAction Stop
                    }
                } catch {
                    # Move may fail if nested directory exists, but structure is correct
                    if (!(Test-Path $ExtractDirWithConfig)) {
                        throw
                    }
                }
            }
            # Also check for directory without MT prefix (some releases use different naming)
            $extractedWithoutMT = Join-Path $LibsDir "$OriginalDir-$BuildConfig"
            if (Test-Path $extractedWithoutMT) {
                try {
                    Move-Item -Path $extractedWithoutMT -Destination $ExtractDirWithConfig -Force -ErrorAction Stop
                } catch {
                    # Move may fail if nested directory exists, but structure is correct
                    if (!(Test-Path $ExtractDirWithConfig)) {
                        throw
                    }
                }
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

            # Copy DLLs to the dlls directory for dynamic linking
            $DllsDir = Join-Path $ScriptDir "SherpaOnnx\dlls\$Platform\$BuildConfig"
            if (!(Test-Path $DllsDir)) {
                New-Item -ItemType Directory -Path $DllsDir -Force | Out-Null
            }
            Copy-Item -Path (Join-Path $ExtractDirWithConfig "lib\sherpa-onnx-c-api.dll") -Destination $DllsDir -Force
            Copy-Item -Path (Join-Path $ExtractDirWithConfig "lib\onnxruntime.dll") -Destination $DllsDir -Force
            Copy-Item -Path (Join-Path $ExtractDirWithConfig "lib\onnxruntime_providers_shared.dll") -Destination $DllsDir -Force
            Write-Host "  Copied DLLs to $DllsDir" -ForegroundColor DarkGray

            Write-Host "  Status: Complete" -ForegroundColor Green
            $SuccessCount++
        }
        catch {
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
            if (Test-Path $DestFile) {
                Remove-Item $DestFile -Force
            }
            # Also clean up tar file if it exists
            $tarFile = $DestFile -replace '\.bz2$', ''
            if (Test-Path $tarFile) {
                Remove-Item $tarFile -Force
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
    Write-Host "Dependencies ready! You can now build VoiceGardenSAPIAdapter." -ForegroundColor Green
    exit 0
}
else {
    Write-Host ""
    Write-Host "Some dependencies failed to download. Please check the errors above." -ForegroundColor Red
    exit 1
}
