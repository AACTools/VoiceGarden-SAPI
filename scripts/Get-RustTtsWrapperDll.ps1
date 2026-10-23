# Resolves the rust_tts_wrapper.dll path for a RID from the NuGet cache,
# pinned to the exact RustTtsWrapper.Bindings version referenced by
# VoiceGarden.UI.csproj. Never falls back to "whatever is cached" — a
# mismatch between the csproj pin and the shipped DLL is an ABI hazard
# (rust-tts-wrapper#31 boundary-callback consolidation).
#
# Usage:
#   $dll = & scripts\Get-RustTtsWrapperDll.ps1 -Rid win-x64 [-Csproj path]
#   if ($dll) { Copy-Item $dll ... } else { # error handling }
#
# Companion (ships the whole native dir incl. engine runtime DLLs):
#   & scripts\Get-RustTtsWrapperDll.ps1 -Rid win-x64 -CopyAllNativeTo payload\x64
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-x86', 'win-arm64')] [string]$Rid,
    [string]$Csproj = "$PSScriptRoot\..\VoiceGarden.UI\VoiceGarden.UI.csproj",
    # Override for tests (point at a fabricated package cache).
    [string]$PackagesDir = (Join-Path $env:USERPROFILE ".nuget\packages"),
    # When set, copies every native DLL of the pinned package into this
    # directory (engine-qwen3tts / engine-omnivoice / engine-ggml* need to
    # sit beside rust_tts_wrapper.dll) and emits nothing.
    [string]$CopyAllNativeTo
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Csproj)) { throw "csproj not found: $Csproj" }

# PackageId case in the cache dir is lowercased on all NuGet hosts we use.
$pkgId = 'rustttswrapper.bindings'

# Parse the pinned version (honours VersionOverride-style children too).
$project = [xml](Get-Content $Csproj -Raw)
$pkgVersion = $null
foreach ($group in $project.Project.ItemGroup.PackageReference | Where-Object { $_ }) {
    $ref = @($group) | Where-Object { $_.Include -eq 'RustTtsWrapper.Bindings' }
    if ($ref) { $pkgVersion = $ref.Version; break }
}
if (-not $pkgVersion) { throw "RustTtsWrapper.Bindings not referenced in $Csproj" }

# Strip NuGet floating-version suffixes (e.g. "0.5.*" -> not supported: pin exactly).
if ($pkgVersion -match '[*^~]') {
    throw "RustTtsWrapper.Bindings version '$pkgVersion' is floating; pin an exact version (ABI canary)."
}

$pkgDir = Join-Path $PackagesDir "$pkgId\$pkgVersion"
$nativeDir = Join-Path $pkgDir "runtimes\$Rid\native"
$dll = Join-Path $nativeDir "rust_tts_wrapper.dll"

if ($CopyAllNativeTo) {
    if (-not (Test-Path $dll)) {
        Write-Warning "Package $pkgVersion has no runtimes\$Rid\native\rust_tts_wrapper.dll; nothing to copy"
        Write-Output $null
        return
    }
    New-Item -ItemType Directory -Force -Path $CopyAllNativeTo | Out-Null
    foreach ($f in Get-ChildItem $nativeDir -Filter *.dll) {
        Copy-Item $f.FullName (Join-Path $CopyAllNativeTo $f.Name) -Force
    }
    # COMPAT (rust-tts-wrapper#62): 1.6.4 x64 packages the engine runtime
    # DLLs with an engine- prefix while the import tables require the linker
    # names - rust_tts_wrapper.dll imports qwen3tts.dll/omnivoice.dll, and
    # qwen3tts.dll imports ggml.dll/ggml-base.dll/ggml-cpu.dll. LoadLibrary
    # fails with 126 without these aliases. No-op once the wrapper ships the
    # linker-named files.
    $aliases = @{
        'engine-qwen3tts.dll' = 'qwen3tts.dll'
        'engine-omnivoice.dll' = 'omnivoice.dll'
        'engine-ggml.dll' = 'ggml.dll'
        'engine-ggml-base.dll' = 'ggml-base.dll'
        'engine-ggml-cpu.dll' = 'ggml-cpu.dll'
    }
    foreach ($src in @($aliases.Keys)) {
        $srcPath = Join-Path $CopyAllNativeTo $src
        $dstPath = Join-Path $CopyAllNativeTo $aliases[$src]
        if ((Test-Path $srcPath) -and -not (Test-Path $dstPath)) {
            Copy-Item $srcPath $dstPath -Force
            Write-Verbose "compat alias: $src -> $($aliases[$src])"
        }
    }
    Write-Output $null
    return
}

if (Test-Path $dll) {
    Write-Verbose "Resolved $Rid -> $dll"
    Write-Output $dll
    return
}

if (-not (Test-Path $pkgDir)) {
    Write-Warning "RustTtsWrapper.Bindings $pkgVersion is not in the NuGet cache. Run: dotnet restore $Csproj"
} else {
    Write-Warning "Package $pkgVersion restored but has no runtimes\$Rid\native\rust_tts_wrapper.dll"
}
Write-Output $null
