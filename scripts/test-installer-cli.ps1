param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Run-Step([string]$name, [string]$cmd) {
    Write-Host "[test] $name"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) {
        throw "$name failed with exit code $LASTEXITCODE"
    }
}

Set-Location $RepoRoot

if (-not (Test-Path ".\out\Installer.exe")) {
    throw "Missing .\out\Installer.exe. Run build-all first."
}

$tmp = Join-Path $RepoRoot "tmp"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$plan = @"
{
  "version": 1,
  "scope": "current-user",
  "architectures": ["x64"],
  "engines": {
    "azure_online": { "enabled": false },
    "sherpa_offline": { "enabled": true, "rescan": true }
  },
  "post_install": {
    "register_com": false,
    "verify_registration": false,
    "run_self_test": false
  }
}
"@
$planPath = Join-Path $tmp "install-plan-ci-smoke.json"
Set-Content -Path $planPath -Value $plan -Encoding UTF8

Run-Step "help" "out\Installer.exe --help"
Run-Step "plan dry-run json" "out\Installer.exe --json --dry-run --plan `"$planPath`""
Run-Step "direct dry-run json" "out\Installer.exe --json --dry-run --scope current-user --arch x64 --engine sherpa --sherpa-rescan"
Run-Step "plan execute silent json" "out\Installer.exe --silent --json --plan `"$planPath`""
Run-Step "plan positional dry-run json" "out\Installer.exe --json --dry-run `"$planPath`""

$fixtureDir = Join-Path $RepoRoot "samples\install-plans"
if (Test-Path $fixtureDir) {
    Get-ChildItem $fixtureDir -Filter *.json | Sort-Object Name | ForEach-Object {
        Run-Step "fixture dry-run $($_.Name)" "out\Installer.exe --json --dry-run --plan `"$($_.FullName)`""
    }
}

$runnerExe = Join-Path $RepoRoot "out\InstallPlanRunner.exe"
if (Test-Path $runnerExe) {
    $runnerDir = Join-Path $tmp "runner-smoke"
    New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
    Copy-Item $runnerExe (Join-Path $runnerDir "InstallPlanRunner.exe") -Force
    Copy-Item (Join-Path $fixtureDir "web-minimal.json") (Join-Path $runnerDir "install-plan.json") -Force
    Run-Step "install-plan-runner auto plan json" "`"$runnerDir\InstallPlanRunner.exe`" --json"
}

Write-Host "[test] Installer CLI regression checks passed."
