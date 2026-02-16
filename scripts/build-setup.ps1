#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build MSI package for NaturalVoiceSAPIAdapter.

.DESCRIPTION
Generates a WiX payload manifest from a staged payload directory and builds:
- NaturalVoiceSAPIAdapter.msi

Optional uninstall data cleanup is controlled via MSI property:
  msiexec /x {ProductCode} REMOVE_APPDATA=1
#>

param(
    [string]$PayloadDir = ".\out",
    [string]$Configuration = "Release",
    [string]$OutputDir = ".\installer-output",
    [string]$Version = "",
    [string]$BrandingFile = "",
    [switch]$SkipCurate
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$payloadPath = if ([System.IO.Path]::IsPathRooted($PayloadDir)) { $PayloadDir } else { Join-Path $repoRoot $PayloadDir }
$payloadInput = (Resolve-Path $payloadPath).Path
$setupDir = Join-Path $repoRoot "Setup"
$manifestPath = Join-Path $setupDir "PayloadFiles.wxs"
$outputFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
$curatedPayload = Join-Path $outputFull "setup-payload"

if (!(Test-Path $payloadInput -PathType Container)) {
    throw "Payload directory not found: $payloadInput"
}

if (!(Test-Path $setupDir -PathType Container)) {
    throw "Setup project directory not found: $setupDir"
}

$productName = "NaturalVoiceSAPIAdapter"
$manufacturer = "NaturalVoiceSAPIAdapter"
$installFolderName = "NaturalVoiceSAPIAdapter"
$installerShortcutName = "NaturalVoiceSAPIAdapter Installer"
$projectUrl = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter"

$brandingPath = $null
if (![string]::IsNullOrWhiteSpace($BrandingFile)) {
    $brandingPath = if ([System.IO.Path]::IsPathRooted($BrandingFile)) { $BrandingFile } else { Join-Path $repoRoot $BrandingFile }
    if (!(Test-Path $brandingPath -PathType Leaf)) {
        throw "Branding file not found: $brandingPath"
    }
}
elseif (Test-Path (Join-Path $repoRoot "config\branding.json") -PathType Leaf) {
    $brandingPath = Join-Path $repoRoot "config\branding.json"
}

if ($brandingPath) {
    $branding = Get-Content $brandingPath -Raw | ConvertFrom-Json
    if ($branding.product_name) { $productName = [string]$branding.product_name }
    if ($branding.manufacturer) { $manufacturer = [string]$branding.manufacturer }
    if ($branding.install_folder_name) { $installFolderName = [string]$branding.install_folder_name }
    if ($branding.installer_shortcut_name) { $installerShortcutName = [string]$branding.installer_shortcut_name }
    if ($branding.project_url) { $projectUrl = [string]$branding.project_url }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $installerExe = Join-Path $payloadInput "Installer.exe"
    if (Test-Path $installerExe) {
        $v = (Get-Item $installerExe).VersionInfo.FileVersion
        if (![string]::IsNullOrWhiteSpace($v)) {
            $Version = $v
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "0.3.0.0"
    }
}

if (!(Test-Path $outputFull)) {
    New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
}

if ($SkipCurate) {
    $payloadFull = $payloadInput
}
else {
    Write-Host "Creating curated setup payload..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "create-setup-payload.ps1") -SourceDir $payloadInput -OutputDir $curatedPayload
    $payloadFull = (Resolve-Path $curatedPayload).Path
}

if ($brandingPath) {
    Copy-Item -Path $brandingPath -Destination (Join-Path $payloadFull "branding.json") -Force
}

Write-Host "Generating setup payload manifest..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "generate-setup-payload-wxs.ps1") -PayloadDir $payloadFull -OutputPath $manifestPath

Write-Host "Building MSI..." -ForegroundColor Cyan
dotnet build (Join-Path $setupDir "Setup.wixproj") `
    -c $Configuration `
    -p:PayloadDir="$payloadFull" `
    -p:ProductVersion="$Version" `
    -p:ProductName="$productName" `
    -p:Manufacturer="$manufacturer" `
    -p:InstallFolderName="$installFolderName" `
    -p:InstallerShortcutName="$installerShortcutName" `
    -p:ProjectUrl="$projectUrl" `
    -p:OutputPath="$outputFull\" `
    /nologo

if ($LASTEXITCODE -ne 0) {
    throw "MSI build failed with exit code $LASTEXITCODE"
}

$builtMsi = Get-ChildItem -Path $outputFull -Filter *.msi | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $builtMsi) {
    throw "MSI output was not produced in $outputFull"
}

$targetMsi = Join-Path $outputFull "NaturalVoiceSAPIAdapter.msi"
Copy-Item -Path $builtMsi.FullName -Destination $targetMsi -Force

# Keep only the canonical MSI filename in output to avoid user confusion.
$targetFull = [System.IO.Path]::GetFullPath($targetMsi)
Get-ChildItem -Path $outputFull -Filter *.msi | ForEach-Object {
    if ([System.IO.Path]::GetFullPath($_.FullName) -ne $targetFull) {
        Remove-Item -Path $_.FullName -Force
    }
}

Write-Host "MSI built: $targetMsi" -ForegroundColor Green
