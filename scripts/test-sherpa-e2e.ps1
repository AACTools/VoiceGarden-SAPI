<#
.SYNOPSIS
    End-to-end test: download SherpaOnnx models, promote to SAPI, speak via COM.
#>
param(
    [string[]]$ModelIds = @("kokoro-en-v0_19", "mms_eng", "piper-en-amy-low")
)

$ErrorActionPreference = "Continue"
$exe = "VoiceGarden.UI\bin\publish\VoiceGarden.UI.exe"
$modelsDir = "$env:LOCALAPPDATA\VoiceGardenSAPIAdapter\models"

function Write-Section($title) {
    Write-Host "`n==============================================================" -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host "==============================================================" -ForegroundColor Cyan
}

function Test-ModelDir($modelId) {
    $dir = Join-Path $modelsDir $modelId
    if (-not (Test-Path $dir)) { return $false }
    $onnx = Get-ChildItem $dir -Filter "*.onnx" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    return $onnx -ne $null
}

# Step 0: Clean slate
Write-Section "Step 0: Clean slate"
.\scripts\cleanup-voices.ps1 2>&1 | Select-String "Deleted|Cleaned|Summary"

# Step 1: Download models via CLI
foreach ($modelId in $ModelIds) {
    Write-Section "Step 1: Download $modelId"
    if (Test-ModelDir $modelId) {
        Write-Host "  Already downloaded, skipping" -ForegroundColor Yellow
        continue
    }
    Write-Host "  Downloading $modelId..."
    & $exe models download $modelId 2>&1 | ForEach-Object { Write-Host "    $_" }
    if (Test-ModelDir $modelId) {
        Write-Host "  OK: model files present" -ForegroundColor Green
    } else {
        Write-Host "  FAILED: no .onnx found" -ForegroundColor Red
    }
}

# Step 2: Verify models installed
Write-Section "Step 2: Verify installed models"
& $exe models list 2>&1 | ForEach-Object { Write-Host "  $_" }

# Step 3: Promote to SAPI
Write-Section "Step 3: Promote to SAPI"
& $exe models promote-all 2>&1 | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Non-admin promote failed (expected). Trying elevated..." -ForegroundColor Yellow
    $regFile = "$env:TEMP\vg_test_promote.reg"
    # Use the app's elevated promote
    Start-Process -FilePath $exe -ArgumentList "models","promote-all" -Verb RunAs -Wait
    Start-Sleep 2
}

# Step 4: Verify registry
Write-Section "Step 4: Verify SAPI tokens in registry"
Write-Host "  HKLM:" -ForegroundColor Yellow
Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens" -ErrorAction SilentlyContinue | 
    Where-Object { $_.PSChildName -match "Sherpa|kokoro|piper|mms" } | 
    ForEach-Object { 
        $default = (Get-ItemProperty $_.PSPath)."(default)"
        $clsid = (Get-ItemProperty $_.PSPath)."CLSID"
        Write-Host "    $($_.PSChildName) = $default (CLSID: $clsid)" -ForegroundColor Green
    }
Write-Host "  HKCU:" -ForegroundColor Yellow
Get-ChildItem "HKCU:\SOFTWARE\Microsoft\Speech\Voices\Tokens" -ErrorAction SilentlyContinue | 
    ForEach-Object { Write-Host "    $($_.PSChildName)" }

# Step 5: Test speaking via SAPI COM
Write-Section "Step 5: Speak test via SAPI COM"
Add-Type -AssemblyName "System.Speech"

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$installedVoices = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name }
Write-Host "  System.Speech sees $($installedVoices.Count) voices:"
$installedVoices | ForEach-Object { Write-Host "    $_" }

foreach ($modelId in $ModelIds) {
    $tokenName = "Sherpa-$modelId"
    Write-Host "`n  Testing: $tokenName" -ForegroundColor Yellow
    
    # Try to select and speak
    try {
        $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
        
        # Try selecting by token name
        $found = $false
        foreach ($v in $synth.GetInstalledVoices()) {
            if ($v.VoiceInfo.Name -match $modelId -or $v.VoiceInfo.Name -match "Sherpa") {
                $synth.SelectVoice($v.VoiceInfo.Name)
                Write-Host "    Selected voice: $($v.VoiceInfo.Name)" -ForegroundColor Green
                $found = $true
                break
            }
        }
        
        if (-not $found) {
            Write-Host "    Voice not found in System.Speech enumerator" -ForegroundColor Red
            continue
        }
        
        $outFile = "$env:TEMP\vg_test_$modelId.wav"
        $synth.SetOutputToWaveFile($outFile)
        $synth.Speak("Hello, this is a test of the $modelId voice.")
        $synth.SetOutputToNull()
        
        $size = (Get-Item $outFile).Length
        if ($size -gt 1000) {
            Write-Host "    Audio generated: $([math]::Round($size/1024))KB" -ForegroundColor Green
        } else {
            Write-Host "    Audio too small or silent: ${size} bytes" -ForegroundColor Red
        }
        Remove-Item $outFile -ErrorAction SilentlyContinue
    } catch {
        Write-Host "    FAILED: $_" -ForegroundColor Red
    }
}

Write-Section "Done"
