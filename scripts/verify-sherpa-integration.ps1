param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Write-Host 'Sherpa integration verification' -ForegroundColor Cyan

if (-not $SkipBuild) {
    Write-Host '[1/3] Building SherpaOnnxConfig...' -ForegroundColor Yellow
    dotnet build SherpaOnnxConfig\SherpaOnnxConfig.csproj -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

Write-Host '[2/3] Running CLI rescan...' -ForegroundColor Yellow
$output = & dotnet run --project SherpaOnnxConfig\SherpaOnnxConfig.csproj --configuration Release -- rescan 2>&1
$output | Out-Host
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0 -and $exitCode -ne 2) {
    throw "Unexpected rescan exit code: $exitCode"
}

Write-Host '[3/3] Verifying path consistency...' -ForegroundColor Yellow
$pathChecks = @(
    'SherpaOnnxConfig\\MainForm.cs',
    'SherpaOnnx\\SherpaOnnxModels.cpp',
    'Installer\\Installer.rc'
)
foreach ($f in $pathChecks) {
    $content = Get-Content $f -Raw
    if ($content -match 'OpenSpeech\\\\models|OpenSpeech/models') {
        throw "Found legacy OpenSpeech model path in $f"
    }
}

Write-Host "Verification complete. rescan exit code=$exitCode (0=no issues, 2=model issues found)." -ForegroundColor Green
