<#
.SYNOPSIS
    Tests the PUA sentinel feature for inline SSML/SpeechMarkdown.
    Uses SAPI.SpVoice COM directly to avoid System.Speech text processing.
#>

# Find VoiceGarden voices via SAPI COM
$voice = New-Object -ComObject SAPI.SpVoice
$voices = $voice.GetVoices()
$vgVoice = $null
for ($i = 0; $i -lt $voices.Count; $i++) {
    $v = $voices.Item($i)
    $name = $v.GetDescription()
    if ($name -match "Azure Abbi") {
        $vgVoice = $v
        Write-Host "Found VoiceGarden voice: $name" -ForegroundColor Cyan
        break
    }
}

if (-not $vgVoice) {
    Write-Host "No Azure Abbi voice found. Available:" -ForegroundColor Yellow
    for ($i = 0; $i -lt $voices.Count; $i++) {
        Write-Host "  $($voices.Item($i).GetDescription())"
    }
    exit 1
}

$voice.Voice = $vgVoice
Write-Host "Selected: $($voice.Voice.GetDescription())" -ForegroundColor Green

$sentinel = [char]0xE000 + [char]0xE001
$ssmlMarker = [char]0xE002
$mdMarker = [char]0xE003

# SAPI flags: SVSFPurgeBeforeSpeak = 2, SVSFDefault = 0
$results = @()

# Test 1: Plain text (no sentinel) — baseline
Write-Host "`n=== Test 1: Plain text (baseline) ===" -ForegroundColor Yellow
try {
    $voice.Speak("Hello world", 2)
    Write-Host "  PASS" -ForegroundColor Green
    $results += "PASS"
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    $results += "FAIL"
}

# Test 2: SSML via sentinel
Write-Host "`n=== Test 2: SSML via PUA sentinel ===" -ForegroundColor Yellow
$ssmlText = "$sentinel$ssmlMarker<speak><prosody rate='slow'>Hello from SSML</prosody></speak>"
Write-Host "  Sending: (sentinel + SSML payload)" -ForegroundColor DarkGray
try {
    $voice.Speak($ssmlText, 2)
    Write-Host "  PASS" -ForegroundColor Green
    $results += "PASS"
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    $results += "FAIL"
}

# Test 3: Speech Markdown via sentinel
Write-Host "`n=== Test 3: Speech Markdown via PUA sentinel ===" -ForegroundColor Yellow
$mdText = "$sentinel$mdMarker`Hello from [rate:slow]Speech Markdown[/rate]"
Write-Host "  Sending: (sentinel + SpeechMarkdown payload)" -ForegroundColor DarkGray
try {
    $voice.Speak($mdText, 2)
    Write-Host "  PASS" -ForegroundColor Green
    $results += "PASS"
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    $results += "FAIL"
}

# Test 4: Empty payload after sentinel (should not crash)
Write-Host "`n=== Test 4: Empty payload ===" -ForegroundColor Yellow
try {
    $voice.Speak("$sentinel$ssmlMarker", 2)
    Write-Host "  PASS" -ForegroundColor Green
    $results += "PASS"
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    $results += "FAIL"
}

# Test 5: Regression — plain text still works
Write-Host "`n=== Test 5: Plain text regression ===" -ForegroundColor Yellow
try {
    $voice.Speak("Regression check OK", 2)
    Write-Host "  PASS" -ForegroundColor Green
    $results += "PASS"
} catch {
    Write-Host "  FAIL: $_" -ForegroundColor Red
    $results += "FAIL"
}

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
$pass = ($results | Where-Object { $_ -eq "PASS" }).Count
Write-Host "$pass / $($results.Count) passed"

# Check the log to verify sentinel was detected
Write-Host "`n=== Last log entries ===" -ForegroundColor Cyan
$logDir = "$env:LOCALAPPDATA\VoiceGardenSAPIAdapter"
$latestLog = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestLog) {
    Select-String -Path $latestLog.FullName -Pattern "sentinel|SSML|SpeechMarkdown" | Select-Object -Last 5
} else {
    Write-Host "No log file found"
}
