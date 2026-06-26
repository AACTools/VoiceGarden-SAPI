param(
    [switch]$Force,
    [switch]$CurrentUserOnly
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-ElevatedIfNeeded {
    if (Test-IsAdmin) {
        return
    }

    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    if ($Force) { $argList += "-Force" }
    if ($CurrentUserOnly) { $argList += "-CurrentUserOnly" }

    Write-Host "Relaunching elevated..." -ForegroundColor Yellow
    Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs | Out-Null
    exit 0
}

function Remove-RegKeyIfExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ref]$Removed,
        [ref]$Warnings
    )
    try {
        if (Test-Path $Path) {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            $Removed.Value++
            Write-Host "[removed] $Path" -ForegroundColor Green
        }
    }
    catch {
        $Warnings.Value++
        Write-Host "[warn] failed to remove $Path : $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Remove-TokenSubkeysByPrefix {
    param(
        [Parameter(Mandatory = $true)][string]$TokensRoot,
        [Parameter(Mandatory = $true)][string]$Prefix,
        [ref]$Removed,
        [ref]$Warnings
    )

    try {
        if (-not (Test-Path $TokensRoot)) {
            return
        }
        Get-ChildItem -Path $TokensRoot -ErrorAction Stop |
            Where-Object { $_.PSChildName -like "$Prefix*" } |
            ForEach-Object {
                Remove-RegKeyIfExists -Path $_.PSPath -Removed ([ref]$Removed.Value) -Warnings ([ref]$Warnings.Value)
            }
    }
    catch {
        $Warnings.Value++
        Write-Host "[warn] failed scanning $TokensRoot : $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Remove-PathIfExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ref]$Removed,
        [ref]$Warnings
    )
    try {
        if (Test-Path $Path) {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            $Removed.Value++
            Write-Host "[removed] $Path" -ForegroundColor Green
        }
    }
    catch {
        $Warnings.Value++
        Write-Host "[warn] failed to remove $Path : $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Restart-ElevatedIfNeeded

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NaturalVoice/Sherpa Full Cleanup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not $Force) {
    Write-Host "This will remove:" -ForegroundColor Yellow
    Write-Host "  - HKLM/HKCU Sherpa voice tokens" -ForegroundColor Yellow
    Write-Host "  - NaturalVoice TokenEnums entries" -ForegroundColor Yellow
    Write-Host "  - NaturalVoice COM CLSID registrations" -ForegroundColor Yellow
    Write-Host "  - NaturalVoiceSAPIAdapter app data (models/log/cache)" -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host "Type YES to continue"
    if ($confirm -ne "YES") {
        Write-Host "Cancelled." -ForegroundColor Yellow
        exit 1
    }
}

$removed = 0
$warnings = 0

# 1) Remove Sherpa tokens from HKLM/HKCU speech token roots.
$tokenRoots = @(
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens",
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens",
    "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\Tokens"
)
foreach ($root in $tokenRoots) {
    Remove-TokenSubkeysByPrefix -TokensRoot $root -Prefix "Sherpa-" -Removed ([ref]$removed) -Warnings ([ref]$warnings
    )
}

# 2) Remove TokenEnums hook keys.
$tokenEnumKeys = @(
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator",
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator",
    "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\TokenEnums\NaturalVoiceEnumerator"
)
foreach ($k in $tokenEnumKeys) {
    Remove-RegKeyIfExists -Path $k -Removed ([ref]$removed) -Warnings ([ref]$warnings)
}

# 3) Remove COM class registrations used by adapter.
$clsids = @(
    "{013ab33b-ad1a-401c-8bee-f6e2b046a94e}", # TTSEngine
    "{b8b9e38f-e5a2-4661-9fde-4ac7377aa6f6}"  # VoiceTokenEnumerator
)
foreach ($clsid in $clsids) {
    $clsidPaths = @(
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$clsid",
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Classes\CLSID\$clsid",
        "Registry::HKEY_CURRENT_USER\SOFTWARE\Classes\CLSID\$clsid"
    )
    foreach ($p in $clsidPaths) {
        Remove-RegKeyIfExists -Path $p -Removed ([ref]$removed) -Warnings ([ref]$warnings)
    }
}

# 4) Remove adapter config keys.
$configKeys = @(
    "Registry::HKEY_CURRENT_USER\SOFTWARE\NaturalVoiceSAPIAdapter",
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\NaturalVoiceSAPIAdapter"
)
foreach ($k in $configKeys) {
    Remove-RegKeyIfExists -Path $k -Removed ([ref]$removed) -Warnings ([ref]$warnings)
}

# 5) Remove app data folders.
if ($CurrentUserOnly) {
    $paths = @(
        Join-Path $env:LOCALAPPDATA "NaturalVoiceSAPIAdapter"
    )
}
else {
    $paths = @()
    Get-ChildItem "C:\Users" -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $candidate = Join-Path $_.FullName "AppData\Local\NaturalVoiceSAPIAdapter"
        $paths += $candidate
    }
}

foreach ($p in ($paths | Select-Object -Unique)) {
    Remove-PathIfExists -Path $p -Removed ([ref]$removed) -Warnings ([ref]$warnings)
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Cleanup Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Removed items: $removed" -ForegroundColor White
Write-Host "Warnings: $warnings" -ForegroundColor White
Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "1) Run Installer.exe and register desired bitness" -ForegroundColor Yellow
Write-Host "2) Run SherpaOnnxConfig.exe rescan" -ForegroundColor Yellow
Write-Host "3) Run scripts\sapi-probe.ps1 -VoiceId piper-en-alan-low" -ForegroundColor Yellow
