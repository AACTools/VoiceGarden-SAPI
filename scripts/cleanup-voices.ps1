<#
.SYNOPSIS
    Remove all VoiceGarden/Sherpa/Cloud SAPI voice tokens from the registry.
.DESCRIPTION
    Wipes Sherpa-* and Cloud-* tokens from HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens
    so you can test from a clean state. Built-in Microsoft voices (TTS_MS_*) are kept.
.PARAMETER DryRun
    Show what would be deleted without actually deleting.
.EXAMPLE
    .\cleanup-voices.ps1
    .\cleanup-voices.ps1 -DryRun
#>
param(
    [switch]$DryRun
)

$tokensRoot = "HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens"

# Also check WOW6432Node for 32-bit registrations
$wowTokensRoot = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens"

# Also check HKCU (promoted voices can end up here too)
$hkcuTokensRoot = "HKCU:\SOFTWARE\Microsoft\Speech\Voices\Tokens"

$prefixes = @("Sherpa-", "Cloud-", "NaturalVoice-", "eSpeak")
# Note: eSpeak voices are also from our adapter in 32-bit land
$deleted = 0
$kept = 0

foreach ($root in @($tokensRoot, $wowTokensRoot, $hkcuTokensRoot)) {
    if (-not (Test-Path $root)) { continue }

    $children = Get-ChildItem $root -ErrorAction SilentlyContinue
    foreach ($child in $children) {
        $name = $child.PSChildName
        $isOurs = $false
        foreach ($prefix in $prefixes) {
            if ($name -like "$prefix*") { $isOurs = $true; break }
        }

        if ($isOurs) {
            $hive = if ($root -like "*WOW6432Node*") { "WOW6432Node" }
                    elseif ($root -like "*HKCU*") { "HKCU" }
                    else { "HKLM" }
            if ($DryRun) {
                Write-Host "  [DRY-RUN] Would delete: $name ($hive)" -ForegroundColor Yellow
            } else {
                try {
                    Remove-Item $child.PSPath -Recurse -Force
                    Write-Host "  Deleted: $name ($hive)" -ForegroundColor Green
                } catch {
                    Write-Host "  FAILED:  $name ($hive) - $_" -ForegroundColor Red
                }
            }
            $deleted++
        } else {
            $kept++
        }
    }
}

# Also clean up old NaturalVoice registry keys
$vgRoots = @(
    "HKLM:\SOFTWARE\VoiceGardenSAPIAdapter",
    "HKLM:\SOFTWARE\NaturalVoiceSAPIAdapter",
    "HKCU:\SOFTWARE\VoiceGardenSAPIAdapter",
    "HKCU:\SOFTWARE\NaturalVoiceSAPIAdapter"
)

foreach ($key in $vgRoots) {
    if (Test-Path $key) {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would delete registry key: $key" -ForegroundColor Yellow
        } else {
            try {
                Remove-Item $key -Recurse -Force
                Write-Host "  Cleaned: $key" -ForegroundColor Green
            } catch {
                Write-Host "  FAILED:  $key - $_" -ForegroundColor Red
            }
        }
    }
}

Write-Host ""
$action = if ($DryRun) { "would be" } else { "were" }
Write-Host "Summary: $deleted voice token(s) $action removed, $kept built-in voice(s) kept" -ForegroundColor Cyan
if (-not $DryRun -and $deleted -gt 0) {
    Write-Host "Clean state. Restart any SAPI clients (Grid3) to pick up changes." -ForegroundColor Cyan
}
