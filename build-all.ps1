#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complete local build script for NaturalVoiceSAPIAdapter

.DESCRIPTION
    Builds all components of NaturalVoiceSAPIAdapter locally, including:
    - SherpaOnnx dependencies download
    - NaturalVoiceSAPIAdapter DLL (main SAPI adapter)
    - SherpaOnnxConfig (Model Manager)
    - Installer

    This replicates what the GitHub Actions CI/CD does, but locally.

.PARAMETER Configuration
    Build configuration: "Debug" or "Release"
    Default: "Release"

.PARAMETER Platforms
    Platforms to build: "x64", "x86", "ARM64", or "all"
    Default: "x64"

.PARAMETER SkipSherpaDeps
    Skip downloading SherpaOnnx dependencies (useful if already downloaded)

.PARAMETER SkipSubmodules
    Skip initializing/updating submodules

.EXAMPLE
    .\build-all.ps1
    Build x64 Release (most common)

.EXAMPLE
    .\build-all.ps1 -Configuration Debug -Platforms all
    Build Debug for all platforms (full development build)

.EXAMPLE
    .\build-all.ps1 -Configuration Release -Platforms x86,x64
    Build Release for x86 and x64
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86", "ARM64", "all")]
    [string[]]$Platforms = @("x64"),

    [switch]$SkipSherpaDeps,

    [switch]$SkipSubmodules
)

$ErrorActionPreference = "Stop"

# Script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path $ScriptDir "installer-output"
$InstallerOutputDir = Join-Path $ScriptDir "Installer\bin\$Configuration"

# Find MSBuild (VS 2022)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsInstallPath = & $vswhere -property installationPath -latest -products *
    $msbuild = Join-Path $vsInstallPath "MSBuild\Current\Bin\MSBuild.exe"
    if (!(Test-Path $msbuild)) {
        $msbuild = Join-Path $vsInstallPath "MSBuild\15.0\Bin\MSBuild.exe"
    }
}
if (!(Test-Path $msbuild)) {
    Write-Host "Error: MSBuild not found. Please install Visual Studio 2022." -ForegroundColor Red
    exit 1
}

# Expand "all" platforms
if ($Platforms -contains "all") {
    $Platforms = @("x64", "x86", "ARM64")
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NaturalVoiceSAPIAdapter Local Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow
Write-Host "Output Directory: $OutputDir" -ForegroundColor Yellow
Write-Host ""

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Step 1: Initialize submodules
if (!$SkipSubmodules) {
    Write-Host "[Step 1/5] Initializing submodules..." -ForegroundColor Cyan
    try {
        git submodule update --init --recursive
        Write-Host "  Submodules initialized" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Host ("  Warning: Failed to initialize submodules: " + $errMsg) -ForegroundColor Yellow
        Write-Host "  Continuing anyway (submodules may already be initialized)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "[Step 1/5] Skipping submodules (SkipSubmodules specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 2: Download SherpaOnnx dependencies
if (!$SkipSherpaDeps) {
    Write-Host "[Step 2/5] Downloading SherpaOnnx dependencies..." -ForegroundColor Cyan
    $depsScript = Join-Path $ScriptDir "download-sherpa-deps.ps1"

    if (!(Test-Path $depsScript)) {
        Write-Host "  Error: download-sherpa-deps.ps1 not found!" -ForegroundColor Red
        exit 1
    }

    # Determine which configurations to download
    $confs = @($Configuration)
    if ($Configuration -eq "Debug") {
        # For Debug builds, download Debug libraries
        & $depsScript -Platforms $Platforms -Configuration Debug -Force
    }
    else {
        # For Release builds, download Release libraries
        & $depsScript -Platforms $Platforms -Configuration Release -Force
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Error: Failed to download SherpaOnnx dependencies" -ForegroundColor Red
        exit 1
    }
    Write-Host "  SherpaOnnx dependencies downloaded" -ForegroundColor Green
}
else {
    Write-Host "[Step 2/5] Skipping SherpaOnnx dependencies (SkipSherpaDeps specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 3: Restore NuGet packages
Write-Host "[Step 3/5] Restoring NuGet packages..." -ForegroundColor Cyan
try {
    # For C++ projects, use MSBuild to restore packages
    & $msbuild (Join-Path $ScriptDir "NaturalVoiceSAPIAdapter.sln") /t:Restore /p:Configuration=$Configuration /nologo /v:minimal
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  NuGet packages restored" -ForegroundColor Green
    } else {
        Write-Host "  Warning: MSBuild restore had issues, continuing..." -ForegroundColor Yellow
    }
}
catch {
    $errMsg = $_.Exception.Message
    Write-Host ("  Warning: NuGet restore had issues: " + $errMsg) -ForegroundColor Yellow
    Write-Host "  Continuing anyway..." -ForegroundColor Yellow
}
Write-Host ""

# Step 4: Build NaturalVoiceSAPIAdapter DLLs
Write-Host "[Step 4/5] Building NaturalVoiceSAPIAdapter..." -ForegroundColor Cyan

foreach ($Platform in $Platforms) {
    Write-Host "  Building $Platform..." -ForegroundColor Cyan

    # Map platform names for MSBuild
    $msbuildPlatform = $Platform
    if ($Platform -eq "x86") {
        $msbuildPlatform = "Win32"
    }

    try {
        $outDir = Join-Path $ScriptDir "NaturalVoiceSAPIAdapter\bin\$Configuration"
        if ($Platform -eq "x64") {
            $outDir = Join-Path $outDir "x64"
        } elseif ($Platform -eq "ARM64") {
            $outDir = Join-Path $outDir "ARM64"
        }

        & $msbuild (Join-Path $ScriptDir "NaturalVoiceSAPIAdapter.sln") `
            /m /maxcpucount `
            /p:Configuration=$Configuration `
            /p:Platform=$msbuildPlatform `
            /p:OutDir="$outDir\" `
            /nologo /v:minimal

        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild failed with exit code $LASTEXITCODE"
        }

        # Copy output files to final directory
        $platformOutDir = Join-Path $OutputDir $Platform
        if (!(Test-Path $platformOutDir)) {
            New-Item -ItemType Directory -Path $platformOutDir -Force | Out-Null
        }

        Copy-Item -Path "$outDir\*.dll" -Destination $platformOutDir -Force
        Copy-Item -Path "$outDir\*.pdb" -Destination $platformOutDir -Force -ErrorAction SilentlyContinue

        Write-Host "    $Platform built successfully" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Host ("    Error building ${Platform}: " + $errMsg) -ForegroundColor Red
        exit 1
    }
}
Write-Host ""

# Step 5: Build SherpaOnnxConfig (Model Manager) - x64 only
Write-Host "[Step 5/5] Building SherpaOnnxConfig (Model Manager)..." -ForegroundColor Cyan
try {
    $sherpaConfigOutput = Join-Path $OutputDir "SherpaOnnxConfig"
    if (!(Test-Path $sherpaConfigOutput)) {
        New-Item -ItemType Directory -Path $sherpaConfigOutput -Force | Out-Null
    }

    dotnet publish (Join-Path $ScriptDir "SherpaOnnxConfig\SherpaOnnxConfig.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=true `
        -o $sherpaConfigOutput `
        /nologo /v:q

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Write-Host "  SherpaOnnxConfig built successfully" -ForegroundColor Green
}
catch {
    $errMsg = $_.Exception.Message
    Write-Host ("  Error building SherpaOnnxConfig: " + $errMsg) -ForegroundColor Red
    Write-Host "  Note: This requires .NET 8 SDK to be installed" -ForegroundColor Yellow
    exit 1
}

# Copy to installer output
$installerDir = Join-Path $ScriptDir "Installer\bin\$Configuration"
if (Test-Path $installerDir) {
    Copy-Item -Path "$sherpaConfigOutput\SherpaOnnxConfig.exe" -Destination $installerDir -Force
    Write-Host "  Copied SherpaOnnxConfig to installer directory" -ForegroundColor Green
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output files:" -ForegroundColor Yellow
Write-Host "  Main DLLs:     $OutputDir" -ForegroundColor White
Write-Host "  Model Manager: $sherpaConfigOutput" -ForegroundColor White
Write-Host "  Installer:     $installerDir" -ForegroundColor White
Write-Host ""

if ($Configuration -eq "Release") {
    Write-Host "To test the installer:" -ForegroundColor Yellow
    Write-Host "  1. Run: $installerDir\Installer.exe" -ForegroundColor White
    Write-Host "  2. Or copy files from $OutputDir to test manually" -ForegroundColor White
}
else {
    Write-Host "Debug build - for testing purposes" -ForegroundColor Yellow
}

Write-Host ""
exit 0
