#!/usr/bin/env pwsh
<#
.SYNOPSIS
Create curated setup payload directory for MSI packaging.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$src = (Resolve-Path $SourceDir).Path

if (!(Test-Path $src -PathType Container)) {
    throw "Source directory not found: $src"
}

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$rootAllow = @(
    "VoiceGarden.UI.exe",
    "install-plan.json",
    "_branding.json.removed",
    "SherpaOnnxConfig.exe",
    "models.json",
    "LICENSE.txt",
    "README.md"
)

foreach ($name in $rootAllow) {
    $p = Join-Path $src $name
    if (Test-Path $p -PathType Leaf) {
        Copy-Item -Path $p -Destination (Join-Path $OutputDir $name) -Force
    }
}

function Copy-ArchTree([string]$archDirPath, [string]$archName) {
    if (!(Test-Path $archDirPath -PathType Container)) {
        return $false
    }
    $dst = Join-Path $OutputDir $archName
    New-Item -ItemType Directory -Path $dst -Force | Out-Null

    # Include runtime-oriented files only.
    $includeExt = @("*.dll", "*.exe", "*.json", "*.txt")
    # Exclude debug/development tools from installer payload
    $excludeNames = @("TtsApplication.exe")

    foreach ($pattern in $includeExt) {
        Get-ChildItem -Path $archDirPath -Recurse -File -Filter $pattern | ForEach-Object {
            if ($excludeNames -contains $_.Name) {
                return
            }
            $rel = $_.FullName.Substring($archDirPath.Length).TrimStart('\')
            $destFile = Join-Path $dst $rel
            $destParent = Split-Path -Parent $destFile
            if (!(Test-Path $destParent)) {
                New-Item -ItemType Directory -Path $destParent -Force | Out-Null
            }
            Copy-Item -Path $_.FullName -Destination $destFile -Force
        }
    }
    return $true
}

$hasAnyArch = $false
$hasAnyArch = (Copy-ArchTree (Join-Path $src "x86") "x86") -or $hasAnyArch
$hasAnyArch = (Copy-ArchTree (Join-Path $src "x64") "x64") -or $hasAnyArch
$hasAnyArch = (Copy-ArchTree (Join-Path $src "ARM64") "ARM64") -or $hasAnyArch

if (-not $hasAnyArch) {
    # Fallback for local out\ layout that stores runtime files at root.
    $fallbackArch = Join-Path $OutputDir "x64"
    New-Item -ItemType Directory -Path $fallbackArch -Force | Out-Null

    $fallbackNames = @(
        "VoiceGardenSAPIAdapter.dll",
        "rust_tts_wrapper.dll",
        "sherpa-onnx-c-api.dll",
        "onnxruntime.dll",
        "onnxruntime_providers_shared.dll",
        "ucrtbase.dll",
        "Arm64XForwarder.dll",
        "Microsoft.CognitiveServices.Speech.core.dll",
        "Microsoft.CognitiveServices.Speech.extension.embedded.tts.dll",
        "Microsoft.CognitiveServices.Speech.extension.kws.dll",
        "Microsoft.CognitiveServices.Speech.extension.codec.dll"
    )
    foreach ($n in $fallbackNames) {
        $p = Join-Path $src $n
        if (Test-Path $p -PathType Leaf) {
            Copy-Item -Path $p -Destination (Join-Path $fallbackArch $n) -Force
        }
    }

    Get-ChildItem -Path $src -File -Filter *.json | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $fallbackArch $_.Name) -Force
    }
}

$count = (Get-ChildItem -Path $OutputDir -Recurse -File | Measure-Object).Count
Write-Host "Curated payload created: $OutputDir" -ForegroundColor Green
Write-Host "Payload files: $count"

