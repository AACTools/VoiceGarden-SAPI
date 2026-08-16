Add-Type -AssemblyName "System.Speech"

Write-Host "=== Word Boundary Test ===" -ForegroundColor Cyan
$text = "The quick brown fox jumps over the lazy dog."

foreach ($voice in @("Azure-Jenny", "kokoro-en-v0_19", "Microsoft David Desktop")) {
    Write-Host "`n--- $voice ---" -ForegroundColor Yellow
    $count = 0
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $script:count = 0
    $synth.Add_SpeakProgress({
        $script:count++
        if ($script:count -le 5) {
            Write-Host "  Word $script:count`: '$($args[1].Text)' at $([math]::Round($args[1].AudioPosition.TotalMilliseconds))ms"
        }
    })
    try {
        $synth.SelectVoice($voice)
        $out = "$env:TEMP\wbtest.wav"
        $synth.SetOutputToWaveFile($out)
        $synth.Speak($text)
        $synth.SetOutputToNull()
        Start-Sleep -Milliseconds 500
        $sz = [math]::Round((Get-Item $out).Length / 1KB)
        Remove-Item $out -EA SilentlyContinue
        $status = if ($script:count -gt 0) { "OK" } else { "NO EVENTS" }
        Write-Host "  Total: $($script:count) word events, audio=${sz}KB — $status"
    } catch {
        Write-Host "  ERROR: $_"
    }
}
