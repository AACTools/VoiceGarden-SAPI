Add-Type -AssemblyName "System.Speech"

Write-Host "=== Word Boundary Test (real-time playback) ===" -ForegroundColor Cyan
Write-Host "You will HEAR the speech. Watch the word highlighting below." -ForegroundColor Yellow

foreach ($voice in @("Azure-Jenny", "kokoro-en-v0_19")) {
    Write-Host "`n--- $voice ---" -ForegroundColor Yellow
    $script:count = 0
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $synth.SelectVoice($voice)
    $synth.Add_SpeakProgress({
        $script:count++
        $w = $args[1]
        Write-Host ("  [{0,5}ms] '{1}'" -f [math]::Round($w.AudioPosition.TotalMilliseconds), $w.Text) -NoNewline
        Write-Host ""
    })
    try {
        Write-Host "  Speaking..."
        $synth.Speak("The quick brown fox jumps over the lazy dog.")
        Write-Host "  Total: $($script:count) word events"
    } catch {
        Write-Host "  ERROR: $_"
    }
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
