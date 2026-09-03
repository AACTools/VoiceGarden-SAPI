# Resolves the rust_tts_wrapper.dll path for a RID from the NuGet cache,
# pinned to the exact RustTtsWrapper.Bindings version referenced by
# VoiceGarden.UI.csproj. Never falls back to "whatever is cached" — a
# mismatch between the csproj pin and the shipped DLL is an ABI hazard
# (rust-tts-wrapper#31 boundary-callback consolidation).
#
# Usage:
#   $dll = & scripts\Get-RustTtsWrapperDll.ps1 -Rid win-x64 [-Csproj path]
#   if ($dll) { Copy-Item $dll ... } else { # error handling }
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-x86', 'win-arm64')] [string]$Rid,
    [string]$Csproj = "$PSScriptRoot\..\VoiceGarden.UI\VoiceGarden.UI.csproj",
    # Override for tests (point at a fabricated package cache).
    [string]$PackagesDir = (Join-Path $env:USERPROFILE ".nuget\packages")
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
$dll = Join-Path $pkgDir "runtimes\$Rid\native\rust_tts_wrapper.dll"

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
