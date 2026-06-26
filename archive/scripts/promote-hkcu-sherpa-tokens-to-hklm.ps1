param(
    [switch]$ClearHklm,
    [string]$ImportFile = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Export-HkcuSherpaTokens {
    $subPath = "SOFTWARE\Microsoft\Speech\Voices\Tokens"
    $rootKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subPath)
    if ($null -eq $rootKey) { return @() }

    $tokens = @()
    foreach ($tokenName in $rootKey.GetSubKeyNames()) {
        if ($tokenName -notlike "Sherpa-*") { continue }

        $tokenKey = $rootKey.OpenSubKey($tokenName)
        if ($null -eq $tokenKey) { continue }

        $entry = [ordered]@{
            TokenName = $tokenName
            RootValues = @()
            Attributes = @()
            NaturalVoiceConfig = @()
        }

        foreach ($name in $tokenKey.GetValueNames()) {
            $kind = $tokenKey.GetValueKind($name).ToString()
            $entry.RootValues += [ordered]@{ Name = $name; Value = $tokenKey.GetValue($name); Kind = $kind }
        }

        $attrKey = $tokenKey.OpenSubKey("Attributes")
        if ($null -ne $attrKey) {
            foreach ($name in $attrKey.GetValueNames()) {
                $kind = $attrKey.GetValueKind($name).ToString()
                $entry.Attributes += [ordered]@{ Name = $name; Value = $attrKey.GetValue($name); Kind = $kind }
            }
            $attrKey.Close()
        }

        $cfgKey = $tokenKey.OpenSubKey("NaturalVoiceConfig")
        if ($null -ne $cfgKey) {
            foreach ($name in $cfgKey.GetValueNames()) {
                $kind = $cfgKey.GetValueKind($name).ToString()
                $entry.NaturalVoiceConfig += [ordered]@{ Name = $name; Value = $cfgKey.GetValue($name); Kind = $kind }
            }
            $cfgKey.Close()
        }

        $tokenKey.Close()
        $tokens += $entry
    }

    $rootKey.Close()
    return $tokens
}

function To-RegistryValueKind([string]$kind) {
    switch ($kind) {
        "DWord" { return [Microsoft.Win32.RegistryValueKind]::DWord }
        "QWord" { return [Microsoft.Win32.RegistryValueKind]::QWord }
        "ExpandString" { return [Microsoft.Win32.RegistryValueKind]::ExpandString }
        "MultiString" { return [Microsoft.Win32.RegistryValueKind]::MultiString }
        "Binary" { return [Microsoft.Win32.RegistryValueKind]::Binary }
        default { return [Microsoft.Win32.RegistryValueKind]::String }
    }
}

function Write-TokensToHklm([array]$tokens, [bool]$clearHklm) {
    $subPath = "SOFTWARE\Microsoft\Speech\Voices\Tokens"
    $rootKey = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($subPath)
    if ($null -eq $rootKey) {
        throw "Failed to open/create HKLM:\\$subPath"
    }

    if ($clearHklm) {
        foreach ($name in $rootKey.GetSubKeyNames()) {
            if ($name -like "Sherpa-*") {
                $rootKey.DeleteSubKeyTree($name, $false)
            }
        }
    }

    $count = 0
    foreach ($t in $tokens) {
        $tokenKey = $rootKey.CreateSubKey([string]$t.TokenName)
        foreach ($v in @($t.RootValues)) {
            $name = [string]$v.Name
            $kind = To-RegistryValueKind ([string]$v.Kind)
            $tokenKey.SetValue($name, $v.Value, $kind)
        }

        $attrKey = $tokenKey.CreateSubKey("Attributes")
        foreach ($v in @($t.Attributes)) {
            $name = [string]$v.Name
            $kind = To-RegistryValueKind ([string]$v.Kind)
            $attrKey.SetValue($name, $v.Value, $kind)
        }
        $attrKey.Close()

        $cfgKey = $tokenKey.CreateSubKey("NaturalVoiceConfig")
        foreach ($v in @($t.NaturalVoiceConfig)) {
            $name = [string]$v.Name
            $kind = To-RegistryValueKind ([string]$v.Kind)
            $cfgKey.SetValue($name, $v.Value, $kind)
        }
        $cfgKey.Close()
        $tokenKey.Close()
        $count++
    }

    $rootKey.Close()
    return $count
}

if (-not (Test-IsAdmin) -and [string]::IsNullOrWhiteSpace($ImportFile)) {
    $tokens = Export-HkcuSherpaTokens
    if ($tokens.Count -eq 0) {
        Write-Host "No HKCU Sherpa-* tokens found to promote." -ForegroundColor Yellow
        exit 1
    }

    $tmp = Join-Path $env:TEMP ("sherpa_tokens_" + [guid]::NewGuid().ToString("N") + ".json")
    $tokens | ConvertTo-Json -Depth 8 | Out-File -FilePath $tmp -Encoding UTF8
    Write-Host "Exported $($tokens.Count) HKCU tokens to $tmp" -ForegroundColor Cyan
    Write-Host "Relaunching elevated to write HKLM..." -ForegroundColor Yellow

    $args = @("-NoProfile","-ExecutionPolicy","Bypass","-File","`"$PSCommandPath`"","-ImportFile","`"$tmp`"")
    if ($ClearHklm) { $args += "-ClearHklm" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args | Out-Null
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ImportFile)) {
    Write-Host "ImportFile is required in elevated phase." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $ImportFile)) {
    Write-Host "Import file not found: $ImportFile" -ForegroundColor Red
    exit 1
}

$tokens = Get-Content $ImportFile -Raw | ConvertFrom-Json
$tokenArray = @($tokens)
$written = Write-TokensToHklm -tokens $tokenArray -clearHklm:$ClearHklm
Write-Host "Promoted $written Sherpa token(s) to HKLM." -ForegroundColor Green

try { Remove-Item $ImportFile -Force -ErrorAction SilentlyContinue } catch {}

Write-Host ""
Write-Host "Verify with:" -ForegroundColor Yellow
Write-Host "  reg query `"HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens`" /s | findstr /I `"Sherpa-`"" -ForegroundColor Yellow
