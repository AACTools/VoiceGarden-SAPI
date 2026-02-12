param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

$ttsClsid = "{013ab33b-ad1a-401c-8bee-f6e2b046a94e}"
$enumClsid = "{b8b9e38f-e5a2-4661-9fde-4ac7377aa6f6}"

function Read-RegString([Microsoft.Win32.RegistryHive]$hive, [Microsoft.Win32.RegistryView]$view, [string]$subKey, [string]$valueName = "") {
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            $key = $base.OpenSubKey($subKey, $false)
            if ($null -eq $key) { return "" }
            try {
                $value = $key.GetValue($valueName, "")
                if ($null -eq $value) { return "" }
                return [string]$value
            } finally {
                $key.Dispose()
            }
        } finally {
            $base.Dispose()
        }
    } catch {
        return ""
    }
}

$requiredChecks = @(
    @{
        Name = "HKLM x64 TTS InprocServer32"
        Value = Read-RegString LocalMachine Registry64 "SOFTWARE\Classes\CLSID\$ttsClsid\InprocServer32"
    },
    @{
        Name = "HKLM x64 Enumerator InprocServer32"
        Value = Read-RegString LocalMachine Registry64 "SOFTWARE\Classes\CLSID\$enumClsid\InprocServer32"
    },
    @{
        Name = "HKLM x64 TokenEnums CLSID"
        Value = Read-RegString LocalMachine Registry64 "SOFTWARE\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator" "CLSID"
    }
)

$optionalChecks = @(
    @{
        Name = "HKCU TokenEnums CLSID"
        Value = Read-RegString CurrentUser Default "Software\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator" "CLSID"
    }
)

$failed = $false
foreach ($check in $requiredChecks) {
    if ([string]::IsNullOrWhiteSpace($check.Value)) {
        Write-Host ("[FAIL] {0}: <missing>" -f $check.Name) -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host ("[OK]   {0}: {1}" -f $check.Name, $check.Value) -ForegroundColor Green
    }
}

foreach ($check in $optionalChecks) {
    if ([string]::IsNullOrWhiteSpace($check.Value)) {
        Write-Host ("[WARN] {0}: <missing> (optional)" -f $check.Name) -ForegroundColor Yellow
    } else {
        Write-Host ("[OK]   {0}: {1}" -f $check.Name, $check.Value) -ForegroundColor Green
    }
}

if ($VerboseOutput) {
    Write-Host ""
    Write-Host "Tip: run Installer.exe as Administrator and click Register 64-bit if HKLM entries are missing." -ForegroundColor Yellow
}

if ($failed) {
    exit 1
}

exit 0
