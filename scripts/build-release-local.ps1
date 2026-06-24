#!/usr/bin/env pwsh
<#
.SYNOPSIS
    CI-parity local build (utilities + payload + MSI/bootstrapper).

.DESCRIPTION
    Mirrors .github/workflows/msbuild.yml locally:
    - Sherpa deps
    - SherpaOnnxConfig publish
    - Utilities (AzureSpeechSDKShim, TtsApplication, Arm64XForwarder, Installer)
    - Main adapter per platform
    - DLL trimming and ucrtbase copy
    - Payload composition
    - MSI and setup.exe (optional)
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "x86", "ARM64", "all")]
    [string[]]$Platforms = @("x64"),

    [switch]$SkipSherpaDeps,
    [switch]$ForceSherpaDeps,
    [switch]$SkipSubmodules,
    [switch]$SkipVerify,
    [switch]$BuildSetup
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$StageRoot = Join-Path $RepoRoot "out-full"
$PayloadDir = Join-Path $RepoRoot "payload"
$InstallerOutputDir = Join-Path $RepoRoot "installer-output"

function Resolve-MSBuild {
    $msbuild = $null
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsInstallPath = & $vswhere -property installationPath -latest -products *
        if ($vsInstallPath) {
            $msbuild = Join-Path $vsInstallPath "MSBuild\Current\Bin\MSBuild.exe"
            if (!(Test-Path $msbuild)) {
                $msbuild = Join-Path $vsInstallPath "MSBuild\15.0\Bin\MSBuild.exe"
            }
        }
    }
    if (!$msbuild -or !(Test-Path $msbuild)) {
        $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($cmd) {
            return $cmd.Source
        }
        throw "MSBuild not found. Please install Visual Studio 2022 or add MSBuild to PATH."
    }
    return $msbuild
}

function Resolve-NuGet {
    $cmd = Get-Command nuget -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidatePaths = @(
        "$env:ProgramFiles\NuGet\nuget.exe",
        "$env:ProgramFiles(x86)\NuGet\nuget.exe",
        "$env:ChocolateyInstall\bin\nuget.exe",
        (Join-Path $RepoRoot "nuget.exe")
    )

    foreach ($candidate in $candidatePaths) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Ensure-Dir {
    param([string]$Path)
    if (!(Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Trim-Dlls {
    param(
        [Parameter(Mandatory = $true)][string]$DirPath
    )

    $vspath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -property installationPath -latest
    $msvcpath = (Get-ChildItem "$vspath\VC\Tools\MSVC" | Sort-Object Name -Descending | Select-Object -First 1).FullName
    $dumpbinpath = "$msvcpath\bin\Hostx86\x86\dumpbin.exe"

    Push-Location $DirPath
    try {
        $dlls = & $dumpbinpath /dependents NaturalVoiceSAPIAdapter.dll Microsoft.CognitiveServices.*.dll `
            | Where-Object { $_ -like "    *.dll" } | ForEach-Object { $_.Trim() } | Select-Object -Unique
        $dlls += "NaturalVoiceSAPIAdapter.dll", "Microsoft.CognitiveServices.*.dll"
        $dlls += "sherpa-onnx-c-api.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll"
        Remove-Item * -Include *.dll -Exclude $dlls
        Remove-Item Microsoft.CognitiveServices.Speech.extension.codec.dll -ErrorAction Ignore
    }
    finally {
        Pop-Location
    }
}

function Resolve-RestoreTarget {
    param(
        [Parameter(Mandatory = $true)][string]$InputPath
    )

    if (Test-Path $InputPath -PathType Leaf) {
        return $InputPath
    }

    if (!(Test-Path $InputPath -PathType Container)) {
        throw "Restore target not found: $InputPath"
    }

    $sln = Get-ChildItem -Path $InputPath -Filter *.sln -File | Select-Object -First 1
    if ($sln) { return $sln.FullName }

    $proj = Get-ChildItem -Path $InputPath -Filter *.vcxproj -File | Select-Object -First 1
    if ($proj) { return $proj.FullName }

    $proj = Get-ChildItem -Path $InputPath -Filter *.csproj -File | Select-Object -First 1
    if ($proj) { return $proj.FullName }

    throw "Could not resolve restore target under: $InputPath"
}

function Invoke-Restore {
    param(
        [Parameter(Mandatory = $true)][string]$InputPath,
        [string]$SolutionDirectory = ""
    )

    $target = Resolve-RestoreTarget -InputPath $InputPath

    if ($nugetExe) {
        if ([string]::IsNullOrWhiteSpace($SolutionDirectory)) {
            & $nugetExe restore $target
        } else {
            & $nugetExe restore $target -SolutionDirectory $SolutionDirectory
        }
        if ($LASTEXITCODE -ne 0) {
            throw "NuGet restore failed for: $target"
        }
        return
    }

    # Fallback for environments without nuget.exe.
    & $msbuild $target /t:Restore /p:RestorePackagesConfig=true /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild restore fallback failed for: $target"
    }
}

# Expand "all" platforms
if ($Platforms -contains "all") {
    $Platforms = @("x64", "x86", "ARM64")
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NaturalVoiceSAPIAdapter CI-Parity Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor Yellow
Write-Host "Stage Root: $StageRoot" -ForegroundColor Yellow
Write-Host "Payload Dir: $PayloadDir" -ForegroundColor Yellow
Write-Host "Installer Output: $InstallerOutputDir" -ForegroundColor Yellow
Write-Host ""

$msbuild = Resolve-MSBuild
$nugetExe = Resolve-NuGet
if ($nugetExe) {
    Write-Host "NuGet restore tool: $nugetExe" -ForegroundColor DarkGray
} else {
    Write-Host "NuGet.exe not found; using MSBuild restore fallback." -ForegroundColor Yellow
}

# Step 1: Initialize submodules
if (!$SkipSubmodules) {
    Write-Host "[Step 1/8] Initializing submodules..." -ForegroundColor Cyan
    git submodule update --init --recursive
    Write-Host "  Submodules initialized" -ForegroundColor Green
} else {
    Write-Host "[Step 1/8] Skipping submodules (SkipSubmodules specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 2: Download SherpaOnnx dependencies
if (!$SkipSherpaDeps) {
    Write-Host "[Step 2/8] Downloading SherpaOnnx dependencies..." -ForegroundColor Cyan
    $depsScript = Join-Path $RepoRoot "download-sherpa-deps.ps1"
    if (!(Test-Path $depsScript)) {
        throw "download-sherpa-deps.ps1 not found!"
    }
    if ($ForceSherpaDeps) {
        & $depsScript -Platforms $Platforms -Configuration $Configuration -Force
    } else {
        & $depsScript -Platforms $Platforms -Configuration $Configuration
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to download SherpaOnnx dependencies"
    }
    Write-Host "  SherpaOnnx dependencies downloaded" -ForegroundColor Green
} else {
    Write-Host "[Step 2/8] Skipping SherpaOnnx dependencies (SkipSherpaDeps specified)" -ForegroundColor DarkGray
}
Write-Host ""

# Step 3: Build SherpaOnnxConfig (self-contained x64)
Write-Host "[Step 3/8] Building SherpaOnnxConfig (Model Manager)..." -ForegroundColor Cyan
$sherpaConfigOutput = Join-Path $RepoRoot "sherpa-config"
Ensure-Dir $sherpaConfigOutput
dotnet publish (Join-Path $RepoRoot "SherpaOnnxConfig\SherpaOnnxConfig.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -o $sherpaConfigOutput `
    /nologo /v:q
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
Write-Host "  SherpaOnnxConfig built successfully" -ForegroundColor Green
Write-Host ""

# Step 4: Build Utilities per platform
Write-Host "[Step 4/8] Building utilities..." -ForegroundColor Cyan
Ensure-Dir $StageRoot
if (Test-Path $StageRoot) {
    Remove-Item -Path $StageRoot -Recurse -Force
}
Ensure-Dir $StageRoot

foreach ($Platform in $Platforms) {
    $utilOut = Join-Path $StageRoot "utilities-$Platform"
    Ensure-Dir $utilOut

    if ($Platform -ne "ARM64") {
        Write-Host "  AzureSpeechSDKShim ($Platform)..." -ForegroundColor Cyan
        Invoke-Restore -InputPath (Join-Path $RepoRoot "AzureSpeechSDKShim")
        & $msbuild /m /p:Configuration=$Configuration /p:Platform=$Platform /p:OutDir="$utilOut\" (Join-Path $RepoRoot "AzureSpeechSDKShim")
        if ($LASTEXITCODE -ne 0) { throw "AzureSpeechSDKShim build failed ($Platform)" }
    }

    $ttsAppDir = Join-Path $RepoRoot "TtsApplication"
    if (Test-Path $ttsAppDir) {
        Write-Host "  TtsApplication ($Platform)..." -ForegroundColor Cyan
        Invoke-Restore -InputPath $ttsAppDir
        if ($Platform -eq "x86") {
            & $msbuild /m /p:Configuration=$Configuration /p:Platform=Win32 /p:OutDir="$utilOut\" (Join-Path $ttsAppDir "TtsApplication.sln")
        } else {
            & $msbuild /m /p:Configuration=$Configuration /p:Platform=$Platform /p:OutDir="$utilOut\" (Join-Path $ttsAppDir "TtsApplication.sln")
        }
        if ($LASTEXITCODE -ne 0) { throw "TtsApplication build failed ($Platform)" }
    } else {
        Write-Host "  TtsApplication ($Platform)... skipped (not present)" -ForegroundColor DarkGray
    }

    if ($Platform -eq "ARM64") {
        Write-Host "  Arm64XForwarder (ARM64EC)..." -ForegroundColor Cyan
        Invoke-Restore -InputPath (Join-Path $RepoRoot "Arm64XForwarder") -SolutionDirectory $RepoRoot
        & $msbuild /m /p:Configuration=$Configuration /p:Platform=ARM64EC /p:OutDir="$utilOut\" (Join-Path $RepoRoot "Arm64XForwarder")
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Warning: ARM64EC build failed; falling back to ARM64 for Arm64XForwarder." -ForegroundColor Yellow
            & $msbuild /m /p:Configuration=$Configuration /p:Platform=ARM64 /p:OutDir="$utilOut\" (Join-Path $RepoRoot "Arm64XForwarder")
            if ($LASTEXITCODE -ne 0) { throw "Arm64XForwarder build failed (ARM64EC and ARM64 fallback)" }
        }
    }

    if ($Platform -eq "x86") {
        Write-Host "  Installer (Win32)..." -ForegroundColor Cyan
        Invoke-Restore -InputPath (Join-Path $RepoRoot "Installer") -SolutionDirectory $RepoRoot
        & $msbuild /m /p:Configuration=$Configuration /p:Platform=Win32 /p:OutDir="$utilOut\" (Join-Path $RepoRoot "Installer\Installer.vcxproj")
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
        Copy-Item -Path (Join-Path $utilOut "Installer.exe") -Destination (Join-Path $utilOut "InstallPlanRunner.exe") -Force
    }

    # Stage SherpaOnnxConfig for x86/x64 utilities (matches CI behavior)
    if ($Platform -eq "x86" -or $Platform -eq "x64") {
        Copy-Item -Path (Join-Path $sherpaConfigOutput "SherpaOnnxConfig.exe") -Destination $utilOut -Force
        $models = Join-Path $sherpaConfigOutput "merged_models.json"
        if (Test-Path $models) {
            Copy-Item -Path $models -Destination $utilOut -Force
        }
    }
}
Write-Host "  Utilities built successfully" -ForegroundColor Green
Write-Host ""

# Step 5: Build main adapter per platform
Write-Host "[Step 5/8] Building NaturalVoiceSAPIAdapter per platform..." -ForegroundColor Cyan
foreach ($Platform in $Platforms) {
    $mainOut = Join-Path $StageRoot "main-$Platform"
    Ensure-Dir $mainOut

    Write-Host "  Building main adapter ($Platform)..." -ForegroundColor Cyan
    & $msbuild (Join-Path $RepoRoot "NaturalVoiceSAPIAdapter.sln") `
        /m /maxcpucount `
        /p:Configuration=$Configuration `
        /p:Platform=$Platform `
        /nologo /v:minimal `
        /t:NaturalVoiceSAPIAdapter:Clean
    if ($LASTEXITCODE -ne 0) {
        throw "Clean failed for NaturalVoiceSAPIAdapter ($Platform)"
    }

    & $msbuild (Join-Path $RepoRoot "NaturalVoiceSAPIAdapter.sln") `
        /m /maxcpucount `
        /p:Configuration=$Configuration `
        /p:Platform=$Platform `
        /p:OutDir="$mainOut\" `
        /p:RegisterOutput=false `
        /nologo /v:minimal `
        /t:NaturalVoiceSAPIAdapter

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed for NaturalVoiceSAPIAdapter ($Platform)"
    }

    # Trim DLLs for x86/x64 like CI
    if ($Platform -eq "x86" -or $Platform -eq "x64") {
        Trim-Dlls -DirPath $mainOut
    }

    # Copy ucrtbase.dll for x86/x64 like CI
    if ($Platform -eq "x64") {
        Copy-Item -Path "$env:SystemRoot\System32\ucrtbase.dll" -Destination $mainOut -Force
    } elseif ($Platform -eq "x86") {
        Copy-Item -Path "$env:SystemRoot\SysWOW64\ucrtbase.dll" -Destination $mainOut -Force
    }
}
Write-Host "  Main adapter built successfully" -ForegroundColor Green
Write-Host ""

# Step 5.5: Build .NET adapter (x86, x64 only)
$dotnetAdapterPlatforms = $Platforms | Where-Object { $_ -ne "ARM64" }
if ($dotnetAdapterPlatforms.Count -gt 0) {
    Write-Host "[Step 5.5/9] Building .NET SAPI adapter..." -ForegroundColor Cyan
    foreach ($Platform in $dotnetAdapterPlatforms) {
        $dotnetOut = Join-Path $StageRoot "dotnet-adapter-$Platform"
        Ensure-Dir $dotnetOut
        $rid = if ($Platform -eq "x86") { "win-x86" } else { "win-x64" }
        Write-Host "  Publishing .NET adapter ($Platform)..." -ForegroundColor Cyan
        dotnet publish (Join-Path $RepoRoot "NaturalVoiceSAPIAdapter.Net\NaturalVoiceSAPIAdapter.Net.csproj") `
            -c $Configuration `
            -r $rid `
            --self-contained false `
            -o $dotnetOut `
            /nologo /v:q
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for .NET adapter ($Platform)"
        }
    }
    Write-Host "  .NET adapter built successfully" -ForegroundColor Green
    Write-Host ""
}

# Step 6: Sherpa verification
if (!$SkipVerify) {
    Write-Host "[Step 6/8] Running Sherpa integration verification..." -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\verify-sherpa-integration.ps1") -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw "Sherpa integration verification failed" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\run-sherpa-smoke-test.ps1") -CompileOnly
    if ($LASTEXITCODE -ne 0) { throw "Sherpa smoke test compile failed" }
    Write-Host "  Verification passed" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[Step 6/8] Skipping verification (SkipVerify specified)" -ForegroundColor DarkGray
    Write-Host ""
}

# Step 7: Compose payload
Write-Host "[Step 7/8] Composing payload..." -ForegroundColor Cyan
if (Test-Path $PayloadDir) {
    Remove-Item -Path $PayloadDir -Recurse -Force
}
Ensure-Dir $PayloadDir
Ensure-Dir (Join-Path $PayloadDir "x86")
Ensure-Dir (Join-Path $PayloadDir "x64")
Ensure-Dir (Join-Path $PayloadDir "ARM64")

foreach ($Platform in $Platforms) {
    $utilOut = Join-Path $StageRoot "utilities-$Platform"
    $mainOut = Join-Path $StageRoot "main-$Platform"
    $platformPayload = Join-Path $PayloadDir $Platform
    Copy-Item $utilOut\* $platformPayload\ -Recurse -Force
    Copy-Item $mainOut\* $platformPayload\ -Recurse -Force

    $dotnetOut = Join-Path $StageRoot "dotnet-adapter-$Platform"
    if (Test-Path $dotnetOut) {
        Copy-Item $dotnetOut\* $platformPayload\ -Recurse -Force
    }
}

if (Test-Path (Join-Path $PayloadDir "x86\Installer.exe")) {
    Copy-Item (Join-Path $PayloadDir "x86\Installer.exe") (Join-Path $PayloadDir "Installer.exe") -Force
}
if (Test-Path (Join-Path $PayloadDir "x86\InstallPlanRunner.exe")) {
    Copy-Item (Join-Path $PayloadDir "x86\InstallPlanRunner.exe") (Join-Path $PayloadDir "InstallPlanRunner.exe") -Force
}

$brandingSource = ""
$brandingCandidate = Join-Path $RepoRoot "config\branding.json"
if (Test-Path $brandingCandidate) {
    $brandingSource = $brandingCandidate
    Copy-Item $brandingSource (Join-Path $PayloadDir "branding.json") -Force
}

$defaultPlanSource = Join-Path $RepoRoot "samples\install-plans\default-install-plan.json"
if (Test-Path $defaultPlanSource) {
    Copy-Item $defaultPlanSource (Join-Path $PayloadDir "install-plan.json") -Force
}
Write-Host "  Payload ready at $PayloadDir" -ForegroundColor Green
Write-Host ""

# Step 8: Build MSI + setup.exe (optional)
if ($BuildSetup) {
    Write-Host "[Step 8/8] Building MSI and setup.exe..." -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\build-setup.ps1") -PayloadDir $PayloadDir -OutputDir $InstallerOutputDir -BrandingFile $brandingSource
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\build-bootstrapper.ps1") -MsiPath (Join-Path $InstallerOutputDir "NaturalVoiceSAPIAdapter.msi") -OutputDir $InstallerOutputDir -BrandingFile $brandingSource
    if ($LASTEXITCODE -ne 0) { throw "Bootstrapper build failed" }
    Write-Host "  MSI + setup.exe built in $InstallerOutputDir" -ForegroundColor Green
} else {
    Write-Host "[Step 8/8] Skipping MSI/bootstrapper (BuildSetup not specified)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Complete (CI-Parity)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Payload: $PayloadDir" -ForegroundColor Yellow
Write-Host "Installer output: $InstallerOutputDir" -ForegroundColor Yellow
Write-Host ""
