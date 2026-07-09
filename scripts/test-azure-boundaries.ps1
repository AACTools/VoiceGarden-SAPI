Add-Type -AssemblyName "System.Speech"

$script:count = 0
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Add_SpeakProgress({
    $script:count++
    $w = $args[1]
    if ($script:count -le 9) {
        Write-Host ("  Word {0}: '{1}' pos={2} len={3}" -f $script:count, $w.Text, $w.CharacterPosition, $w.CharacterCount)
    }
})
$synth.SelectVoice("Azure-Jenny")
$out = "$env:TEMP\wb_az_035.wav"
$synth.SetOutputToWaveFile($out)
$synth.Speak("The quick brown fox jumps over the lazy dog.")
$synth.SetOutputToNull()
$sz = [math]::Round((Get-Item $out).Length / 1KB)
Remove-Item $out -EA SilentlyContinue
Write-Host "Total: $($script:count) events, ${sz}KB"
