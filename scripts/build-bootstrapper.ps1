#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build setup.exe bootstrapper for NaturalVoiceSAPIAdapter MSI.
#>

param(
    [string]$MsiPath = ".\installer-output\NaturalVoiceSAPIAdapter.msi",
    [string]$Configuration = "Release",
    [string]$OutputDir = ".\installer-output",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$bootstrapperDir = Join-Path $repoRoot "SetupLauncher"
$msiResolved = if ([System.IO.Path]::IsPathRooted($MsiPath)) { $MsiPath } else { Join-Path $repoRoot $MsiPath }
$msiFull = (Resolve-Path $msiResolved).Path
$outputFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }

if (!(Test-Path $msiFull -PathType Leaf)) {
    throw "MSI not found: $msiFull"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $v = (Get-Item $msiFull).VersionInfo.FileVersion
    if (![string]::IsNullOrWhiteSpace($v)) {
        $Version = $v
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "0.3.0.0"
    }
}

if (!(Test-Path $outputFull)) {
    New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
}

Write-Host "Building setup.exe bootstrapper..." -ForegroundColor Cyan
$publishDir = Join-Path $outputFull "setup-publish"
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

dotnet publish (Join-Path $bootstrapperDir "SetupLauncher.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:Version="$Version" `
    -o "$publishDir" `
    /nologo

if ($LASTEXITCODE -ne 0) {
    throw "Bootstrapper build failed with exit code $LASTEXITCODE"
}

$builtExe = Get-ChildItem -Path $publishDir -Filter setup.exe | Select-Object -First 1
if (-not $builtExe) {
    throw "Bootstrapper exe not found in $publishDir"
}

$targetExe = Join-Path $outputFull "setup.exe"
Copy-Item -Path $builtExe.FullName -Destination $targetExe -Force
$targetMsi = Join-Path $outputFull "NaturalVoiceSAPIAdapter.msi"
if ([System.IO.Path]::GetFullPath($msiFull) -ne [System.IO.Path]::GetFullPath($targetMsi)) {
    Copy-Item -Path $msiFull -Destination $targetMsi -Force
}

Write-Host "Bootstrapper built: $targetExe" -ForegroundColor Green
