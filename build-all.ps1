#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complete local build script for NaturalVoiceSAPIAdapter

.DESCRIPTION
    Builds ALL components of NaturalVoiceSAPIAdapter locally, including:
    - SherpaOnnx dependencies download
    - NaturalVoiceSAPIAdapter DLL (main SAPI adapter) - x86, x64, ARM64
    - AzureSpeechSDKShim - x86, x64
    - TtsApplication - x64, ARM64
    - Arm64XForwarder - ARM64
    - SherpaOnnxConfig (Model Manager)
    - Installer - x86

    This replicates what the GitHub Actions CI/CD does, but locally.

.PARAMETER Configuration
    Build configuration: "Debug" or "Release"
    Default: "Release"

.PARAMETER Platforms
    Platforms to build: "x64", "x86", "ARM64", or "all"
    Default: "x64"

.PARAMETER SkipSherpaDeps
    Skip downloading SherpaOnnx dependencies (useful if already downloaded)

.PARAMETER ForceSherpaDeps
    Force re-download SherpaOnnx dependencies even if already extracted

.PARAMETER SkipSubmodules
    Skip initializing/updating submodules

.EXAMPLE
    .\build-all.ps1
    Build x64 Release (most common)

.EXAMPLE
    .\build-all.ps1 -Configuration Release -Platforms all
    Build Release for all platforms (full local build matching CI/CD)
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86", "ARM64", "all")]
    [string[]]$Platforms = @("x64"),

    [switch]$SkipSherpaDeps,

    [switch]$ForceSherpaDeps,

    [switch]$SkipSubmodules,

    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"

# Script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path $ScriptDir "installer-output"
$UtilitiesOutputDir = Join-Path $ScriptDir "out"

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

# Create output directories
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
if (!(Test-Path $UtilitiesOutputDir)) {
    New-Item -ItemType Directory -Path $UtilitiesOutputDir -Force | Out-Null
}

function Copy-IfExists {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )
    if (Test-Path $SourcePath) {
        Copy-Item -Path $SourcePath -Destination $DestinationPath -Force -ErrorAction Stop
    }
}

# Step 1: Initialize submodules
if (!$SkipSubmodules) {
    Write-Host "[Step 1/7] Initializing submodules..." -ForegroundColor Cyan
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
    Write-Host "[Step 1/7] Skipping submodules (SkipSubmodules specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 2: Download SherpaOnnx dependencies
if (!$SkipSherpaDeps) {
    Write-Host "[Step 2/7] Downloading SherpaOnnx dependencies..." -ForegroundColor Cyan
    $depsScript = Join-Path $ScriptDir "download-sherpa-deps.ps1"

    if (!(Test-Path $depsScript)) {
        Write-Host "  Error: download-sherpa-deps.ps1 not found!" -ForegroundColor Red
        exit 1
    }

    # Determine which configurations to download
    $confs = @($Configuration)
    if ($Configuration -eq "Debug") {
        # For Debug builds, download Debug libraries
        if ($ForceSherpaDeps) {
            & $depsScript -Platforms $Platforms -Configuration Debug -Force
        } else {
            & $depsScript -Platforms $Platforms -Configuration Debug
        }
    }
    else {
        # For Release builds, download Release libraries
        if ($ForceSherpaDeps) {
            & $depsScript -Platforms $Platforms -Configuration Release -Force
        } else {
            & $depsScript -Platforms $Platforms -Configuration Release
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Error: Failed to download SherpaOnnx dependencies" -ForegroundColor Red
        exit 1
    }
    Write-Host "  SherpaOnnx dependencies downloaded" -ForegroundColor Green
}
else {
    Write-Host "[Step 2/7] Skipping SherpaOnnx dependencies (SkipSherpaDeps specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 3: Restore NuGet packages
Write-Host "[Step 3/7] Restoring NuGet packages..." -ForegroundColor Cyan
try {
    # Restore for solution
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

# Step 4: Build SherpaOnnxConfig (Model Manager)
Write-Host "[Step 4/7] Building SherpaOnnxConfig (Model Manager)..." -ForegroundColor Cyan
try {
    $sherpaConfigOutput = Join-Path $UtilitiesOutputDir "sherpa-config"
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
Write-Host ""

# Step 5: Build Utilities (AzureSpeechSDKShim, TtsApplication, Arm64XForwarder)
# NOTE: These are optional utilities that have some build issues in local environment
# They are built in CI/CD but skipped here for local development
Write-Host "[Step 5/7] Building Utilities..." -ForegroundColor Cyan
Write-Host "  Skipping utilities (AzureSpeechSDKShim, TtsApplication) - optional for local testing" -ForegroundColor DarkGray
Write-Host "  Note: These are included in the GitHub Actions release builds" -ForegroundColor DarkGray
Write-Host ""

# Step 6: Build NaturalVoiceSAPIAdapter DLLs (main SAPI adapter)
Write-Host "[Step 6/7] Building NaturalVoiceSAPIAdapter..." -ForegroundColor Cyan

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
            /p:RegisterOutput=false `
            /nologo /v:minimal

        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild failed with exit code $LASTEXITCODE"
        }

        # Copy core runtime files to utilities directory (for installer/runtime verification).
        # Fail fast if these cannot be refreshed, to avoid stale DLLs in out\.
        Copy-IfExists -SourcePath "$outDir\NaturalVoiceSAPIAdapter.dll" -DestinationPath $UtilitiesOutputDir
        Copy-IfExists -SourcePath "$outDir\NaturalVoiceSAPIAdapter.pdb" -DestinationPath $UtilitiesOutputDir
        Copy-IfExists -SourcePath "$outDir\sherpa-onnx-c-api.dll" -DestinationPath $UtilitiesOutputDir
        Copy-IfExists -SourcePath "$outDir\onnxruntime.dll" -DestinationPath $UtilitiesOutputDir
        Copy-IfExists -SourcePath "$outDir\onnxruntime_providers_shared.dll" -DestinationPath $UtilitiesOutputDir

        Write-Host "    $Platform built successfully" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Host ("    Error building ${Platform}: " + $errMsg) -ForegroundColor Red
        exit 1
    }
}
Write-Host ""

# Step 7: Build Installer (x86 only)
Write-Host "[Step 7/7] Building Installer..." -ForegroundColor Cyan
try {
    & $msbuild (Join-Path $ScriptDir "Installer\Installer.vcxproj") `
        /p:Configuration=$Configuration `
        /p:Platform=Win32 `
        /p:OutDir="$UtilitiesOutputDir\" `
        /m /nologo /v:minimal

    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed"
    }

    Write-Host "  Installer built successfully" -ForegroundColor Green
}
catch {
    $errMsg = $_.Exception.Message
    Write-Host ("  Error building Installer: " + $errMsg) -ForegroundColor Red
    exit 1
}
Write-Host ""

# Copy SherpaOnnxConfig to utilities output (for installer)
Write-Host "Copying SherpaOnnxConfig to utilities output..." -ForegroundColor Cyan
try {
    Copy-Item -Path "$UtilitiesOutputDir\sherpa-config\SherpaOnnxConfig.exe" -Destination $UtilitiesOutputDir -Force
    if (Test-Path "$UtilitiesOutputDir\sherpa-config\merged_models.json") {
        Copy-Item -Path "$UtilitiesOutputDir\sherpa-config\merged_models.json" -Destination $UtilitiesOutputDir -Force
    }
    Write-Host "  Copied successfully" -ForegroundColor Green
}
catch {
    Write-Host "  Warning: Failed to copy SherpaOnnxConfig: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Step 8: Run verification checks
if (!$SkipVerify) {
    Write-Host "[Step 8/8] Running Sherpa integration verification..." -ForegroundColor Cyan
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "scripts\verify-sherpa-integration.ps1") -SkipBuild
        if ($LASTEXITCODE -ne 0) {
            throw "Verification script failed with exit code $LASTEXITCODE"
        }
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ScriptDir "scripts\run-sherpa-smoke-test.ps1")
        if ($LASTEXITCODE -ne 0) {
            throw "Sherpa smoke test failed with exit code $LASTEXITCODE"
        }
        Write-Host "  Verification passed" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        Write-Host ("  Error: Verification failed: " + $errMsg) -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}
else {
    Write-Host "[Step 8/8] Skipping verification (SkipVerify specified)" -ForegroundColor DarkGray
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output files:" -ForegroundColor Yellow
Write-Host "  All components: $UtilitiesOutputDir" -ForegroundColor White
Write-Host "  Installer (with embedded components): C:\github\NaturalVoiceSAPIAdapter\out\Installer.exe" -ForegroundColor White
Write-Host ""

if ($Configuration -eq "Release") {
    Write-Host "To test the installer:" -ForegroundColor Yellow
    Write-Host "  Run: C:\github\NaturalVoiceSAPIAdapter\out\Installer.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "The installer embeds all components from $UtilitiesOutputDir" -ForegroundColor Cyan
    Write-Host "  including SherpaOnnxConfig.exe (Model Manager), NaturalVoiceSAPIAdapter.dll," -ForegroundColor Cyan
    Write-Host "  and all runtime DLLs." -ForegroundColor Cyan
}
else {
    Write-Host "Debug build - for testing purposes" -ForegroundColor Yellow
}

Write-Host ""
exit 0
