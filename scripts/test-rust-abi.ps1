# ABI test for the shipped rust_tts_wrapper.dll (issue #15).
#
# Verifies, WITHOUT loading it into this process:
#   1. Every symbol the C++ loader resolves is exported.
#   2. The ABI canary symbol (tts_set_on_mark) is present — it only exists
#      in DLLs built with the consolidated 7-arg boundary callback
#      (rust-tts-wrapper#31). A DLL that fails this check would deliver
#      garbage charOffset/charLen to the boundary lambda.
#   3. Negative test: a pre-consolidation DLL (0.3.16 in the NuGet cache,
#      if present) must FAIL the same check.
#
# Usage: pwsh -File scripts\test-rust-abi.ps1 [-DllPath path]
#        (default: csproj-pinned package via Get-RustTtsWrapperDll.ps1)
param([string]$DllPath)

$ErrorActionPreference = 'Stop'
$failures = 0

function It($name, [scriptblock]$body) {
    try {
        & $body
        Write-Host "  PASS $name" -ForegroundColor Green
    } catch {
        $script:failures++
        Write-Host "  FAIL $name : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-True($cond, $msg) { if (-not $cond) { throw $msg } }

function Get-Exports([string]$path) {
    # Parse PE export table via .NET — no dumpbin dependency.
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    Assert-True ($bytes[$peOffset] -eq 0x50 -and $bytes[$peOffset + 1] -eq 0x45) "not a PE file"
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $optHeaderOffset = $peOffset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optHeaderOffset)
    $is64 = ($magic -eq 0x20B)
    $dataDirOffset = $optHeaderOffset + $(if ($is64) { 112 } else { 96 })
    $exportRva = [BitConverter]::ToUInt32($bytes, $dataDirOffset)
    # Section table to convert RVA -> file offset
    $numSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $sizeOfOptional = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $sectionsOffset = $peOffset + 24 + $sizeOfOptional
    function RvaToOffset([uint32]$rva) {
        for ($s = 0; $s -lt $numSections; $s++) {
            $so = $sectionsOffset + $s * 40
            $vaddr = [BitConverter]::ToUInt32($bytes, $so + 12)
            $vsize = [BitConverter]::ToUInt32($bytes, $so + 8)
            $rawPtr = [BitConverter]::ToUInt32($bytes, $so + 20)
            if ($rva -ge $vaddr -and $rva -lt ($vaddr + $vsize)) {
                return [int]($rawPtr + ($rva - $vaddr))
            }
        }
        throw "RVA 0x$($rva.ToString('X')) not in any section"
    }
    if ($exportRva -eq 0) { return @{} }
    $dir = RvaToOffset $exportRva
    $numNames = [BitConverter]::ToUInt32($bytes, $dir + 24)
    $namesRva = [BitConverter]::ToUInt32($bytes, $dir + 32)
    $namesOff = RvaToOffset $namesRva
    $exports = @{}
    for ($i = 0; $i -lt $numNames; $i++) {
        $nameRva = [BitConverter]::ToUInt32($bytes, $namesOff + $i * 4)
        $nameOff = RvaToOffset $nameRva
        $end = $nameOff
        while ($bytes[$end] -ne 0) { $end++ }
        $name = [System.Text.Encoding]::ASCII.GetString($bytes, $nameOff, $end - $nameOff)
        $exports[$name] = $true
    }
    return $exports
}

# Resolve the DLL under test
if (-not $DllPath) {
    $DllPath = & "$PSScriptRoot\Get-RustTtsWrapperDll.ps1" -Rid win-x64
    if (-not $DllPath) { throw "could not resolve the pinned rust_tts_wrapper.dll (run dotnet restore first)" }
}
Write-Host "ABI check: $DllPath"

$required = @(
    'tts_create', 'tts_destroy', 'tts_speak', 'tts_speak_ssml', 'tts_speak_sync',
    'tts_stop', 'tts_set_voice', 'tts_set_rate', 'tts_set_pitch', 'tts_set_volume',
    'tts_set_on_audio', 'tts_set_on_boundary', 'tts_set_on_viseme',
    'tts_set_on_start', 'tts_set_on_end', 'tts_set_on_error', 'tts_get_last_error'
)
$canary = 'tts_set_on_mark'
$floravox = 'tts_get_engines'  # enumeration API needed for engine discovery

It 'exports every symbol the C++ loader resolves' {
    $exports = Get-Exports $DllPath
    foreach ($sym in $required) {
        Assert-True ($exports.ContainsKey($sym)) "missing export: $sym"
    }
}

It 'ABI canary: tts_set_on_mark present (consolidated boundary callback)' {
    $exports = Get-Exports $DllPath
    Assert-True ($exports.ContainsKey($canary)) "$canary missing - DLL predates the 7-arg boundary ABI; the loader would read garbage offsets"
}

It 'exports tts_get_engines (engine enumeration)' {
    $exports = Get-Exports $DllPath
    Assert-True ($exports.ContainsKey($floravox)) "$floravox missing"
}

# Negative control: the oldest cached package must fail the canary.
$oldDll = Get-ChildItem "$env:USERPROFILE\.nuget\packages\rustttswrapper.bindings\0.3.*\runtimes\win-x64\native\rust_tts_wrapper.dll" -ErrorAction SilentlyContinue |
    Sort-Object FullName | Select-Object -First 1
if ($oldDll) {
    It "negative control: pre-consolidation DLL ($($oldDll.FullName.Split('\')[-4])) is rejected by the canary" {
        $exports = Get-Exports $oldDll.FullName
        Assert-True (-not $exports.ContainsKey($canary)) "old DLL unexpectedly has $canary - the canary no longer discriminates!"
    }
} else {
    Write-Host "  SKIP negative control (no 0.3.x package cached)" -ForegroundColor DarkGray
}

if ($failures) { exit 1 } else { Write-Host "All ABI tests passed." -ForegroundColor Green; exit 0 }
