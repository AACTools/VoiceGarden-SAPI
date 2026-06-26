param(
    [switch]$CompileOnly,
    [switch]$RequireModel,
    [string]$ModelRoot = "$env:LOCALAPPDATA\NaturalVoiceSAPIAdapter\models",
    [string]$ModelId
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$outDir = Join-Path $repoRoot "out\sherpa-smoke"
$exePath = Join-Path $outDir "sherpa-smoke-test.exe"

if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Clear stale smoke-test processes/executable from previous hangs.
Get-Process sherpa-smoke-test -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
if (Test-Path $exePath) {
    Remove-Item $exePath -Force -ErrorAction SilentlyContinue
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) {
    throw "vswhere.exe not found"
}
$vsInstall = & $vswhere -latest -products * -property installationPath
if ([string]::IsNullOrWhiteSpace($vsInstall)) {
    throw "Visual Studio installation not found"
}
$vcvars = Join-Path $vsInstall "VC\Auxiliary\Build\vcvars64.bat"
if (!(Test-Path $vcvars)) {
    throw "vcvars64.bat not found at $vcvars"
}

$src1 = Join-Path $repoRoot "scripts\sherpa-smoke-test.cpp"
$src2 = Join-Path $repoRoot "SherpaOnnx\SherpaOnnxEngine.cpp"
$src3 = Join-Path $repoRoot "SherpaOnnx\SherpaOnnxDynamic.cpp"
$incCandidates = @(
    (Join-Path $repoRoot "SherpaOnnx\libs\sherpa-onnx-v1.12.23-win-x64-shared-Release\include"),
    (Join-Path $repoRoot "SherpaOnnx\libs\sherpa-onnx-v1.12.23-win-x64-shared-Debug\include"),
    (Join-Path $repoRoot "SherpaOnnx\libs\sherpa-onnx-v1.12.23-win-x64-shared\include")
)
$inc1 = $incCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$inc1 = [string]$inc1
if ([string]::IsNullOrWhiteSpace($inc1)) {
    throw ("Sherpa include directory not found. Expected one of:`n  - " + ($incCandidates -join "`n  - "))
}
$inc2 = $repoRoot

Write-Host "Compiling sherpa smoke test..." -ForegroundColor Cyan
$compileCmd = "call `"$vcvars`" && cl /nologo /EHsc /std:c++20 /O2 /I`"$inc1`" /I`"$inc2`" `"$src1`" `"$src2`" `"$src3`" /Fe:`"$exePath`""

cmd /c $compileCmd | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Smoke test compile failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $repoRoot "SherpaOnnx\dlls\x64\Release\sherpa-onnx-c-api.dll") $outDir -Force
Copy-Item (Join-Path $repoRoot "SherpaOnnx\dlls\x64\Release\onnxruntime.dll") $outDir -Force
Copy-Item (Join-Path $repoRoot "SherpaOnnx\dlls\x64\Release\onnxruntime_providers_shared.dll") $outDir -Force

if ($CompileOnly) {
    Write-Host "Compile-only mode complete." -ForegroundColor Green
    exit 0
}

if (!(Test-Path $ModelRoot)) {
    if ($RequireModel) {
        throw "Model root not found: $ModelRoot"
    }
    Write-Host "No model root found. Skipping runtime smoke test." -ForegroundColor Yellow
    exit 0
}

function Get-ModelCandidateRoot {
    param([string]$Root, [string]$PreferredModelId)

    if (![string]::IsNullOrWhiteSpace($PreferredModelId)) {
        $explicit = Join-Path $Root $PreferredModelId
        if (Test-Path $explicit) { return $explicit }
        throw "Requested model ID not found under root: $PreferredModelId"
    }

    $dirs = Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue
    if ($dirs.Count -eq 0) { return $null }

    $preferred = $dirs |
        Sort-Object @{Expression = {
            if ($_.Name -match '^piper-') { 0 }
            elseif ($_.Name -match '^mms_') { 1 }
            elseif ($_.Name -match '^kokoro-') { 2 }
            else { 3 }
        }}, Name |
        Select-Object -First 1

    return $preferred.FullName
}

function Get-EspeakDataDir([string]$rootPath) {
    $dir = Get-ChildItem -Path $rootPath -Recurse -Directory -Filter espeak-ng-data | Select-Object -First 1
    if ($null -eq $dir) { return $null }
    return $dir.FullName
}

function Resolve-ModelConfig {
    param([string]$CandidateRoot)

    $searchRoot = $CandidateRoot
    $subdirs = Get-ChildItem -Path $CandidateRoot -Directory -ErrorAction SilentlyContinue
    if ($subdirs.Count -eq 1) {
        $searchRoot = $subdirs[0].FullName
    }

    $tokens = Get-ChildItem -Path $searchRoot -Recurse -File -Filter tokens.txt | Select-Object -First 1
    if ($null -eq $tokens) {
        return $null
    }

    $voices = Get-ChildItem -Path $searchRoot -Recurse -File -Filter voices.bin | Select-Object -First 1
    if ($voices) {
        $kokoroModel = Get-ChildItem -Path $searchRoot -Recurse -File -Filter *.onnx |
            Where-Object { $_.Name -notmatch 'encoder|decoder|vocoder|acoustic' } |
            Select-Object -First 1
        if ($kokoroModel) {
            return @{
                ModelType = "kokoro"
                Model = $kokoroModel.FullName
                Tokens = $tokens.FullName
                Voices = $voices.FullName
                DataDir = Get-EspeakDataDir -rootPath $searchRoot
            }
        }
    }

    $matchaModel = Get-ChildItem -Path $searchRoot -Recurse -File -Filter "model-steps*.onnx" | Select-Object -First 1
    $matchaVocoder = Get-ChildItem -Path $searchRoot -Recurse -File |
        Where-Object { $_.Name -match '^(vocos|vocoder).+\.onnx$' } |
        Select-Object -First 1
    if ($matchaModel -and $matchaVocoder) {
        return @{
            ModelType = "matcha"
            AcousticModel = $matchaModel.FullName
            Vocoder = $matchaVocoder.FullName
            Tokens = $tokens.FullName
            DataDir = Get-EspeakDataDir -rootPath $searchRoot
        }
    }

    $vitsModel = Get-ChildItem -Path $searchRoot -Recurse -File -Filter *.onnx |
        Where-Object { $_.Name -notmatch 'vocoder|encoder|decoder|acoustic|model-steps|fp16-encoder|fp16-decoder' } |
        Select-Object -First 1
    if ($vitsModel) {
        return @{
            ModelType = "vits"
            Model = $vitsModel.FullName
            Tokens = $tokens.FullName
            DataDir = Get-EspeakDataDir -rootPath $searchRoot
        }
    }

    return $null
}

$candidateRoot = Get-ModelCandidateRoot -Root $ModelRoot -PreferredModelId $ModelId
if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
    if ($RequireModel) { throw "No model directories found under $ModelRoot" }
    Write-Host "No model directory found. Skipping runtime smoke test." -ForegroundColor Yellow
    exit 0
}

$modelConfig = Resolve-ModelConfig -CandidateRoot $candidateRoot
if ($null -eq $modelConfig) {
    if ($RequireModel) { throw "Could not infer model config from: $candidateRoot" }
    Write-Host "Could not infer model config. Skipping runtime smoke test." -ForegroundColor Yellow
    exit 0
}

Write-Host ("Using model root: " + $candidateRoot) -ForegroundColor DarkGray
Write-Host ("Detected model type: " + $modelConfig.ModelType) -ForegroundColor DarkGray

$args = @("--model-type", $modelConfig.ModelType, "--text", "Smoke test from build script.")
switch ($modelConfig.ModelType) {
    "matcha" {
        $args += @("--acoustic-model", $modelConfig.AcousticModel, "--vocoder", $modelConfig.Vocoder, "--tokens", $modelConfig.Tokens)
    }
    "kokoro" {
        $args += @("--model", $modelConfig.Model, "--voices", $modelConfig.Voices, "--tokens", $modelConfig.Tokens)
    }
    default {
        $args += @("--model", $modelConfig.Model, "--tokens", $modelConfig.Tokens)
    }
}
if ($modelConfig.DataDir) {
    $args += @("--data-dir", $modelConfig.DataDir)
}

Write-Host "Running sherpa smoke test..." -ForegroundColor Cyan
Push-Location $outDir
try {
    $argLine = ($args | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }) -join " "
    $proc = Start-Process -FilePath $exePath -ArgumentList $argLine -PassThru -NoNewWindow
    if (-not ($proc.WaitForExit(120000))) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        throw "Smoke test timed out after 120s"
    }
    $exitCode = 0
    try { $exitCode = [int]$proc.ExitCode } catch { $exitCode = 0 }
    if ($exitCode -ne 0) {
        throw "Smoke test failed with exit code $exitCode"
    }
}
finally {
    Pop-Location
}

Write-Host "Sherpa smoke test passed." -ForegroundColor Green
