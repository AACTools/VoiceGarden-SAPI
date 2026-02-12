param(
    [switch]$CompileOnly
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$outDir = Join-Path $repoRoot "out\vanilla-amy-test"
$exePath = Join-Path $outDir "sherpa-vanilla-amy-test.exe"

if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) {
    throw "vswhere.exe not found."
}

$vsInstall = & $vswhere -latest -products * -property installationPath
if ([string]::IsNullOrWhiteSpace($vsInstall)) {
    throw "Visual Studio installation not found."
}

$vcvars = Join-Path $vsInstall "VC\Auxiliary\Build\vcvars64.bat"
if (!(Test-Path $vcvars)) {
    throw "vcvars64.bat not found at $vcvars"
}

$src = Join-Path $repoRoot "scripts\sherpa-vanilla-amy-test.c"
$inc = Join-Path $repoRoot "SherpaOnnx\libs\sherpa-onnx-v1.12.23-win-x64-shared\include"
$libDir = Join-Path $repoRoot "SherpaOnnx\libs\sherpa-onnx-v1.12.23-win-x64-shared-Release\lib"

Write-Host "Compiling vanilla sherpa C test..." -ForegroundColor Cyan
$cmd = "call `"$vcvars`" && cl /nologo /O2 /EHsc /I`"$inc`" `"$src`" /link /LIBPATH:`"$libDir`" sherpa-onnx-c-api.lib /OUT:`"$exePath`""
cmd /c $cmd | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Compile failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $libDir "sherpa-onnx-c-api.dll") $outDir -Force
Copy-Item (Join-Path $libDir "onnxruntime.dll") $outDir -Force
Copy-Item (Join-Path $libDir "onnxruntime_providers_shared.dll") $outDir -Force

if ($CompileOnly) {
    Write-Host "Compile-only mode complete." -ForegroundColor Green
    exit 0
}

Write-Host "Running vanilla sherpa C test..." -ForegroundColor Cyan
Push-Location $outDir
try {
    $proc = Start-Process -FilePath $exePath -PassThru -NoNewWindow
    if (-not $proc.WaitForExit(120000)) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        throw "Vanilla test timed out after 120s"
    }
    if ($proc.ExitCode -ne 0) {
        throw "Vanilla test failed with exit code $($proc.ExitCode)"
    }
}
finally {
    Pop-Location
}

Write-Host "Vanilla test passed." -ForegroundColor Green
