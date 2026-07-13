<#
.SYNOPSIS
    Reset VoiceGarden SAPI to a clean state so you can demo the full install walkthrough.

.DESCRIPTION
    Uninstalls the VoiceGardenSAPI MSI and removes ALL per-user/per-machine state so the
    app behaves as a fresh install:
      - running VoiceGarden / SherpaOnnxConfig processes are stopped
      - the VoiceGardenSAPI MSI is uninstalled (removes Program Files install + COM CLSIDs)
      - app data folders are deleted (downloaded models, API-key/credential store, settings, logs)
      - HKCU\Software\VoiceGardenSAPIAdapter is deleted  -> this re-triggers onboarding/walkthrough
      - promoted SAPI voice tokens named "Sherpa-*" are removed (HKLM + HKCU, 64/32-bit)
      - the VoiceGarden SAPI token enumerator is removed (HKLM + HKCU)

    Other SAPI5 voices are NEVER touched: only children named "Sherpa-*" are deleted under
    the Tokens roots, and only the named "VoiceGardenEnumerator" key under TokenEnums.

.PARAMETER DryRun
    Print exactly what would be changed, without changing anything. Does not need admin.

.PARAMETER Force
    Skip the confirmation prompt.

.PARAMETER KeepMsi
    Do not uninstall the app. Reset state only (onboarding, models, promoted voices).

.PARAMETER KeepAppData
    Keep the app data folders (models, credentials, settings, logs).

.EXAMPLE
    # Preview first (no admin needed, changes nothing):
    .\Reset-VoiceGarden.ps1 -DryRun

    # Full wipe for the demo:
    .\Reset-VoiceGarden.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch] $DryRun,
    [switch] $Force,
    [switch] $KeepMsi,
    [switch] $KeepAppData
)

$ErrorActionPreference = 'Stop'
$ProductDisplayName = 'VoiceGardenSAPI'

# ---------- helpers ----------

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Get-RegKey($hive, $view, $path) {
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        return $base.OpenSubKey($path, $false)
    } catch { return $null }
}

function Test-RegKey($hive, $view, $path) { [bool](Get-RegKey $hive $view $path) }

function Remove-RegKey($hive, $view, $path, [string]$label) {
    if (-not (Test-RegKey $hive $view $path)) {
        Write-Host "  [-] $label : not present" -ForegroundColor DarkGray
        return
    }
    if ($DryRun) {
        Write-Host "  [DRY] would delete $label" -ForegroundColor Yellow
        return
    }
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        $base.DeleteSubKeyTree($path, $false)
        Write-Host "  [x] deleted $label" -ForegroundColor Green
    } catch {
        Write-Host "  [!] failed $label : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Remove-SherpaTokens($hive, $view, [string]$label) {
    $root = Get-RegKey $hive $view 'SOFTWARE\Microsoft\Speech\Voices\Tokens'
    if ($null -eq $root) {
        Write-Host "  [-] $label : Tokens root not present" -ForegroundColor DarkGray
        return
    }
    $sherpa = @($root.GetSubKeyNames() | Where-Object { $_ -like 'Sherpa-*' })
    if ($sherpa.Count -eq 0) {
        Write-Host "  [-] $label : no Sherpa-* tokens" -ForegroundColor DarkGray
        return
    }
    foreach ($t in $sherpa) {
        $full = "SOFTWARE\Microsoft\Speech\Voices\Tokens\$t"
        if ($DryRun) {
            Write-Host "  [DRY] would delete $label \$t" -ForegroundColor Yellow
        } else {
            try {
                $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
                $base.DeleteSubKeyTree($full, $false)
                Write-Host "  [x] deleted $label \$t" -ForegroundColor Green
            } catch {
                Write-Host "  [!] failed $label \$t : $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

function Find-MsiProductCode {
    foreach ($pair in @(
        @{ Hive = [Microsoft.Win32.RegistryHive]::LocalMachine; View = [Microsoft.Win32.RegistryView]::Registry64 },
        @{ Hive = [Microsoft.Win32.RegistryHive]::LocalMachine; View = [Microsoft.Win32.RegistryView]::Registry32 },
        @{ Hive = [Microsoft.Win32.RegistryHive]::CurrentUser;  View = [Microsoft.Win32.RegistryView]::Registry64 }
    )) {
        $uninstall = Get-RegKey $pair.Hive $pair.View 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
        if (-not $uninstall) { continue }
        foreach ($sub in $uninstall.GetSubKeyNames()) {
            $k = $uninstall.OpenSubKey($sub, $false)
            if (-not $k) { continue }
            $dn = $k.GetValue('DisplayName')
            if ($dn -ieq $ProductDisplayName) {
                if ($sub -match '^\{[0-9A-Fa-f\-]{36}\}$') { return $sub }
                $us = $k.GetValue('UninstallString')
                if ($us -match '\{[0-9A-Fa-f\-]{36}\}') { return $matches[0] }
            }
        }
    }
    return $null
}

function Stop-VoiceGardenProcesses {
    $names = 'VoiceGarden.UI', 'SherpaOnnxConfig', 'setup'
    $procs = Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName }
    if (-not $procs) {
        Write-Host "  [-] no running VoiceGarden processes" -ForegroundColor DarkGray
        return
    }
    foreach ($p in $procs) {
        if ($DryRun) {
            Write-Host "  [DRY] would stop process $($p.ProcessName) (pid $($p.Id))" -ForegroundColor Yellow
        } else {
            try { $p | Stop-Process -Force -ErrorAction Stop; Write-Host "  [x] stopped $($p.ProcessName) (pid $($p.Id))" -ForegroundColor Green }
            catch { Write-Host "  [!] could not stop $($p.ProcessName): $($_.Exception.Message)" -ForegroundColor Red }
        }
    }
}

# ---------- elevation ----------

if (-not $DryRun -and -not (Test-Admin)) {
    Write-Host "Not running as admin. Re-launching elevated..." -ForegroundColor Cyan
    $args = @()
    if ($Force)        { $args += '-Force' }
    if ($KeepMsi)      { $args += '-KeepMsi' }
    if ($KeepAppData)  { $args += '-KeepAppData' }
    Start-Process -FilePath 'powershell.exe' `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $($args -join ' ')" `
        -Verb RunAs
    exit
}

# ---------- banner / dry-run summary ----------

Write-Host ""
Write-Host "=== VoiceGarden SAPI reset ===" -ForegroundColor Cyan
if ($DryRun) { Write-Host "(DRY RUN - nothing will be changed)" -ForegroundColor Yellow }
Write-Host ""

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$appDataRoots = @('VoiceGardenSAPI', 'VoiceGardenSAPIAdapter', 'VoiceGardensSAPIAdapter') |
    ForEach-Object { Join-Path $localAppData $_ }

$productCode = Find-MsiProductCode

Write-Host "Plan:" -ForegroundColor White
Write-Host ("  MSI product '{0}' : {1}" -f $ProductDisplayName, $(if ($productCode) { $productCode } else { 'not installed' }))
Write-Host ("  AppData folders  : {0}" -f ($appDataRoots -join '; '))
Write-Host ("  HKCU app key     : Software\VoiceGardenSAPIAdapter")
Write-Host ("  SAPI Sherpa-*    : HKLM (64/32-bit) + HKCU")
Write-Host ("  Token enum       : VoiceGardenEnumerator (HKLM + HKCU)")
Write-Host ""

if (-not $DryRun -and -not $Force) {
    $ans = Read-Host "Proceed with full wipe? [y/N]"
    if ($ans -notmatch '^[yY]') { Write-Host "Cancelled." -ForegroundColor Yellow; exit }
}

# ---------- execute ----------

Write-Host "`n[1/5] Stopping processes..." -ForegroundColor Cyan
Stop-VoiceGardenProcesses

Write-Host "`n[2/5] Uninstalling MSI..." -ForegroundColor Cyan
if ($KeepMsi) {
    Write-Host "  [-] -KeepMsi set, skipping uninstall" -ForegroundColor DarkGray
} elseif (-not $productCode) {
    Write-Host "  [-] no installed '$ProductDisplayName' MSI found" -ForegroundColor DarkGray
} elseif ($DryRun) {
    Write-Host "  [DRY] would run: msiexec /x $productCode /passive" -ForegroundColor Yellow
} else {
    Write-Host "  running msiexec /x $productCode /passive ..." -ForegroundColor DarkGray
    $p = Start-Process msiexec -ArgumentList "/x `"$productCode`" /passive" -Wait -PassThru
    Write-Host ("  exit code: {0}" -f $p.ExitCode)
}

Write-Host "`n[3/5] Removing app data..." -ForegroundColor Cyan
if ($KeepAppData) {
    Write-Host "  [-] -KeepAppData set, keeping app data" -ForegroundColor DarkGray
} else {
    foreach ($root in $appDataRoots) {
        if (Test-Path -LiteralPath $root) {
            if ($DryRun) {
                Write-Host "  [DRY] would delete $root" -ForegroundColor Yellow
            } else {
                try { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction Stop; Write-Host "  [x] deleted $root" -ForegroundColor Green }
                catch { Write-Host "  [!] failed $root : $($_.Exception.Message)" -ForegroundColor Red }
            }
        } else {
            Write-Host "  [-] not present: $root" -ForegroundColor DarkGray
        }
    }
}

Write-Host "`n[4/5] Removing HKCU app key..." -ForegroundColor Cyan
Remove-RegKey ([Microsoft.Win32.RegistryHive]::CurrentUser) ([Microsoft.Win32.RegistryView]::Default) `
    'Software\VoiceGardenSAPIAdapter' 'HKCU\Software\VoiceGardenSAPIAdapter'

Write-Host "`n[5/5] Removing promoted SAPI tokens + enumerator..." -ForegroundColor Cyan
# Sherpa-* voice tokens
Remove-SherpaTokens ([Microsoft.Win32.RegistryHive]::LocalMachine) ([Microsoft.Win32.RegistryView]::Registry64) 'HKLM64 Tokens'
Remove-SherpaTokens ([Microsoft.Win32.RegistryHive]::LocalMachine) ([Microsoft.Win32.RegistryView]::Registry32) 'HKLM32 Tokens'
Remove-SherpaTokens ([Microsoft.Win32.RegistryHive]::CurrentUser)  ([Microsoft.Win32.RegistryView]::Default)  'HKCU  Tokens'
# Token enumerator (named key only; parent TokenEnums is never deleted)
Remove-RegKey ([Microsoft.Win32.RegistryHive]::LocalMachine) ([Microsoft.Win32.RegistryView]::Registry64) `
    'SOFTWARE\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator' 'HKLM TokenEnums\VoiceGardenEnumerator'
Remove-RegKey ([Microsoft.Win32.RegistryHive]::CurrentUser) ([Microsoft.Win32.RegistryView]::Default) `
    'SOFTWARE\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator' 'HKCU TokenEnums\VoiceGardenEnumerator'

Write-Host "`n=== Done ===" -ForegroundColor Green
if ($DryRun) { Write-Host "Dry run complete. Re-run without -DryRun to apply." -ForegroundColor Yellow }
else {
    Write-Host "VoiceGarden has been reset. Restart target AAC apps if they were running," -ForegroundColor White
    Write-Host "then run your installer for a fresh walkthrough." -ForegroundColor White
}
Write-Host ""
