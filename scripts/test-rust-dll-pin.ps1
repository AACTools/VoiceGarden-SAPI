# Tests for scripts\Get-RustTtsWrapperDll.ps1 (issue #15: CI must ship the
# csproj-pinned rust_tts_wrapper.dll, never "whatever is cached").
#
# Usage: pwsh -File scripts\test-rust-dll-pin.ps1  (exit 0 = pass)
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Get-RustTtsWrapperDll.ps1'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) "vg-rust-pin-test-$(Get-Random)"
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
function Assert-Throw($block, $msg) {
    try { & $block } catch { return }
    throw $msg
}

try {
    New-Item -ItemType Directory -Force -Path "$temp\cache" | Out-Null

    # Fake csproj pinning 9.9.9
    $csproj = "$temp\fake.csproj"
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="RustTtsWrapper.Bindings" Version="9.9.9" />
  </ItemGroup>
</Project>
'@ | Set-Content $csproj

    # Fake package cache with 1.0.0 (stale) and 9.9.9 (pinned)
    foreach ($v in '1.0.0', '9.9.9') {
        $dir = "$temp\cache\rustttswrapper.bindings\$v\runtimes\win-x64\native"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Content (Join-Path $dir 'rust_tts_wrapper.dll') "fake-$v"
    }

    It 'resolves the pinned version, not the cached newest/oldest' {
        $dll = & $script -Rid win-x64 -Csproj $csproj -PackagesDir "$temp\cache"
        Assert-True ($dll -like '*\9.9.9\runtimes\win-x64\native\rust_tts_wrapper.dll') "got: $dll"
    }

    It 'returns $null when the pinned version is not restored' {
        $csproj2 = "$temp\fake-v8.csproj"
        (Get-Content $csproj -Raw) -replace '9\.9\.9', '8.8.8' | Set-Content $csproj2
        $dll = & $script -Rid win-x64 -Csproj $csproj2 -PackagesDir "$temp\cache" 3>$null
        Assert-True (-not $dll) "expected no result, got: $dll"
    }

    It 'returns $null when the RID is missing from the pinned package' {
        $dll = & $script -Rid win-x86 -Csproj $csproj -PackagesDir "$temp\cache" 3>$null
        Assert-True (-not $dll) "expected no result for win-x86, got: $dll"
    }

    It 'rejects floating versions' {
        $csproj3 = "$temp\fake-float.csproj"
        (Get-Content $csproj -Raw) -replace '9\.9\.9', '9.9.*' | Set-Content $csproj3
        Assert-Throw { & $script -Rid win-x64 -Csproj $csproj3 -PackagesDir "$temp\cache" | Out-Null } 'floating version should throw'
    }
} finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}

if ($failures) { exit 1 } else { Write-Host "All pin tests passed." -ForegroundColor Green; exit 0 }
