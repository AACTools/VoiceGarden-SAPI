Add-Type -AssemblyName "System.Speech"

function Resolve-VoiceName([string]$wanted) {
    # Promoted Sherpa voices are named by catalog name (e.g. 'v0_19') when
    # promoted via SherpaOnnxConfig, or by model id when promoted via
    # VoiceGarden.UI — accept either.
    $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $hit = $s.GetInstalledVoices() | Where-Object {
        $_.VoiceInfo.Name -eq $wanted -or $_.VoiceInfo.Description -like "*$wanted*" -or $wanted -like "*$($_.VoiceInfo.Name)*"
    } | Select-Object -First 1
    if ($hit) { return $hit.VoiceInfo.Name }
    return $wanted
}

Write-Host "=== Boundary Crash Reproduction Test ===" -ForegroundColor Cyan
Write-Host "This test uses PromptBuilder with prosody changes (like Grid3 does)"
Write-Host "to verify boundary events don't crash System.Speech."
Write-Host ""

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer

foreach ($voiceName in @("kokoro-en-v0_19", "Azure-Jenny")) {
    Write-Host "--- Testing: $voiceName ---" -ForegroundColor Yellow

    try {
        $synth.SelectVoice((Resolve-VoiceName $voiceName))
    } catch {
        Write-Host "  Voice not available, skipping"
        continue
    }

    # Test 1: Plain text (should always work)
    Write-Host "  Test 1: Plain text..."
    try {
        $out = "$env:TEMP\boundary_test_plain.wav"
        $synth.SetOutputToWaveFile($out)
        $synth.Speak("The quick brown fox jumps over the lazy dog.")
        $synth.SetOutputToNull()
        Write-Host "    OK ($([math]::Round((Get-Item $out).Length/1KB))KB)" -ForegroundColor Green
        Remove-Item $out -EA SilentlyContinue
    } catch {
        Write-Host "    CRASH: $_" -ForegroundColor Red
    }

    # Test 2: PromptBuilder with rate change (like Grid3)
    Write-Host "  Test 2: PromptBuilder with rate change..."
    try {
        $slowStyle = New-Object System.Speech.Synthesis.PromptStyle
        $slowStyle.Rate = [System.Speech.Synthesis.PromptRate]::Slow
        $fastStyle = New-Object System.Speech.Synthesis.PromptStyle
        $fastStyle.Rate = [System.Speech.Synthesis.PromptRate]::Fast

        $pb = New-Object System.Speech.Synthesis.PromptBuilder
        $pb.StartStyle($slowStyle)
        $pb.AppendText("The quick brown fox")
        $pb.EndStyle()
        $pb.AppendText(" jumps over ")
        $pb.StartStyle($fastStyle)
        $pb.AppendText("the lazy dog.")
        $pb.EndStyle()

        $out2 = "$env:TEMP\boundary_test_prompt.wav"
        $synth.SetOutputToWaveFile($out2)
        $synth.Speak($pb)
        $synth.SetOutputToNull()
        Write-Host "    OK ($([math]::Round((Get-Item $out2).Length/1KB))KB)" -ForegroundColor Green
        Remove-Item $out2 -EA SilentlyContinue
    } catch {
        Write-Host "    CRASH: $_" -ForegroundColor Red
    }

    # Test 3: Synchronous speak with SpeakProgress event handler
    Write-Host "  Test 3: SpeakProgress event handler..."
    try {
        $script:eventCount = 0
        $script:events = @()
        $synth.Add_SpeakProgress({
            $script:eventCount++
            $w = $args[1]
            if ($script:eventCount -le 5) {
                $script:events += "  Word: '$($w.Text)' pos=$($w.CharacterPosition) len=$($w.CharacterCount)"
            }
        })
        
        $out3 = "$env:TEMP\boundary_test_events.wav"
        $synth.SetOutputToWaveFile($out3)
        $synth.Speak("Hello world this is a test.")
        $synth.SetOutputToNull()
        
        Write-Host "    Events: $($script:eventCount)"
        if ($script:events) {
            $script:events | ForEach-Object { Write-Host "      $_" }
        }
        Write-Host "    OK" -ForegroundColor Green
        Remove-Item $out3 -EA SilentlyContinue
        
        # Remove handler for next test
        $synth.Remove_SpeakProgress($null)
    } catch {
        Write-Host "    CRASH: $_" -ForegroundColor Red
    }

    # Test 4: Rapid successive speaks (race condition test)
    Write-Host "  Test 4: Rapid successive speaks..."
    try {
        $out4 = "$env:TEMP\boundary_test_rapid.wav"
        $synth.SetOutputToWaveFile($out4)
        $synth.Speak("Hi.")
        $synth.Speak("Hello.")
        $synth.Speak("Hey there.")
        $synth.SetOutputToNull()
        Write-Host "    OK" -ForegroundColor Green
        Remove-Item $out4 -EA SilentlyContinue
    } catch {
        Write-Host "    CRASH: $_" -ForegroundColor Red
    }
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
