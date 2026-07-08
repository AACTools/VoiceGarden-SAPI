<#
.SYNOPSIS
    Cleanup script run during MSI uninstall.
    Unregisters COM DLLs and removes VoiceGarden voice tokens.
#>

$adapterDirs = @(
    "$env:ProgramFiles\VoiceGardenSAPI\x64",
    "${env:ProgramFiles(x86)}\VoiceGardenSAPI\x64",
    "$env:ProgramFiles\VoiceGardenSAPI\x86",
    "${env:ProgramFiles(x86)}\VoiceGardenSAPI\x86"
)

foreach ($dir in $adapterDirs) {
    $dll = Join-Path $dir "VoiceGardenSAPIAdapter.dll"
    if (Test-Path $dll) {
        Start-Process regsvr32 -ArgumentList "/u", "/s", $dll -Wait -ErrorAction SilentlyContinue
    }
}

# Remove VoiceGarden voice tokens from HKLM
$hives = @(
    "HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens"
)
$prefixes = @("Sherpa-", "Cloud-", "Edge-", "NaturalVoice-")

foreach ($hive in $hives) {
    if (Test-Path $hive) {
        Get-ChildItem $hive -ErrorAction SilentlyContinue | ForEach-Object {
            foreach ($prefix in $prefixes) {
                if ($_.PSChildName -like "$prefix*") {
                    Remove-Item $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
}

# Remove HKCU enumerator config
Remove-Item "HKCU:\SOFTWARE\VoiceGardenSAPIAdapter" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "HKCU:\SOFTWARE\NaturalVoiceSAPIAdapter" -Recurse -Force -ErrorAction SilentlyContinue
